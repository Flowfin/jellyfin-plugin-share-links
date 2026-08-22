using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Database.Implementations.Entities;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Net;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;
using Microsoft.Extensions.Logging.Abstractions;

namespace Jellyfin.Plugin.ShareLinks.Tests;

/// <summary>
/// The same table as <see cref="DecisionTableTests"/>, carried one layer further:
/// what each of its rows puts on the wire (#77).
/// </summary>
/// <remarks>
/// <para>
/// #77 asks two things of every row, that it asserts the outcome and that the
/// refusal reason is not disclosed to the caller. The first is a property of the
/// decision and is asserted where the decision is. The second is not: the
/// decision hands its reason back to whoever called it, on purpose, and whether
/// that reason reaches a caller is a property of the route. So the second half of
/// that clause cannot be asserted against the decision at all, and asserting it
/// beside the table, over a handful of chosen situations, would leave the rows
/// nobody chose unjudged.
/// </para>
/// <para>
/// This file drives the guest route once per row of the same table, so the
/// statement is made over the product rather than over a sample. The rows come
/// from <see cref="DecisionTableTests.TheTable"/> rather than from a copy, so a
/// row added there is a row driven here from the moment it is added, and a table
/// that grew an axis cannot leave this behind.
/// </para>
/// <para>
/// Refusals are compared against a literal rather than against each other. All of
/// them being equal is satisfiable by all of them being wrong in the same way,
/// including all of them being the answer a resolved share gets.
/// </para>
/// <para>
/// One row is a situation a request cannot be in. Where the token axis is absent
/// the route is driven with no token, and a real request carrying none does not
/// match the route at all, because the token is a path segment. That is asserted
/// in <c>GuestRouteTests</c> and is not restated here; the row is driven anyway,
/// because a route that stopped refusing a missing token would be a defect
/// whether or not the routing table hides it.
/// </para>
/// <para>
/// The route is driven directly rather than through a server, which is what
/// <c>docs/testing.md</c> requires. What that leaves out is the server's own
/// refusal of a caller it has not signed in, which happens in front of the action.
/// </para>
/// </remarks>
public sealed class DecisionTableOnTheWireTests : IDisposable
{
    // Everything a refusal writes. The status, no header of this plugin's own,
    // and no body. A reason reaching the caller has to arrive in one of those
    // three, so comparing all three is what makes this a statement about
    // disclosure rather than about a status code.
    private const string TheRefusal = "404 headers:[] body:[]";

    private static readonly DateTimeOffset Expiry = new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid Invited = Guid.Parse("66666666-6666-6666-6666-666666666666");
    private static readonly Guid Stranger = Guid.Parse("77777777-7777-7777-7777-777777777777");
    private static readonly Guid Item = Guid.Parse("88888888-8888-8888-8888-888888888888");

    private readonly string _directory;
    private readonly ShareKeyFile _keyFile;
    private readonly byte[] _key;

    /// <summary>
    /// Initializes a new instance of the <see cref="DecisionTableOnTheWireTests"/> class.
    /// </summary>
    public DecisionTableOnTheWireTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "share-links-table-wire-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);

        // The key the route will read, read here through the same file, so a
        // record cannot be hashed under a key the route would not have.
        _keyFile = new ShareKeyFile(Path.Combine(_directory, PluginServiceRegistrator.KeyFileName));
        _key = _keyFile.Read();
    }

    /// <summary>
    /// Removes the directory this test's store and key were kept in.
    /// </summary>
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
    /// Every row of the table that refuses puts the same bytes on the wire, and
    /// the one row that resolves does not.
    /// </summary>
    /// <param name="situation">The five axis values, separated by spaces.</param>
    /// <param name="expected">The answer the table records for them.</param>
    /// <returns>A task that completes when the assertions have been made.</returns>
    [Theory]
    [MemberData(nameof(DecisionTableTests.TheTable), MemberType = typeof(DecisionTableTests))]
    public async Task EveryRowOfTheTableReachesTheCallerAsTheSameBytes(string situation, ShareRefusal expected)
    {
        var answer = await Drive(situation).ConfigureAwait(true);

        if (expected == ShareRefusal.None)
        {
            // The negative half of the same statement. If a resolved share also
            // answered with the refusal, every row here would be equal to every
            // other and the equality would mean nothing.
            var redirect = Assert.IsType<RedirectResult>(answer);
            Assert.Equal("/web/#/details?id=" + Item.ToString("N", CultureInfo.InvariantCulture), redirect.Url);
            Assert.DoesNotContain("a-token", redirect.Url, StringComparison.Ordinal);
            return;
        }

        Assert.Equal(TheRefusal, await Bytes(answer).ConfigureAwait(true));
    }

    /// <summary>
    /// Every reason the decision has a name for reaches the caller as the same
    /// bytes, and the set of reasons driven here is the whole set that exists.
    /// </summary>
    /// <returns>A task that completes when the assertions have been made.</returns>
    /// <remarks>
    /// <para>
    /// The table above has five axes and the decision has one input that is not
    /// among them: the install's key, which is a state of the install rather than
    /// of a share. So the table cannot reach <see cref="ShareRefusal.KeyUnavailable"/>
    /// and a statement made only over the table would leave one reason unjudged.
    /// </para>
    /// <para>
    /// Each drive is checked against the decision before its bytes are compared,
    /// so a fixture that stopped producing the reason it is filed under fails
    /// here rather than quietly testing the same reason nine times. And the set of
    /// reasons is compared against the enum rather than written out, so a reason
    /// added later arrives with a red test instead of with silence.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task EveryReasonTheDecisionCanGiveReachesTheCallerAsTheSameBytes()
    {
        using var store = new ShareStore(StorePath);
        await store.MutateAsync(_ => new[]
        {
            ARecord(revokedAt: null),
            ARecord(token: "revoked-token", revokedAt: Expiry.AddDays(-2)),
        }).ConfigureAwait(true);

        // A key file of the wrong width. Nothing replaces it, which is what makes
        // the reason a refusal rather than a fresh key, so the route reading it
        // refuses every request while it is in that state.
        var shortKeyPath = Path.Combine(_directory, "short.key");
        await File.WriteAllBytesAsync(shortKeyPath, new byte[] { 1, 2, 3 }).ConfigureAwait(true);
        var unreadableKey = new ShareKeyFile(shortKeyPath);

        var drives = new Dictionary<ShareRefusal, Situation>
        {
            [ShareRefusal.PluginNotActive] = new Situation(_keyFile, "a-token", Invited, PluginStatus.Disabled, Before),
            [ShareRefusal.NoTokenPresented] = new Situation(_keyFile, string.Empty, Invited, PluginStatus.Active, Before),
            [ShareRefusal.KeyUnavailable] = new Situation(unreadableKey, "a-token", Invited, PluginStatus.Active, Before),
            [ShareRefusal.NoSuchShare] = new Situation(_keyFile, "not-the-token", Invited, PluginStatus.Active, Before),
            [ShareRefusal.Revoked] = new Situation(_keyFile, "revoked-token", Invited, PluginStatus.Active, Before),
            [ShareRefusal.Expired] = new Situation(_keyFile, "a-token", Invited, PluginStatus.Active, After),
            [ShareRefusal.CallerNotSignedIn] = new Situation(_keyFile, "a-token", null, PluginStatus.Active, Before),
            [ShareRefusal.CallerNotInvited] = new Situation(_keyFile, "a-token", Stranger, PluginStatus.Active, Before),
            [ShareRefusal.ItemGone] = new Situation(_keyFile, "a-token", Invited, PluginStatus.Active, Before, TheServerHoldsTheItem: false),
        };

        var everyReason = Enum.GetValues<ShareRefusal>().Where(reason => reason != ShareRefusal.None);
        Assert.Equal(
            everyReason.OrderBy(reason => reason).ToList(),
            drives.Keys.OrderBy(reason => reason).ToList());

        var records = await store.ReadAsync(CancellationToken.None).ConfigureAwait(true);

        foreach (var (reason, situation) in drives)
        {
            var decided = ShareResolution.Resolve(
                records,
                situation.KeyFile,
                situation.Token,
                situation.Caller,
                situation.Status,
                At(situation.Now),
                _ => situation.TheServerHoldsTheItem);

            Assert.Equal(reason, decided.Refusal);
            Assert.Equal(TheRefusal, await Bytes(await Ask(store, situation).ConfigureAwait(true)).ConfigureAwait(true));
        }
    }

    private static DateTimeOffset Before => Expiry.AddHours(-1);

    private static DateTimeOffset After => Expiry.AddHours(1);

    private async Task<ActionResult> Drive(string situation)
    {
        var axes = situation.Split(' ');
        Assert.Equal(5, axes.Length);

        var status = axes[0] switch
        {
            "Active" => PluginStatus.Active,
            "NotActive" => PluginStatus.Disabled,
            _ => throw new ArgumentOutOfRangeException(nameof(situation), situation, "the table names a plugin state this method does not build"),
        };

        var token = axes[1] switch
        {
            "Absent" => null,
            "Empty" => string.Empty,
            "Unmatched" => "not-the-token",
            "Matched" => "a-token",
            _ => throw new ArgumentOutOfRangeException(nameof(situation), situation, "the table names a token state this method does not build"),
        };

        if (axes[2] is not ("Live" or "Revoked" or "Expired" or "RevokedAndExpired"))
        {
            throw new ArgumentOutOfRangeException(nameof(situation), situation, "the table names a share state this method does not build");
        }

        var revoked = axes[2] is "Revoked" or "RevokedAndExpired";
        var expired = axes[2] is "Expired" or "RevokedAndExpired";

        Guid? caller = axes[3] switch
        {
            "NotSignedIn" => null,
            "SignedIn" => axes[4] switch
            {
                "Named" => Invited,
                "NotNamed" => Stranger,
                _ => throw new ArgumentOutOfRangeException(nameof(situation), situation, "the table names an invitation state this method does not build"),
            },
            _ => throw new ArgumentOutOfRangeException(nameof(situation), situation, "the table names a caller state this method does not build"),
        };

        using var store = new ShareStore(StorePath);
        await store.MutateAsync(_ => new[] { ARecord(revokedAt: revoked ? Expiry.AddDays(-2) : null) }).ConfigureAwait(true);

        return await Ask(
            store,
            new Situation(_keyFile, token, caller, status, expired ? After : Before)).ConfigureAwait(true);
    }

    private ShareRecord ARecord(string token = "a-token", DateTimeOffset? revokedAt = null) => new ShareRecord
    {
        SchemaVersion = ShareRecord.CurrentSchemaVersion,
        Id = Guid.NewGuid(),
        ItemId = Item,
        InvitedUserIds = new[] { Invited },
        CreatedByUserId = Guid.NewGuid(),
        CreatedAt = Expiry.AddDays(-7),
        ExpiresAt = Expiry,
        RevokedAt = revokedAt,
        TokenHash = ShareTokenHash.Compute(_key, token),
    };

    private async Task<ActionResult> Ask(IShareStore store, Situation situation)
    {
        var controller = new ShareLinksGuestController(
            store,
            situation.KeyFile,
            ContextFor(situation.Caller),
            ManagerSaying(situation.Status),
            situation.TheServerHoldsTheItem ? ALibraryThatHoldsEveryItem() : ALibraryHoldingNothing(),
            At(situation.Now),
            NullLogger<ShareLinksGuestController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };

        return await controller.Open(situation.Token!, CancellationToken.None).ConfigureAwait(true);
    }

    // What the result would write, as the string a refusal is compared by: the
    // status, the headers, and the body.
    private static async Task<string> Bytes(ActionResult answer)
    {
        var body = new MemoryStream();
        var http = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection().AddLogging().BuildServiceProvider(),
        };
        http.Response.Body = body;

        await answer.ExecuteResultAsync(new ActionContext(http, new RouteData(), new ActionDescriptor())).ConfigureAwait(true);

        return string.Format(
            CultureInfo.InvariantCulture,
            "{0} headers:[{1}] body:[{2}]",
            http.Response.StatusCode,
            string.Join(",", http.Response.Headers.Select(header => header.Key + "=" + header.Value).OrderBy(text => text, StringComparer.Ordinal)),
            Encoding.UTF8.GetString(body.ToArray()));
    }

    private static Plugin ThePlugin()
    {
        var paths = new Mock<IApplicationPaths>();
        paths.SetReturnsDefault(Path.GetTempPath());

        return Plugin.Instance ?? new Plugin(paths.Object, Mock.Of<IXmlSerializer>());
    }

    private static IPluginManager ManagerSaying(PluginStatus status)
    {
        var manifest = new PluginManifest { Id = ThePlugin().Id, Status = status };
        var manager = new Mock<IPluginManager>();
        manager.SetupGet(m => m.Plugins).Returns(new[] { new LocalPlugin(Path.GetTempPath(), true, manifest) });

        return manager.Object;
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

    private static TimeProvider At(DateTimeOffset instant) => new FixedClock(instant);

    // One situation the route can be driven in. It is a type rather than five
    // parameters because two of the drives below differ only in the key file, and
    // a positional argument list that long is a place to transpose two of them.
    private sealed record Situation(
        ShareKeyFile KeyFile,
        string? Token,
        Guid? Caller,
        PluginStatus Status,
        DateTimeOffset Now,
        bool TheServerHoldsTheItem = true);

    private sealed class FixedClock : TimeProvider
    {
        private readonly DateTimeOffset _instant;

        public FixedClock(DateTimeOffset instant) => _instant = instant;

        public override DateTimeOffset GetUtcNow() => _instant;
    }

    // The server saying it still holds whatever a record names (#39). What an item
    // really is belongs to the server; nothing on this route reaches past whether
    // it is there, so identity is the whole fake.
    private static ILibraryManager ALibraryThatHoldsEveryItem()
    {
        var library = new Mock<ILibraryManager>(MockBehavior.Strict);
        library.Setup(m => m.GetItemById(It.IsAny<Guid>()))
            .Returns((Guid id) => new Folder { Id = id });
        return library.Object;
    }

    // The server after a scan removed what the record names (#39).
    private static ILibraryManager ALibraryHoldingNothing()
    {
        var library = new Mock<ILibraryManager>(MockBehavior.Strict);
        library.Setup(m => m.GetItemById(It.IsAny<Guid>()))
            .Returns((Guid _) => null);
        return library.Object;
    }

}
