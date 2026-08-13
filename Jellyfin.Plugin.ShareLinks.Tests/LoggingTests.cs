using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Database.Implementations.Entities;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Controller.Net;
using MediaBrowser.Model.Plugins;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.ShareLinks.Tests;

/// <summary>
/// What the four paths write, and what no line of it may carry (#27).
/// </summary>
/// <remarks>
/// <para>
/// Create, resolve, refuse and revoke are driven through the routines that
/// perform them rather than through <see cref="ShareLog"/>, because a test that
/// drove the logging routine directly would assert that the four lines are safe
/// and never that the four paths write them. The refusals are driven once per
/// reason, so a reason added later to <see cref="ShareRefusal"/> has to be given
/// a case here before this file compiles against the table below.
/// </para>
/// <para>
/// The never list is asserted twice over. Once as an absence, which is what the
/// issue asks for: the token, the stored hash and the accounts do not appear.
/// And once as a whitelist over the placeholder names, which is the half that
/// survives a field nobody has thought of yet - an item title added to a line
/// reddens <see cref="TheFieldsALineCarriesAreTheOnesThePolicyAllows"/> without
/// anybody having to add a title to the never list first.
/// </para>
/// </remarks>
public sealed class LoggingTests : IDisposable
{
    private static readonly DateTimeOffset Now = new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid Invited = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Stranger = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid Operator = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid Item = Guid.Parse("55555555-5555-5555-5555-555555555555");

    // The token every share below is minted with. It is a literal rather than a
    // minted one so that the search for it in a line is a search for a value
    // this file knows in full.
    private const string TheToken = "the-token-nobody-may-log";

    // Everything a line is allowed to carry, by placeholder name. Adding a name
    // here is the moment somebody decides a new kind of value may reach a log
    // file, and docs/logging.md is where that decision is argued.
    private static readonly string[] Allowed = ["Share", "Item", "Expires", "Invited", "Outcome", "Reason"];

    private readonly string _directory;
    private readonly ShareKeyFile _keyFile;
    private readonly byte[] _key;
    private readonly CapturingLogger _log = new CapturingLogger();

    public LoggingTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "share-links-logging-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        _keyFile = new ShareKeyFile(Path.Combine(_directory, PluginServiceRegistrator.KeyFileName));
        _key = _keyFile.Read();
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

    /// <summary>
    /// Each of the four paths writes at least one line. Every assertion below is
    /// over the lines this collects, so an empty capture would pass them all.
    /// </summary>
    /// <returns>A task that completes when the assertions have been made.</returns>
    [Fact]
    public async Task EachOfTheFourPathsWritesALine()
    {
        var before = 0;

        await Create().ConfigureAwait(true);
        Assert.True(_log.Lines.Count > before, "create wrote nothing");
        before = _log.Lines.Count;

        await Resolve().ConfigureAwait(true);
        Assert.True(_log.Lines.Count > before, "resolve wrote nothing");
        before = _log.Lines.Count;

        await Refuse(ShareRefusal.CallerNotInvited).ConfigureAwait(true);
        Assert.True(_log.Lines.Count > before, "refuse wrote nothing");
        before = _log.Lines.Count;

        await Revoke().ConfigureAwait(true);
        Assert.True(_log.Lines.Count > before, "revoke wrote nothing");
    }

    /// <summary>
    /// The clause this issue is about. Not one line of the four paths, at any
    /// level, carries the token that was presented or the token a share was
    /// minted with.
    /// </summary>
    /// <returns>A task that completes when the assertions have been made.</returns>
    [Fact]
    public async Task NoLineCarriesTheRawToken()
    {
        await EveryPath().ConfigureAwait(true);

        Assert.NotEmpty(_log.Lines);
        foreach (var line in _log.Lines)
        {
            Assert.DoesNotContain(TheToken, line.Text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("a-guessed-token", line.Text, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// The never list holds the keyed hash for the same reason it holds the
    /// token: a log joined to a leaked store is a lookup.
    /// </summary>
    /// <returns>A task that completes when the assertions have been made.</returns>
    [Fact]
    public async Task NoLineCarriesTheStoredHash()
    {
        var hash = ShareTokenHash.Compute(_key, TheToken);

        await EveryPath().ConfigureAwait(true);

        Assert.NotEmpty(_log.Lines);
        foreach (var line in _log.Lines)
        {
            Assert.DoesNotContain(hash, line.Text, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// A share is named by a prefix. The whole identifier is what the operator
    /// surface shows and is not what a line carries.
    /// </summary>
    /// <returns>A task that completes when the assertions have been made.</returns>
    [Fact]
    public async Task AShareIsNamedByItsPrefixAndNeverInFull()
    {
        var share = await EveryPath().ConfigureAwait(true);
        var whole = share.Id.ToString("N", CultureInfo.InvariantCulture);

        Assert.Contains(_log.Lines, line => line.Text.Contains(ShareLog.Name(share.Id), StringComparison.Ordinal));
        foreach (var line in _log.Lines)
        {
            Assert.DoesNotContain(whole, line.Text, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// Who was invited and who asked are not in any line. That association is
    /// the operator surface's, and <c>docs/personal-data.md</c> accounts for it
    /// there rather than in a file backup tooling copies around.
    /// </summary>
    /// <returns>A task that completes when the assertions have been made.</returns>
    [Fact]
    public async Task NoLineNamesAnAccount()
    {
        await EveryPath().ConfigureAwait(true);

        Assert.NotEmpty(_log.Lines);
        foreach (var line in _log.Lines)
        {
            foreach (var account in new[] { Invited, Stranger, Operator })
            {
                Assert.DoesNotContain(account.ToString("N", CultureInfo.InvariantCulture), line.Text, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain(account.ToString("D", CultureInfo.InvariantCulture), line.Text, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    /// <summary>
    /// The half of the never list that does not need the thing to be named in
    /// advance. Every placeholder any line emits is one the policy allows, so a
    /// line that grows a field - an item title being the one the decision on
    /// this issue names - reddens here.
    /// </summary>
    /// <returns>A task that completes when the assertions have been made.</returns>
    [Fact]
    public async Task TheFieldsALineCarriesAreTheOnesThePolicyAllows()
    {
        await EveryPath().ConfigureAwait(true);

        Assert.NotEmpty(_log.Lines);
        var carried = _log.Lines
            .SelectMany(line => line.Fields)
            .Where(name => !string.Equals(name, "{OriginalFormat}", StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(Allowed.Order(StringComparer.Ordinal).ToArray(), carried);
    }

    /// <summary>
    /// Every reason the decision can give reaches a line, and none of them
    /// reaches it as anything but the fixed code. A sentence assembled from the
    /// request is how a request's own bytes get into a log file.
    /// </summary>
    /// <returns>A task that completes when the assertions have been made.</returns>
    [Fact]
    public async Task EveryRefusalReasonIsWrittenAsItsFixedCode()
    {
        foreach (var reason in new[]
        {
            ShareRefusal.NoTokenPresented,
            ShareRefusal.NoSuchShare,
            ShareRefusal.Revoked,
            ShareRefusal.Expired,
            ShareRefusal.CallerNotSignedIn,
            ShareRefusal.CallerNotInvited,
            ShareRefusal.PluginNotActive,
        })
        {
            _log.Lines.Clear();
            await Refuse(reason).ConfigureAwait(true);

            var line = Assert.Single(_log.Lines);
            Assert.Contains(reason.ToString(), line.Text, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// A second press is a line as well, saying that it changed nothing. An
    /// operator who pressed twice cannot otherwise tell a press the server never
    /// received from one it received and agreed was already done.
    /// </summary>
    /// <returns>A task that completes when the assertions have been made.</returns>
    [Fact]
    public async Task ARevocationSaysWhatItDid()
    {
        using var store = new ShareStore(StorePath);
        var share = ARecord();
        await store.MutateAsync(_ => new[] { share }).ConfigureAwait(true);

        await store.RevokeAsync(share.Id, Operator, Now, _log).ConfigureAwait(true);
        await store.RevokeAsync(share.Id, Operator, Now, _log).ConfigureAwait(true);
        await store.RevokeAsync(Guid.NewGuid(), Operator, Now, _log).ConfigureAwait(true);

        Assert.Equal(
            new[] { ShareRevocation.Revoked, ShareRevocation.AlreadyStopped, ShareRevocation.NoSuchShare },
            _log.Lines.Select(line => Assert.IsType<ShareRevocation>(line.Value("Outcome"))).ToArray());
    }

    /// <summary>
    /// A store nobody can read is a state an operator has to act on, so it is
    /// louder than a refused token rather than indistinguishable from one.
    /// </summary>
    /// <returns>A task that completes when the assertions have been made.</returns>
    [Fact]
    public async Task AnUnreadableStoreIsAWarningRatherThanARefusal()
    {
        var store = new Mock<IShareStore>();
        store.Setup(s => s.ReadAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ShareStoreUnreadableException("the store is not readable", new IOException()));

        await Open(store.Object, TheToken, Invited).ConfigureAwait(true);

        var line = Assert.Single(_log.Lines);
        Assert.Equal(LogLevel.Warning, line.Level);
        Assert.DoesNotContain(line.Fields, name => !string.Equals(name, "{OriginalFormat}", StringComparison.Ordinal));
    }

    // Create, resolve, refuse and revoke, in one store, so that every assertion
    // above is made over the whole of what the four paths write rather than over
    // one of them at a time.
    private async Task<ShareRecord> EveryPath()
    {
        var share = await Create().ConfigureAwait(true);
        await Resolve().ConfigureAwait(true);
        await Refuse(ShareRefusal.CallerNotInvited).ConfigureAwait(true);
        await Refuse(ShareRefusal.NoSuchShare).ConfigureAwait(true);
        await Revoke().ConfigureAwait(true);
        await Resolve().ConfigureAwait(true);
        return share;
    }

    private async Task<ShareRecord> Create()
    {
        using var store = new ShareStore(StorePath);
        var record = ARecord();
        await store.AddAsync(
            record,
            new ShareBounds(
                ShareBounds.DefaultMaxLiveShares,
                ShareBounds.DefaultMaxLiveSharesPerItem,
                ShareBounds.DefaultMaxShareLifetimeDays,
                ShareBounds.DefaultExpiredShareRetentionDays),
            Now,
            _log).ConfigureAwait(true);

        return record;
    }

    private async Task Resolve()
    {
        using var store = new ShareStore(StorePath);
        await Open(store, TheToken, Invited).ConfigureAwait(true);
    }

    private async Task Revoke()
    {
        using var store = new ShareStore(StorePath);
        var records = await store.ReadAsync(CancellationToken.None).ConfigureAwait(true);
        await store.RevokeAsync(records[0].Id, Operator, Now, _log, "sent to the wrong person").ConfigureAwait(true);
    }

    // One store state per reason, so each refusal is reached by the thing it is
    // named after rather than by whichever check happens to come first.
    private async Task Refuse(ShareRefusal reason)
    {
        using var store = new ShareStore(StorePath);
        await store.MutateAsync(_ => Records(reason)).ConfigureAwait(true);

        var caller = reason switch
        {
            ShareRefusal.CallerNotInvited => (Guid?)Stranger,
            ShareRefusal.CallerNotSignedIn => null,
            _ => Invited,
        };

        var token = reason switch
        {
            ShareRefusal.NoSuchShare => "a-guessed-token",
            ShareRefusal.NoTokenPresented => string.Empty,
            _ => TheToken,
        };

        await Open(
            store,
            token,
            caller,
            reason == ShareRefusal.PluginNotActive ? PluginStatus.Disabled : PluginStatus.Active).ConfigureAwait(true);
    }

    private ShareRecord[] Records(ShareRefusal reason) => reason switch
    {
        ShareRefusal.Revoked => [ARecord(revokedAt: Now.AddDays(-1))],
        ShareRefusal.Expired => [ARecord(expiresAt: Now.AddDays(-1))],
        _ => [ARecord()],
    };

    private ShareRecord ARecord(DateTimeOffset? expiresAt = null, DateTimeOffset? revokedAt = null) => new ShareRecord
    {
        SchemaVersion = ShareRecord.CurrentSchemaVersion,
        Id = Guid.NewGuid(),
        ItemId = Item,
        InvitedUserIds = [Invited],
        CreatedByUserId = Operator,
        CreatedAt = Now.AddDays(-1),
        ExpiresAt = expiresAt ?? Now.AddDays(7),
        RevokedAt = revokedAt,
        TokenHash = ShareTokenHash.Compute(_key, TheToken),
    };

    private async Task<ActionResult> Open(
        IShareStore store,
        string presentedToken,
        Guid? caller,
        PluginStatus status = PluginStatus.Active)
    {
        var controller = new ShareLinksGuestController(
            store,
            _keyFile,
            ContextFor(caller),
            ManagerSaying(status),
            At(Now),
            _log.For<ShareLinksGuestController>())
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };

        return await controller.Open(presentedToken, CancellationToken.None).ConfigureAwait(true);
    }

    private static IAuthorizationContext ContextFor(Guid? caller)
    {
        var authorization = new AuthorizationInfo
        {
            IsAuthenticated = caller is not null,
            User = caller is { } identified
                ? new User("guest", "provider", "reset") { Id = identified }
                : null,
        };

        var context = new Mock<IAuthorizationContext>();
        context.Setup(c => c.GetAuthorizationInfo(It.IsAny<HttpRequest>()))
            .ReturnsAsync(authorization);

        return context.Object;
    }

    private static IPluginManager ManagerSaying(PluginStatus status)
    {
        var manifest = new PluginManifest { Id = ThePlugin().Id, Status = status };
        var installed = new Mock<LocalPlugin>(Path.GetTempPath(), true, manifest);
        var manager = new Mock<IPluginManager>();
        manager.SetupGet(m => m.Plugins).Returns([installed.Object]);

        return manager.Object;
    }

    private static Plugin ThePlugin()
    {
        var paths = new Mock<MediaBrowser.Common.Configuration.IApplicationPaths>();
        paths.SetReturnsDefault(Path.GetTempPath());

        return Plugin.Instance ?? new Plugin(paths.Object, Mock.Of<MediaBrowser.Model.Serialization.IXmlSerializer>());
    }

    private static TimeProvider At(DateTimeOffset instant) => new FixedClock(instant);

    private sealed class FixedClock : TimeProvider
    {
        private readonly DateTimeOffset _instant;

        public FixedClock(DateTimeOffset instant) => _instant = instant;

        public override DateTimeOffset GetUtcNow() => _instant;
    }

    // One captured line: the level, the text as a reader of the log sees it, and
    // the placeholders it was assembled from. The last of those is what the
    // whitelist above is compared against, because the rendered text of a field
    // nobody expected would still read as prose.
    private sealed record Line(LogLevel Level, string Text, IReadOnlyList<KeyValuePair<string, object?>> State)
    {
        public IEnumerable<string> Fields => State.Select(pair => pair.Key);

        public object? Value(string field) => State.First(pair => string.Equals(pair.Key, field, StringComparison.Ordinal)).Value;
    }

    private sealed class CapturingLogger : ILogger
    {
        public List<Line> Lines { get; } = [];

        public ILogger<T> For<T>() => new Typed<T>(this);

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
            => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            ArgumentNullException.ThrowIfNull(formatter);

            var fields = state as IReadOnlyList<KeyValuePair<string, object?>> ?? [];
            var text = formatter(state, exception) + " " + string.Join(
                " ",
                fields.Select(pair => string.Format(CultureInfo.InvariantCulture, "{0}={1}", pair.Key, pair.Value)));

            Lines.Add(new Line(logLevel, text, fields));
        }

        // The route asks for ILogger<T> because that is what a server hands a
        // controller. It is the same capture underneath, so a line written
        // through either arrives in one list and no assertion above has to know
        // which path wrote it.
        private sealed class Typed<T> : ILogger<T>
        {
            private readonly CapturingLogger _inner;

            public Typed(CapturingLogger inner) => _inner = inner;

            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull
                => _inner.BeginScope(state);

            public bool IsEnabled(LogLevel logLevel) => _inner.IsEnabled(logLevel);

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
                => _inner.Log(logLevel, eventId, state, exception, formatter);
        }
    }
}
