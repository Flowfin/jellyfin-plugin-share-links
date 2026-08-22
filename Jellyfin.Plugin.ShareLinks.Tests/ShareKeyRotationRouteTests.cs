using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Database.Implementations.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Net;
using MediaBrowser.Controller.Session;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Users;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.ShareLinks.Tests;

/// <summary>
/// What the rotation route does to the key, to the records and to the guests
/// (#28, #243).
/// </summary>
/// <remarks>
/// <para>
/// The action is driven directly rather than through a server, which is what
/// <c>docs/testing.md</c> requires. The key file and the store file are real
/// files under the temporary directory, because what this route is about is two
/// writes landing or not landing, and a fake that cannot fail to write would make
/// the case worth testing unreachable.
/// </para>
/// <para>
/// The claim these tests carry is the one an operator is asked to believe: a link
/// that resolved before the press does not resolve after it. That is asserted by
/// resolving the same token twice against the same records, once with the key the
/// share was issued under and once with the key the file holds afterwards, so a
/// rotation that wrote the same bytes back would fail here rather than pass.
/// </para>
/// <para>
/// What is not reached from here. Whether the server actually ends a session it
/// was asked to end is the server's, and <c>docs/refused-tests.md</c> is where
/// that refusal is written down with what stands in for it: these tests judge
/// which accounts this plugin asked about, in what order, and nothing further.
/// </para>
/// </remarks>
public sealed class ShareKeyRotationRouteTests : IDisposable
{
    private static readonly DateTimeOffset Now = new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid Operator = new Guid("33333333-3333-3333-3333-333333333333");
    private static readonly Guid AnotherOperator = new Guid("44444444-4444-4444-4444-444444444444");
    private static readonly Guid Invited = new Guid("11111111-1111-1111-1111-111111111111");
    private static readonly Guid AnotherInvited = new Guid("22222222-2222-2222-2222-222222222222");
    private static readonly Guid Item = new Guid("55555555-5555-5555-5555-555555555555");

    private readonly string _directory;

    public ShareKeyRotationRouteTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "share-links-rotation-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // A leftover directory under the temporary directory is not worth
            // failing a green suite over.
        }
    }

    private string StorePath => Path.Combine(_directory, PluginServiceRegistrator.StoreFileName);

    private string KeyPath => Path.Combine(_directory, PluginServiceRegistrator.KeyFileName);

    /// <summary>
    /// The whole of what an operator is promised: a link that opened before the
    /// press does not open after it. The same token is resolved twice against the
    /// same records, under the key it was issued with and then under the key the
    /// file holds afterwards, so a rotation that wrote the old bytes back is
    /// caught here rather than believed.
    /// </summary>
    /// <returns>A task that completes when the assertions have been made.</returns>
    [Fact]
    public async Task AShareThatResolvedBeforeTheRotationDoesNotResolveAfterIt()
    {
        var keyFile = new ShareKeyFile(KeyPath);
        var issued = keyFile.Read();
        var token = ShareTokens.Mint();

        using var store = new ShareStore(StorePath);
        await store.MutateAsync(_ => new[] { Live(issued, token) }, CancellationToken.None).ConfigureAwait(false);

        var before = ShareResolution.Resolve(
            await store.ReadAsync(CancellationToken.None).ConfigureAwait(false),
            issued,
            token,
            Invited,
            PluginStatus.Active,
            At(Now));

        Assert.True(before.IsResolved);

        var answer = await Controller(store, keyFile).RotateKey(CancellationToken.None).ConfigureAwait(false);

        var rotated = Assert.IsType<ShareKeyRotated>(Assert.IsType<OkObjectResult>(answer.Result).Value);
        Assert.Equal(ShareKeyRotationOutcome.Rotated, rotated.Outcome);
        Assert.Equal(1, rotated.SharesStopped);

        var after = ShareResolution.Resolve(
            await store.ReadAsync(CancellationToken.None).ConfigureAwait(false),
            keyFile.Read(),
            token,
            Invited,
            PluginStatus.Active,
            At(Now));

        Assert.False(after.IsResolved);

        // Which refusal, and not merely that there was one. The rotation also
        // revokes the record, so a run that never wrote the key would still refuse
        // this token and this test would pass on the wrong half. Under the key on
        // disk afterwards the token matches no hash at all, which is the half only
        // a replaced key produces.
        Assert.Equal(ShareRefusal.NoSuchShare, after.Refusal);
        Assert.NotEqual(issued, keyFile.Read());
    }

    /// <summary>
    /// The number the route answers with is the count of the shares that stopped,
    /// and not the count of the records the store holds. A store carrying a live
    /// share, an expired one and one somebody revoked earlier answers with one.
    /// </summary>
    /// <returns>A task that completes when the assertions have been made.</returns>
    [Fact]
    public async Task TheNumberAnsweredIsTheCountOfTheSharesThisCallStopped()
    {
        var keyFile = new ShareKeyFile(KeyPath);
        var key = keyFile.Read();

        var live = Live(key, ShareTokens.Mint());
        var expired = Live(key, ShareTokens.Mint(), expiresAt: Now.AddDays(-1));
        var revoked = Live(key, ShareTokens.Mint(), revokedAt: Now.AddDays(-2));

        using var store = new ShareStore(StorePath);
        await store.MutateAsync(_ => new[] { live, expired, revoked }, CancellationToken.None).ConfigureAwait(false);

        var answer = await Controller(store, keyFile).RotateKey(CancellationToken.None).ConfigureAwait(false);

        var rotated = Assert.IsType<ShareKeyRotated>(Assert.IsType<OkObjectResult>(answer.Result).Value);
        Assert.Equal(1, rotated.SharesStopped);

        var records = await store.ReadAsync(CancellationToken.None).ConfigureAwait(false);

        // The one that had already been revoked keeps the revoker and the instant
        // it had. A rotation that rewrote it would be claiming to have stopped a
        // share it did not stop.
        Assert.Equal(AnotherOperator, records.Single(record => record.Id == revoked.Id).RevokedByUserId);
        Assert.Equal(Now.AddDays(-2), records.Single(record => record.Id == revoked.Id).RevokedAt);

        // The expired one is untouched. It stopped on its own instant, and a
        // revocation stamped on it now would tell an operator a person stopped it.
        Assert.Null(records.Single(record => record.Id == expired.Id).RevokedAt);

        Assert.Equal(Now, records.Single(record => record.Id == live.Id).RevokedAt);
    }

    /// <summary>
    /// Every record this call stopped says the key rotation stopped it, so an
    /// operator reading the listing afterwards can tell those apart from a share
    /// somebody revoked by hand.
    /// </summary>
    /// <returns>A task that completes when the assertions have been made.</returns>
    [Fact]
    public async Task AStoppedRecordSaysTheRotationIsWhatStoppedIt()
    {
        var keyFile = new ShareKeyFile(KeyPath);
        var key = keyFile.Read();

        using var store = new ShareStore(StorePath);
        await store.MutateAsync(_ => new[] { Live(key, ShareTokens.Mint()) }, CancellationToken.None).ConfigureAwait(false);

        await Controller(store, keyFile).RotateKey(CancellationToken.None).ConfigureAwait(false);

        var record = Assert.Single(await store.ReadAsync(CancellationToken.None).ConfigureAwait(false));

        Assert.Equal(Operator, record.RevokedByUserId);
        Assert.Equal("the keyed hash secret was rotated", record.RevocationReason);
        Assert.Equal(ShareState.Revoked, ShareBounds.StateOf(record, Now));
    }

    /// <summary>
    /// The named half-landed state. The key file cannot be written, the records
    /// are stopped anyway, and what comes back says exactly that rather than
    /// reading as either a rotation or a call that did nothing.
    /// </summary>
    /// <returns>A task that completes when the assertions have been made.</returns>
    [Fact]
    public async Task AKeyThatCannotBeWrittenLeavesTheSharesStoppedAndSaysSo()
    {
        var issued = ShareTokens.MintKeyBytes();
        var token = ShareTokens.Mint();

        using var store = new ShareStore(StorePath);
        await store.MutateAsync(_ => new[] { Live(issued, token) }, CancellationToken.None).ConfigureAwait(false);

        // A directory standing where the key file goes. The write fails, and the
        // read that would have failed first is never made, because rotation
        // replaces the key rather than reading it.
        var occupied = Path.Combine(_directory, "occupied");
        Directory.CreateDirectory(occupied);

        var answer = await Controller(store, new ShareKeyFile(occupied)).RotateKey(CancellationToken.None).ConfigureAwait(false);

        var refused = Assert.IsType<ObjectResult>(answer.Result);
        Assert.Equal(StatusCodes.Status500InternalServerError, refused.StatusCode);

        var rotated = Assert.IsType<ShareKeyRotated>(refused.Value);
        Assert.Equal(ShareKeyRotationOutcome.SharesStoppedKeyKept, rotated.Outcome);
        Assert.Equal(1, rotated.SharesStopped);

        // The half that did land, asserted rather than assumed: the share is
        // stopped, so the link does not open even though the key it was issued
        // under is still the key on disk.
        var after = ShareResolution.Resolve(
            await store.ReadAsync(CancellationToken.None).ConfigureAwait(false),
            issued,
            token,
            Invited,
            PluginStatus.Active,
            At(Now));

        Assert.Equal(ShareRefusal.Revoked, after.Refusal);
    }

    /// <summary>
    /// A store that cannot be written leaves the key alone. The other order would
    /// replace the key over records that still read live, which is the state the
    /// route's order exists to prevent, and the answer carries no count because
    /// nothing was stopped.
    /// </summary>
    /// <returns>A task that completes when the assertions have been made.</returns>
    [Fact]
    public async Task AStoreThatCannotBeWrittenLeavesTheKeyWhereItWas()
    {
        var keyFile = new ShareKeyFile(KeyPath);
        var before = keyFile.Read();

        var answer = await Controller(new AStoreThatCannotBeWritten(), keyFile).RotateKey(CancellationToken.None).ConfigureAwait(false);

        var refused = Assert.IsType<StatusCodeResult>(answer.Result);
        Assert.Equal(StatusCodes.Status500InternalServerError, refused.StatusCode);
        Assert.Equal(before, keyFile.Read());
    }

    /// <summary>
    /// The guests of every stopped share are signed out and disabled, which is
    /// what revoking those same shares one at a time would have done. A rotation
    /// that stopped every share and left every guest watching would behave
    /// differently from the revocation it is a bulk form of.
    /// </summary>
    /// <returns>A task that completes when the assertions have been made.</returns>
    [Fact]
    public async Task TheGuestsOfEveryStoppedShareAreSignedOutAndDisabled()
    {
        var keyFile = new ShareKeyFile(KeyPath);
        var key = keyFile.Read();

        var first = Live(key, ShareTokens.Mint(), invited: Invited, pluginCreated: true);
        var second = Live(key, ShareTokens.Mint(), invited: AnotherInvited, pluginCreated: true);

        using var store = new ShareStore(StorePath);
        await store.MutateAsync(_ => new[] { first, second }, CancellationToken.None).ConfigureAwait(false);

        var sessions = new RecordingSessions();
        var accounts = new RecordingAccounts();
        accounts.Carries[Invited] = 1;
        accounts.Carries[AnotherInvited] = 1;

        await Controller(store, keyFile, Operator, sessions, accounts).RotateKey(CancellationToken.None).ConfigureAwait(false);

        Assert.Equal(new[] { Invited, AnotherInvited }, sessions.Revoked);
        Assert.Equal(new[] { Invited, AnotherInvited }, accounts.Written);
    }

    /// <summary>
    /// A caller the server has not identified is refused, and nothing is changed.
    /// The elevation policy has already refused one, so this is the guard against
    /// writing the empty identifier into the field that says who stopped every
    /// share on the server.
    /// </summary>
    /// <returns>A task that completes when the assertions have been made.</returns>
    [Fact]
    public async Task ACallerTheServerHasNotIdentifiedIsRefusedAndChangesNothing()
    {
        var keyFile = new ShareKeyFile(KeyPath);
        var key = keyFile.Read();

        using var store = new ShareStore(StorePath);
        await store.MutateAsync(_ => new[] { Live(key, ShareTokens.Mint()) }, CancellationToken.None).ConfigureAwait(false);

        var answer = await Controller(store, keyFile, caller: null).RotateKey(CancellationToken.None).ConfigureAwait(false);

        Assert.Equal(StatusCodes.Status403Forbidden, Assert.IsType<StatusCodeResult>(answer.Result).StatusCode);
        Assert.Equal(key, keyFile.Read());
        Assert.Null(Assert.Single(await store.ReadAsync(CancellationToken.None).ConfigureAwait(false)).RevokedAt);
    }

    /// <summary>
    /// A rotation on a server with nothing live answers with zero rather than
    /// refusing, and still replaces the key. The reason to rotate is the key and
    /// not the shares.
    /// </summary>
    /// <returns>A task that completes when the assertions have been made.</returns>
    [Fact]
    public async Task ARotationWithNothingLiveReplacesTheKeyAndAnswersWithZero()
    {
        var keyFile = new ShareKeyFile(KeyPath);
        var before = keyFile.Read();

        using var store = new ShareStore(StorePath);

        var answer = await Controller(store, keyFile).RotateKey(CancellationToken.None).ConfigureAwait(false);

        var rotated = Assert.IsType<ShareKeyRotated>(Assert.IsType<OkObjectResult>(answer.Result).Value);
        Assert.Equal(0, rotated.SharesStopped);
        Assert.Equal(ShareKeyRotationOutcome.Rotated, rotated.Outcome);
        Assert.NotEqual(before, keyFile.Read());
    }

    private static TimeProvider At(DateTimeOffset instant)
    {
        var clock = new Mock<TimeProvider>();
        clock.Setup(provider => provider.GetUtcNow()).Returns(instant);
        return clock.Object;
    }

    private static IAuthorizationContext ContextFor(Guid? caller)
    {
        var authorization = new AuthorizationInfo
        {
            IsAuthenticated = caller is not null,
            User = caller is { } identified
                ? new User("an administrator", "provider", "reset") { Id = identified }
                : null,
        };

        var context = new Mock<IAuthorizationContext>();
        context.Setup(c => c.GetAuthorizationInfo(It.IsAny<HttpRequest>()))
            .ReturnsAsync(authorization);

        return context.Object;
    }

    // One record, in whichever of the four states a rotation has to tell apart.
    // Written out here rather than mutated at each call site, because the type is
    // init-only on purpose and a copy helper would be a second place a field can
    // be forgotten.
    private ShareRecord Live(
        byte[] key,
        string token,
        DateTimeOffset? expiresAt = null,
        DateTimeOffset? revokedAt = null,
        Guid? invited = null,
        bool pluginCreated = false)
    {
        var account = invited ?? Invited;

        return new ShareRecord
        {
            SchemaVersion = ShareRecord.CurrentSchemaVersion,
            Id = Guid.NewGuid(),
            ItemId = Item,
            InvitedUserIds = new[] { account },
            PluginCreatedUserIds = pluginCreated ? new[] { account } : Array.Empty<Guid>(),
            CreatedByUserId = Operator,
            CreatedAt = Now.AddDays(-2),
            ExpiresAt = expiresAt ?? Now.AddDays(7),
            RevokedAt = revokedAt,
            RevokedByUserId = revokedAt is null ? null : AnotherOperator,
            TokenHash = ShareTokenHash.Compute(key, token),
        };
    }

    private ShareLinksAdminController Controller(IShareStore store, ShareKeyFile keyFile)
        => Controller(store, keyFile, Operator, new RecordingSessions(), new RecordingAccounts());

    private ShareLinksAdminController Controller(IShareStore store, ShareKeyFile keyFile, Guid? caller)
        => Controller(store, keyFile, caller, new RecordingSessions(), new RecordingAccounts());

    // The library and the configuration are strict fakes that answer nothing, so
    // a rotation that reached for either fails here rather than passing quietly.
    // Rotation is about the store and the key file and must touch neither.
    private ShareLinksAdminController Controller(
        IShareStore store,
        ShareKeyFile keyFile,
        Guid? caller,
        RecordingSessions sessions,
        RecordingAccounts accounts)
        => new ShareLinksAdminController(
            store,
            keyFile,
            accounts.Manager,
            Mock.Of<ILibraryManager>(MockBehavior.Strict),
            Mock.Of<IPluginConfigurationSource>(MockBehavior.Strict),
            ContextFor(caller),
            sessions.Manager,
            At(Now),
            NullLogger<ShareLinksAdminController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };

    // A store whose write always fails, so the route meets the state where
    // nothing was stopped. The read succeeds, because a store that could not be
    // read is a different answer on a different line.
    private sealed class AStoreThatCannotBeWritten : IShareStore
    {
        public Task<IReadOnlyList<ShareRecord>> ReadAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<ShareRecord>>(Array.Empty<ShareRecord>());

        public Task<IReadOnlyList<ShareRecord>> MutateAsync(
            Func<IReadOnlyList<ShareRecord>, IReadOnlyList<ShareRecord>> change,
            CancellationToken cancellationToken = default)
            => throw new ShareStoreUnwritableException("a path", "the write failed", null);
    }

    // The accounts a server holds, as far as this route can see them, and which
    // of them had a policy written onto them. Strict, so a route reaching for a
    // member nobody expected fails here rather than passing quietly.
    private sealed class RecordingAccounts
    {
        private readonly Mock<IUserManager> _manager = new Mock<IUserManager>(MockBehavior.Strict);

        public RecordingAccounts()
        {
            _manager
                .Setup(manager => manager.GetUserById(It.IsAny<Guid>()))
                .Returns((Guid id) => Carries.TryGetValue(id, out var ceiling)
                    ? new User("a guest", "provider", "reset") { Id = id, MaxActiveSessions = ceiling }
                    : null);

            _manager
                .Setup(manager => manager.UpdatePolicyAsync(It.IsAny<Guid>(), It.IsAny<UserPolicy>()))
                .Callback((Guid id, UserPolicy policy) => Written.Add(id))
                .Returns(Task.CompletedTask);
        }

        public IUserManager Manager => _manager.Object;

        public List<Guid> Written { get; } = new List<Guid>();

        public Dictionary<Guid, int> Carries { get; } = new Dictionary<Guid, int>();
    }

    // A session manager that answers every ask and writes down which accounts it
    // was asked about, in the order it was asked.
    private sealed class RecordingSessions
    {
        private readonly List<Guid> _revoked = new List<Guid>();
        private readonly Mock<ISessionManager> _manager = new Mock<ISessionManager>();

        public RecordingSessions()
        {
            _manager
                .Setup(manager => manager.RevokeUserTokens(It.IsAny<Guid>(), It.IsAny<string>()))
                .Callback((Guid account, string spared) => _revoked.Add(account))
                .Returns(Task.CompletedTask);
        }

        public ISessionManager Manager => _manager.Object;

        public IReadOnlyList<Guid> Revoked => _revoked;
    }
}
