using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Database.Implementations.Entities;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Net;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using System.Text.RegularExpressions;
using Xunit;
using Microsoft.Extensions.Logging.Abstractions;

namespace Jellyfin.Plugin.ShareLinks.Tests;

/// <summary>
/// What the guest route answers, to the three callers #68 names and to every
/// other way of getting nothing (#24, #26).
/// </summary>
/// <remarks>
/// <para>
/// The route is driven directly rather than through a server, which is what
/// <c>docs/testing.md</c> requires of everything here. What that reaches is the
/// whole of this plugin's part: the store read, the decision, and the one thing
/// said back. It starts after the authorize attribute has been honoured or
/// ignored, so where a caller the server has not signed in is refused is not a
/// question anything in this file can answer;
/// <c>RouteRefusalOrderTests</c> puts the framework's own authentication and
/// authorization middlewares in front of this action and asserts that such a
/// caller never reaches it and the store is never read. What neither file
/// reaches is the server's own registration of that refusal, which is whether
/// Jellyfin inserts those middlewares at all, what its default policy requires
/// and what its authentication schemes do.
/// </para>
/// <para>
/// Every refusal is compared as bytes rather than as a type, because the property
/// #26 asks for is about what reaches the caller. Two results can be the same
/// class and differ by a header.
/// </para>
/// </remarks>
public sealed class GuestRouteTests : IDisposable
{
    private static readonly DateTimeOffset Now = new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid Invited = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Stranger = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid Item = Guid.Parse("55555555-5555-5555-5555-555555555555");

    private readonly string _directory;
    private readonly ShareKeyFile _keyFile;
    private readonly byte[] _key;

    public GuestRouteTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "share-links-guest-route-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);

        // The key the route will read, read here through the same file, so a
        // record cannot be hashed under a key the route would not have.
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
    /// Signed in and invited. The caller is sent to the item the share names.
    /// </summary>
    /// <returns>A task that completes when the assertions have been made.</returns>
    [Fact]
    public async Task ACallerTheShareNamesIsSentToTheItem()
    {
        using var store = new ShareStore(StorePath);
        await store.MutateAsync(_ => new[] { ARecord() });

        var answer = await Ask(store, "a-token", Invited);

        var redirect = Assert.IsType<RedirectResult>(answer);
        Assert.Equal("/web/#/details?id=" + Item.ToString("N"), redirect.Url);
    }

    /// <summary>
    /// The address carries the item and never the token. A link the guest lands
    /// on after this is a link that goes into a browser history and a referrer.
    /// </summary>
    /// <returns>A task that completes when the assertions have been made.</returns>
    [Fact]
    public async Task WhereTheCallerIsSentCarriesNoToken()
    {
        using var store = new ShareStore(StorePath);
        await store.MutateAsync(_ => new[] { ARecord() });

        var redirect = Assert.IsType<RedirectResult>(await Ask(store, "a-token", Invited));

        Assert.DoesNotContain("a-token", redirect.Url, StringComparison.Ordinal);
    }

    /// <summary>
    /// A server mounted under a path keeps that path in the address, or the guest
    /// arrives at somebody else's root.
    /// </summary>
    [Fact]
    public void TheAddressKeepsThePathTheServerIsMountedUnder()
        => Assert.Equal(
            "/jellyfin/web/#/details?id=" + Item.ToString("N"),
            ShareLinksGuestController.TheItemsAddress("/jellyfin", Item));

    /// <summary>
    /// Signed in and not invited. This is the second test #24 asks for: the
    /// caller is somebody the server knows and the share does not name.
    /// </summary>
    /// <returns>A task that completes when the assertions have been made.</returns>
    [Fact]
    public async Task ACallerTheShareDoesNotNameGetsNothing()
    {
        using var store = new ShareStore(StorePath);
        await store.MutateAsync(_ => new[] { ARecord() });

        Assert.IsType<NotFoundResult>(await Ask(store, "a-token", Stranger));
    }

    /// <summary>
    /// Not signed in, with a token that is valid and unexpired. This is the first
    /// test #24 asks for, and it is the case a leaked link is.
    /// </summary>
    /// <returns>A task that completes when the assertions have been made.</returns>
    [Fact]
    public async Task AValidUnexpiredTokenFromACallerTheServerHasNotIdentifiedGetsNothing()
    {
        using var store = new ShareStore(StorePath);
        await store.MutateAsync(_ => new[] { ARecord() });

        // The same token that resolves for the invited account two tests above.
        Assert.IsType<NotFoundResult>(await Ask(store, "a-token", caller: null));
    }

    /// <summary>
    /// The same case again, with an identifier sitting beside the answer that
    /// says nobody was authenticated. The route reads two things off the
    /// authorization the server hands it, and a fixture where both are absent
    /// exercises neither of them on its own: with the two joined by "or" instead
    /// of "and", the test above still passes and this one does not.
    /// </summary>
    /// <returns>A task that completes when the assertions have been made.</returns>
    [Fact]
    public async Task AnIdentifierBesideAnUnauthenticatedAnswerIsNotACaller()
    {
        using var store = new ShareStore(StorePath);
        await store.MutateAsync(_ => new[] { ARecord() });

        // The account the share names, attached to an authorization that says the
        // server authenticated nobody. Taking the identifier anyway is the route
        // trusting something beside the server's own answer, which is the one
        // thing the design rests on, and the share would open.
        var answer = await Ask(store, "a-token", ContextSaying(authenticated: false, account: Invited));

        Assert.IsType<NotFoundResult>(answer);
    }

    /// <summary>
    /// The other half of the same pair. An authorization that says somebody was
    /// authenticated and carries no account is not an account, and the empty
    /// identifier is not a caller a record can name.
    /// </summary>
    /// <returns>A task that completes when the assertions have been made.</returns>
    [Fact]
    public async Task AnAuthenticatedAnswerWithNoAccountIsNotACaller()
    {
        using var store = new ShareStore(StorePath);

        // A record naming the empty identifier is not one this plugin writes. It
        // is what a store edited by hand holds, and it is the only arrangement in
        // which dropping the second half of the pair is visible: without it the
        // empty identifier is refused for not being invited rather than for not
        // being an account.
        await store.MutateAsync(_ => new[] { ARecord(invited: Guid.Empty) });

        var answer = await Ask(store, "a-token", ContextSaying(authenticated: true, account: null));

        Assert.IsType<NotFoundResult>(answer);
    }

    /// <summary>
    /// The answers to every way of getting nothing are the same bytes on the
    /// wire. This is #26's condition and #68's third clause, and it is asserted
    /// over what the result writes rather than over its type.
    /// </summary>
    /// <returns>A task that completes when the assertions have been made.</returns>
    [Fact]
    public async Task EveryRefusalIsTheSameBytes()
    {
        using var store = new ShareStore(StorePath);
        await store.MutateAsync(_ => new[]
        {
            ARecord(),
            ARecord(token: "revoked-token", revokedAt: Now.AddDays(-1)),
            ARecord(token: "expired-token", expiresAt: Now.AddDays(-1)),
        });

        var written = new List<string>
        {
            await Bytes(await Ask(store, "no-such-token", Invited)),
            await Bytes(await Ask(store, "a-token", Stranger)),
            await Bytes(await Ask(store, "a-token", caller: null)),
            await Bytes(await Ask(store, "revoked-token", Invited)),
            await Bytes(await Ask(store, "expired-token", Invited)),
            await Bytes(await Ask(store, "a-token", Invited, status: PluginStatus.Disabled)),
            await Bytes(await Ask(store, presentedToken: string.Empty, Invited)),
            await Bytes(await Ask(store, "a-token", Invited, library: ALibraryHolding())),
        };

        Assert.Single(written.Distinct(StringComparer.Ordinal));

        // And the shape of it, so that "all the same" cannot be satisfied by all
        // of them being the success answer.
        Assert.Equal("404 headers:[] body:[]", written[0]);
    }

    /// <summary>
    /// A share whose item the server no longer holds is refused rather than
    /// answered with an address that names nothing (#39).
    /// </summary>
    /// <remarks>
    /// The caller here is signed in and invited and the token is live, so every
    /// other condition says yes. What refuses is the library, and the guest is
    /// told the same nothing every other refusal gives.
    /// </remarks>
    /// <returns>A task that completes when the assertions have been made.</returns>
    [Fact]
    public async Task AShareWhoseItemTheServerNoLongerHoldsIsRefused()
    {
        using var store = new ShareStore(StorePath);
        await store.MutateAsync(_ => new[] { ARecord() });

        var answer = await Ask(store, "a-token", Invited, library: ALibraryHolding());

        Assert.IsType<NotFoundResult>(answer);
        Assert.Equal("404 headers:[] body:[]", await Bytes(answer));
    }

    /// <summary>
    /// The same share, with the item still there, resolves. Without this the test
    /// above is satisfied by a route that refuses everything.
    /// </summary>
    /// <returns>A task that completes when the assertions have been made.</returns>
    [Fact]
    public async Task TheSameShareResolvesWhileTheServerStillHoldsTheItem()
    {
        using var store = new ShareStore(StorePath);
        await store.MutateAsync(_ => new[] { ARecord() });

        var redirect = Assert.IsType<RedirectResult>(
            await Ask(store, "a-token", Invited, library: ALibraryHolding(Item)));

        Assert.Equal("/web/#/details?id=" + Item.ToString("N"), redirect.Url);
    }

    /// <summary>
    /// A caller who was refused before the item question was reached does not
    /// make the server look anything up.
    /// </summary>
    /// <remarks>
    /// Two things at once, and both are why the question is asked last. A token
    /// naming a live share must not cost measurably more than one naming nothing,
    /// which is #26; and an uninvited caller learning that the item behind
    /// another caller's share was removed is a fact about the library handed to
    /// somebody outside it.
    /// </remarks>
    /// <returns>A task that completes when the assertions have been made.</returns>
    [Fact]
    public async Task ACallerRefusedBeforeTheItemQuestionDoesNotMakeTheServerLookAnythingUp()
    {
        using var store = new ShareStore(StorePath);
        await store.MutateAsync(_ => new[]
        {
            ARecord(),
            ARecord(token: "revoked-token", revokedAt: Now.AddDays(-1)),
            ARecord(token: "expired-token", expiresAt: Now.AddDays(-1)),
        });

        var library = new ALibraryThatRemembersWhatItWasAsked();

        await Ask(store, "no-such-token", Invited, library: library.AsManager());
        await Ask(store, "a-token", Stranger, library: library.AsManager());
        await Ask(store, "a-token", caller: null, library: library.AsManager());
        await Ask(store, "revoked-token", Invited, library: library.AsManager());
        await Ask(store, "expired-token", Invited, library: library.AsManager());

        Assert.Empty(library.Asked);

        // And the invited caller on a live share does reach it, so the emptiness
        // above is the order and not a route that never asks.
        await Ask(store, "a-token", Invited, library: library.AsManager());

        Assert.Equal(new[] { Item }, library.Asked);
    }
    /// <summary>
    /// A store that cannot be read is a refusal like any other rather than an
    /// error page. The caller learns nothing either way, and a fault told to a
    /// guest is a fault told to whoever holds the link.
    /// </summary>
    /// <returns>A task that completes when the assertions have been made.</returns>
    [Fact]
    public async Task AStoreThatCannotBeReadRefusesLikeEverythingElse()
    {
        await File.WriteAllTextAsync(StorePath, "{ this is not a store");
        using var store = new ShareStore(StorePath);

        Assert.Equal("404 headers:[] body:[]", await Bytes(await Ask(store, "a-token", Invited)));
    }

    /// <summary>
    /// The operator guide records the refusal a guest meets in a browser
    /// (#269), naming the status the run measured.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This replaces a guard that asked for the opposite sentence and had
    /// stopped biting. Under #68 the second click was the specified behaviour,
    /// so the guard held the sentence telling a guest to open the link again
    /// after signing in, judged as a sentence carrying the link, signing in and
    /// again rather than as the wording of the day. #269 then drove a browser
    /// at a real server and measured that neither half happens, and the
    /// paragraph was rewritten into a retraction that quotes the withdrawn
    /// instruction in order to withdraw it. The retraction carries all three
    /// words, so the guard went on passing on the sentence saying its own
    /// subject does not exist.
    /// </para>
    /// <para>
    /// What is under guard now is the admission rather than an instruction,
    /// because the admission is the part of that section this tree can assert
    /// is true. It is judged as a sentence naming the link and the status the
    /// run on #269 recorded, and that is deliberately tighter than the guard it
    /// replaces: a disclosure that stops naming what was measured is a weaker
    /// disclosure, so a rewording that drops the status reds this instead of
    /// passing quietly.
    /// </para>
    /// <para>
    /// It retires with #269. When a guest opening the link in a browser reaches
    /// the item, the guide stops recording a refusal and this check goes with
    /// the sentence it holds; leaving it standing then would be the same defect
    /// one direction over. It says nothing about whether a guest reads the
    /// guide, and nothing here reaches a running server.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheGuideRecordsTheRefusalAGuestMeetsInABrowser()
    {
        var path = Path.Join(AppContext.BaseDirectory, "docs", "operator-guide.md");
        Assert.True(File.Exists(path), "docs/operator-guide.md was not copied next to the test assembly: " + path);

        var recorded = Regex.Split(File.ReadAllText(path), @"(?<=[.!?])\s+")
            .Where(sentence => sentence.Contains("link", StringComparison.OrdinalIgnoreCase)
                && sentence.Contains("401", StringComparison.Ordinal))
            .ToList();

        Assert.True(
            recorded.Count > 0,
            "the operator guide carries no sentence naming the 401 a guest meets when the link is opened in a browser, so the one thing #269 measured about what the guest does is missing from the page their operator reads.");
    }

    /// <summary>
    /// The token is a segment of the route rather than a value inside the
    /// request, which is what makes a request carrying none fail to match at all
    /// rather than reach code that has to remember to refuse it.
    /// <c>docs/leaked-link.md</c> is where that was decided.
    /// </summary>
    [Fact]
    public void TheTokenIsAPathSegmentOfTheRoute()
    {
        var open = typeof(ShareLinksGuestController).GetMethod(nameof(ShareLinksGuestController.Open))!;
        var template = open.GetCustomAttributes<HttpGetAttribute>().Single().Template;

        Assert.Equal("Guest/{token}", template);
        Assert.Equal(
            "ShareLinks",
            typeof(ShareLinksGuestController).GetCustomAttributes<RouteAttribute>().Single().Template);
    }

    /// <summary>
    /// The guard from #69 now judges something. Until this route landed it ran
    /// over an empty set, where every statement about the members is true.
    /// </summary>
    /// <remarks>
    /// This asserted that the assembly held exactly one action while it did. The
    /// administrator routes in #67 made that false without making anything
    /// wrong, so what is asserted now is the half the count was standing in for:
    /// exactly one action is reached by any caller the server has signed in, and
    /// it is this one. A second route admitting that set is what #53 is about,
    /// and a plain count would no longer notice one arriving.
    /// </remarks>
    [Fact]
    public void TheOnlyActionAnySignedInCallerReachesIsThisOne()
    {
        var judged = RoutePolicy.Judge(typeof(Plugin).Assembly);

        var action = Assert.Single(judged, entry => entry.Verdict == RouteVerdict.RequiresAuthentication);
        Assert.Equal("ShareLinksGuestController.Open", action.Controller + "." + action.Action);
        Assert.False(action.IsRefused);
    }

    /// <summary>
    /// The first of the two tests #284 names: a ceiling below the item's lowest
    /// playable bitrate. The guest holds a valid share and is refused, and what
    /// they are told names the condition.
    /// </summary>
    /// <returns>A task that completes when the assertions have been made.</returns>
    [Fact]
    public async Task ACeilingBelowEveryVersionRefusesTheGuestAndSaysWhy()
    {
        using var store = new ShareStore(StorePath);
        await store.MutateAsync(_ => new[] { ACappedRecord(200_000) });

        var answer = await Ask(
            store,
            "a-token",
            Invited,
            mediaSources: ServerPlayback.Reporting(
                ServerPlayback.AVersionAt(4_000_000, supportsTranscoding: false),
                ServerPlayback.AVersionAt(1_500_000, supportsTranscoding: false)),
            accounts: ServerPlayback.AccountsHolding(Invited));

        var refusal = Assert.IsType<ObjectResult>(answer);
        Assert.Equal(StatusCodes.Status409Conflict, refusal.StatusCode);
        Assert.Equal(ShareLinksGuestController.TheCapCannotBeMetHere, refusal.Value);
    }

    /// <summary>
    /// The second of the two: every version is above the ceiling and one of them
    /// could be transcoded down, but the account is not permitted to transcode.
    /// </summary>
    /// <remarks>
    /// This is the state <c>docs/guest-capabilities.md</c> says this plugin does
    /// not produce - it turns transcoding on for every account it makes - so the
    /// only way in is an operator narrowing the account by hand afterwards. The
    /// permission is read rather than assumed for exactly that reason.
    /// </remarks>
    /// <returns>A task that completes when the assertions have been made.</returns>
    [Fact]
    public async Task AGuestNotPermittedToTranscodeIsRefusedWhereTranscodingWasTheOnlyWayUnderTheCeiling()
    {
        using var store = new ShareStore(StorePath);
        await store.MutateAsync(_ => new[] { ACappedRecord(200_000) });

        var answer = await Ask(
            store,
            "a-token",
            Invited,
            mediaSources: ServerPlayback.Reporting(ServerPlayback.AVersionAt(4_000_000, supportsTranscoding: true)),
            accounts: ServerPlayback.AccountsHolding(Invited, mayTranscode: false));

        var refusal = Assert.IsType<ObjectResult>(answer);
        Assert.Equal(StatusCodes.Status409Conflict, refusal.StatusCode);
        Assert.Equal(ShareLinksGuestController.TheCapCannotBeMetHere, refusal.Value);
    }

    /// <summary>
    /// The clause both of those carry: neither ends with a stream above the cap.
    /// What this route can put a guest in front of a stream with is the redirect
    /// to the item, so what is asserted is that neither answer is one.
    /// </summary>
    /// <remarks>
    /// It is stated as what this route does rather than as a measurement of
    /// bytes. No test in this repository may reach a server, which is
    /// <c>docs/testing.md</c>, so what came out of a transcoder is not asserted
    /// anywhere here. What is asserted is that this plugin does not send the
    /// guest on.
    /// </remarks>
    /// <returns>A task that completes when the assertions have been made.</returns>
    [Fact]
    public async Task NeitherRefusalSendsTheGuestOnToTheItem()
    {
        using var store = new ShareStore(StorePath);
        await store.MutateAsync(_ => new[] { ACappedRecord(200_000) });

        var belowEveryVersion = await Ask(
            store,
            "a-token",
            Invited,
            mediaSources: ServerPlayback.Reporting(ServerPlayback.AVersionAt(4_000_000, supportsTranscoding: false)),
            accounts: ServerPlayback.AccountsHolding(Invited));

        var notPermittedToTranscode = await Ask(
            store,
            "a-token",
            Invited,
            mediaSources: ServerPlayback.Reporting(ServerPlayback.AVersionAt(4_000_000, supportsTranscoding: true)),
            accounts: ServerPlayback.AccountsHolding(Invited, mayTranscode: false));

        Assert.IsNotType<RedirectResult>(belowEveryVersion);
        Assert.IsNotType<RedirectResult>(notPermittedToTranscode);
    }

    /// <summary>
    /// The near miss for the transcoding arm. The same item, the same ceiling and
    /// the same version, with the account permitted to transcode: the share opens.
    /// Without this the tests above are satisfied by a route that refuses every
    /// capped share, which would cap a share at nothing and read as the guard
    /// working.
    /// </summary>
    /// <returns>A task that completes when the assertions have been made.</returns>
    [Fact]
    public async Task AnAccountPermittedToTranscodeReachesTheSameItemUnderTheSameCeiling()
    {
        using var store = new ShareStore(StorePath);
        await store.MutateAsync(_ => new[] { ACappedRecord(200_000) });

        var answer = await Ask(
            store,
            "a-token",
            Invited,
            mediaSources: ServerPlayback.Reporting(ServerPlayback.AVersionAt(4_000_000, supportsTranscoding: true)),
            accounts: ServerPlayback.AccountsHolding(Invited, mayTranscode: true));

        var redirect = Assert.IsType<RedirectResult>(answer);
        Assert.Equal("/web/#/details?id=" + Item.ToString("N"), redirect.Url);
    }

    /// <summary>
    /// The other near miss. A version at or below the ceiling answers the question
    /// before the transcode flag is ever read, so a capped share whose item fits
    /// opens like any other.
    /// </summary>
    /// <returns>A task that completes when the assertions have been made.</returns>
    [Fact]
    public async Task AVersionInsideTheCeilingOpensTheShare()
    {
        using var store = new ShareStore(StorePath);
        await store.MutateAsync(_ => new[] { ACappedRecord(2_000_000) });

        var answer = await Ask(
            store,
            "a-token",
            Invited,
            mediaSources: ServerPlayback.Reporting(
                ServerPlayback.AVersionAt(4_000_000, supportsTranscoding: false),
                ServerPlayback.AVersionAt(1_500_000, supportsTranscoding: false)),
            accounts: ServerPlayback.AccountsHolding(Invited, mayTranscode: false));

        Assert.IsType<RedirectResult>(answer);
    }

    /// <summary>
    /// A version the server reports no bitrate for is an unknown and not a version
    /// above the ceiling, so the share opens. Failing the other way would refuse a
    /// working share on the reading of a field this plugin does not own.
    /// </summary>
    /// <returns>A task that completes when the assertions have been made.</returns>
    [Fact]
    public async Task AVersionTheServerReportsNoBitrateForDoesNotRefuseTheShare()
    {
        using var store = new ShareStore(StorePath);
        await store.MutateAsync(_ => new[] { ACappedRecord(200_000) });

        var answer = await Ask(
            store,
            "a-token",
            Invited,
            mediaSources: ServerPlayback.Reporting(
                ServerPlayback.AVersionAt(4_000_000, supportsTranscoding: false),
                ServerPlayback.AVersionWithNoReportedBitrate(supportsTranscoding: false)),
            accounts: ServerPlayback.AccountsHolding(Invited, mayTranscode: false));

        Assert.IsType<RedirectResult>(answer);
    }

    /// <summary>
    /// A share with no ceiling asks the server nothing about what the item can be
    /// played at. The lookup is paid on the surface that opens a share, and a
    /// share with nothing to meet is not a reason to pay it.
    /// </summary>
    /// <remarks>
    /// The double is strict, so a lookup arriving here fails the test rather than
    /// being answered with an empty list that would pass every assertion above it.
    /// </remarks>
    /// <returns>A task that completes when the assertions have been made.</returns>
    [Fact]
    public async Task AShareWithNoCeilingAsksTheServerNothingAboutTheItemsVersions()
    {
        using var store = new ShareStore(StorePath);
        await store.MutateAsync(_ => new[] { ARecord(madeByThisPlugin: true) });

        Assert.IsType<RedirectResult>(await Ask(
            store,
            "a-token",
            Invited,
            mediaSources: ServerPlayback.AskedNothing(),
            accounts: ServerPlayback.AccountsHolding(Invited)));
    }

    /// <summary>
    /// The clause that holds the exception where it was meant to stop. Every other
    /// refusal on this route answers the same bytes it answered before #284, with
    /// the cap condition armed rather than absent.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The literal is what makes this a statement about disclosure. All of them
    /// being equal to each other is satisfiable by all of them having changed in
    /// the same way, including all of them having grown the sentence this change
    /// adds.
    /// </para>
    /// <para>
    /// The fixtures carry a ceiling and the doubles answer, so the condition is
    /// reachable on this store rather than switched off for the comparison. What
    /// each of these callers is refused for is decided before the ceiling is ever
    /// looked at, and the last assertion is what shows the arming was real.
    /// </para>
    /// </remarks>
    /// <returns>A task that completes when the assertions have been made.</returns>
    [Fact]
    public async Task EveryOtherRefusalOnThisRouteIsUnchangedByTheCapCondition()
    {
        using var store = new ShareStore(StorePath);
        await store.MutateAsync(_ => new[]
        {
            ACappedRecord(200_000),
            ARecord(token: "revoked-token", revokedAt: Now.AddDays(-1), maxBitrateBitsPerSecond: 200_000, madeByThisPlugin: true),
            ARecord(token: "expired-token", expiresAt: Now.AddDays(-1), maxBitrateBitsPerSecond: 200_000, madeByThisPlugin: true),
        });

        var sources = ServerPlayback.Reporting(ServerPlayback.AVersionAt(4_000_000, supportsTranscoding: false));
        var accounts = ServerPlayback.AccountsHolding(Invited);

        var written = new List<string>
        {
            await Bytes(await Ask(store, "no-such-token", Invited, mediaSources: sources, accounts: accounts)),
            await Bytes(await Ask(store, "a-token", Stranger, mediaSources: sources, accounts: accounts)),
            await Bytes(await Ask(store, "a-token", caller: null, mediaSources: sources, accounts: accounts)),
            await Bytes(await Ask(store, "revoked-token", Invited, mediaSources: sources, accounts: accounts)),
            await Bytes(await Ask(store, "expired-token", Invited, mediaSources: sources, accounts: accounts)),
            await Bytes(await Ask(store, "a-token", Invited, status: PluginStatus.Disabled, mediaSources: sources, accounts: accounts)),
            await Bytes(await Ask(store, presentedToken: string.Empty, Invited, mediaSources: sources, accounts: accounts)),
            await Bytes(await Ask(store, "a-token", Invited, library: ALibraryHolding(), mediaSources: sources, accounts: accounts)),
        };

        Assert.Single(written.Distinct(StringComparer.Ordinal));
        Assert.Equal("404 headers:[] body:[]", written[0]);

        // And the condition is reachable on this very store, so the eight above
        // are unchanged because they are decided first rather than because
        // nothing was armed.
        var refused = await Ask(store, "a-token", Invited, mediaSources: sources, accounts: accounts);
        Assert.Equal(StatusCodes.Status409Conflict, Assert.IsType<ObjectResult>(refused).StatusCode);
    }

    /// <summary>
    /// What the guest is told names the condition and nothing else. Not the
    /// ceiling, not what the item can be played at, not the share and not the
    /// item.
    /// </summary>
    /// <remarks>
    /// Asserted as an absence of digits rather than against the numbers of one
    /// fixture. A message that grew a number would pass a comparison against the
    /// two values this test happens to use and fail this one.
    /// </remarks>
    [Fact]
    public void WhatTheGuestIsToldCarriesNoNumberAndNoIdentifier()
    {
        var told = ShareLinksGuestController.TheCapCannotBeMetHere;

        Assert.DoesNotContain(told, character => char.IsDigit(character));
        Assert.DoesNotContain("bitrate", told, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Item.ToString("N", CultureInfo.InvariantCulture), told, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Invited.ToString("N", CultureInfo.InvariantCulture), told, StringComparison.OrdinalIgnoreCase);
    }

    private ShareRecord ARecord(
        string token = "a-token",
        DateTimeOffset? expiresAt = null,
        DateTimeOffset? revokedAt = null,
        Guid? invited = null,
        long? maxBitrateBitsPerSecond = null,
        bool madeByThisPlugin = false) => new ShareRecord
        {
            SchemaVersion = ShareRecord.CurrentSchemaVersion,
            Id = Guid.NewGuid(),
            ItemId = Item,
            InvitedUserIds = new[] { invited ?? Invited },

            // Provenance, and the cap condition needs it: this plugin applies no
            // ceiling to an invited account it did not make, so a record that did
            // not claim the account would come back from the confinement decision
            // with no ceiling and the condition would never be reached (#284).
            PluginCreatedUserIds = madeByThisPlugin
                ? new[] { invited ?? Invited }
                : Array.Empty<Guid>(),
            CreatedByUserId = Guid.NewGuid(),
            CreatedAt = Now.AddDays(-1),
            ExpiresAt = expiresAt ?? Now.AddDays(7),
            RevokedAt = revokedAt,
            MaxBitrateBitsPerSecond = maxBitrateBitsPerSecond,
            TokenHash = ShareTokenHash.Compute(_key, token),
        };

    // One capped share, made by this plugin for the invited account, which is the
    // only arrangement in which the ceiling this plugin applies is in force.
    private ShareRecord ACappedRecord(long ceiling)
        => ARecord(maxBitrateBitsPerSecond: ceiling, madeByThisPlugin: true);

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
        => ContextSaying(authenticated: caller is not null, account: caller);

    /// <summary>
    /// An authorization answer with its two halves set separately. The ordinary
    /// fixture moves them together, because that is what a server produces, and
    /// then neither half is exercised on its own.
    /// </summary>
    /// <param name="authenticated">What the answer says about whether anybody was authenticated.</param>
    /// <param name="account">The account attached to the answer, or none.</param>
    /// <returns>A context returning that answer for any request.</returns>
    private static IAuthorizationContext ContextSaying(bool authenticated, Guid? account)
    {
        // The identifier is not something a caller of this type sets. It comes
        // off the account the server attached, which is #53 seen from the other
        // side, so a fixture that wants a particular caller has to attach one.
        var authorization = new AuthorizationInfo
        {
            IsAuthenticated = authenticated,
            User = account is { } identified
                ? new User("guest", "provider", "reset") { Id = identified }
                : null,
        };

        var context = new Mock<IAuthorizationContext>();
        context.Setup(c => c.GetAuthorizationInfo(It.IsAny<HttpRequest>()))
            .ReturnsAsync(authorization);

        return context.Object;
    }

    // What the result would write, as the string two of them are compared by:
    // the status, the headers, and the body.
    private static async Task<string> Bytes(ActionResult answer)
    {
        var body = new MemoryStream();
        var http = new DefaultHttpContext
        {
            // A result writes itself through the services a request would have.
            // Only the logging one is asked for on this path, and it is what an
            // empty provider is missing when this throws instead of writing.
            RequestServices = new ServiceCollection().AddLogging().BuildServiceProvider(),
        };
        http.Response.Body = body;

        await answer.ExecuteResultAsync(new ActionContext(http, new RouteData(), new ActionDescriptor()));

        return string.Format(
            CultureInfo.InvariantCulture,
            "{0} headers:[{1}] body:[{2}]",
            http.Response.StatusCode,
            string.Join(",", http.Response.Headers.Select(header => header.Key + "=" + header.Value).OrderBy(text => text, StringComparer.Ordinal)),
            Encoding.UTF8.GetString(body.ToArray()));
    }

    private Task<ActionResult> Ask(
        IShareStore store,
        string presentedToken,
        Guid? caller,
        PluginStatus status = PluginStatus.Active,
        ILibraryManager? library = null,
        IMediaSourceManager? mediaSources = null,
        IUserManager? accounts = null,
        IServerConfigurationManager? serverConfiguration = null)
        => Ask(store, presentedToken, ContextFor(caller), status, library, mediaSources, accounts, serverConfiguration);

    private async Task<ActionResult> Ask(
        IShareStore store,
        string presentedToken,
        IAuthorizationContext authorization,
        PluginStatus status = PluginStatus.Active,
        ILibraryManager? library = null,
        IMediaSourceManager? mediaSources = null,
        IUserManager? accounts = null,
        IServerConfigurationManager? serverConfiguration = null)
    {
        var controller = new ShareLinksGuestController(
            store,
            _keyFile,
            authorization,
            ManagerSaying(status),
            library ?? ALibraryHolding(Item),
            // Strict by default, and that default is doing work rather than
            // saving a line. Every fixture in this file but the cap ones is a
            // share with no ceiling, and what those fixtures assert is partly
            // that the server is never asked what the item can be played at
            // (#284).
            mediaSources ?? ServerPlayback.AskedNothing(),
            accounts ?? ServerPlayback.NoAccounts(),
            serverConfiguration ?? ServerConfigurations.WithNoCeiling(),
            At(Now),
            NullLogger<ShareLinksGuestController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };

        return await controller.Open(presentedToken, CancellationToken.None);
    }

    // The server's answer about an item, faked by identity alone: what an item
    // really is belongs to the server, and this route reaches no further than
    // whether one is there (#39).
    private static ILibraryManager ALibraryHolding(params Guid[] items)
    {
        var library = new Mock<ILibraryManager>(MockBehavior.Strict);
        library.Setup(m => m.GetItemById(It.IsAny<Guid>()))
            .Returns((Guid id) => items.Contains(id) ? new Folder { Id = id } : null);
        return library.Object;
    }

    // A library that records every identifier it was asked about, so a test can
    // assert that the server was not asked at all rather than only that the
    // answer was ignored.
    private sealed class ALibraryThatRemembersWhatItWasAsked
    {
        public List<Guid> Asked { get; } = new List<Guid>();

        public ILibraryManager AsManager()
        {
            var library = new Mock<ILibraryManager>(MockBehavior.Strict);
            library.Setup(m => m.GetItemById(It.IsAny<Guid>()))
                .Returns((Guid id) =>
                {
                    Asked.Add(id);
                    return new Folder { Id = id };
                });
            return library.Object;
        }
    }

    private static TimeProvider At(DateTimeOffset instant) => new FixedClock(instant);

    private sealed class FixedClock : TimeProvider
    {
        private readonly DateTimeOffset _instant;

        public FixedClock(DateTimeOffset instant) => _instant = instant;

        public override DateTimeOffset GetUtcNow() => _instant;
    }
}
