using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Database.Implementations.Entities;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Net;
using MediaBrowser.Model.Configuration;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.ShareLinks.Tests;

/// <summary>
/// What the request-path filter confines a guest to, and under which ceiling
/// (#239, #44, #52, #61, #64).
/// </summary>
/// <remarks>
/// <para>
/// The filter is driven directly with a request context rather than through a
/// server, which is what <c>docs/testing.md</c> requires. What that reaches is
/// every decision this plugin makes. What it does not reach is whether the server
/// puts this filter in front of any request at all, which is a registration on
/// the server's own pipeline and is asserted nowhere in this repository.
/// </para>
/// <para>
/// The five widening attempts are #44's, named one per test in the words
/// <c>docs/guest-confinement.md</c> uses for them, so a red test says which
/// relationship was being attacked rather than which line moved.
/// </para>
/// </remarks>
public sealed class GuestConfinementFilterTests
{
    private static readonly DateTimeOffset Now = new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid Operator = new Guid("33333333-3333-3333-3333-333333333333");
    private static readonly Guid Guest = new Guid("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Stranger = new Guid("99999999-9999-9999-9999-999999999999");
    private static readonly Guid Shared = new Guid("55555555-5555-5555-5555-555555555555");
    private static readonly Guid Parent = new Guid("66666666-6666-6666-6666-666666666666");
    private static readonly Guid Sibling = new Guid("77777777-7777-7777-7777-777777777777");
    private static readonly Guid Child = new Guid("88888888-8888-8888-8888-888888888888");

    /// <summary>
    /// Gets #44's five widening attempts, each one as the request that makes it.
    /// </summary>
    /// <remarks>
    /// The names are the ones <c>docs/guest-confinement.md</c> lists under what
    /// confinement has to hold against, so a failure here names the relationship
    /// being attacked. All five are made by the account that legitimately reaches
    /// the shared item, which is the whole difficulty: an answer that refused the
    /// account outright would pass these and break the share.
    /// </remarks>
    public static TheoryData<string, string> Widenings => new()
    {
        { "the parent of the shared item, asked for by its own identifier", "/Items/66666666-6666-6666-6666-666666666666" },
        { "a sibling in the same folder", "/Items/77777777-7777-7777-7777-777777777777" },
        { "the collection or library the item sits in", "/Users/33333333-3333-3333-3333-333333333333/Items?parentId=66666666-6666-6666-6666-666666666666" },
        { "a search whose results would include the item's neighbours", "/Search/Hints?searchTerm=a" },
        { "the item's children, where the shared item is a season or a series", "/Shows/55555555-5555-5555-5555-555555555555/Episodes" },
    };

    /// <summary>
    /// Gets the three ceilings of #64 set and unset in every combination, with
    /// the effective value and the ceilings that produced it.
    /// </summary>
    /// <remarks>
    /// Eight rows, which is every combination of three ceilings being present or
    /// absent, and the values are chosen so that no two of them tie except where
    /// the row is about a tie. The last row is the tie, and it is the case #64
    /// leaves open: both ceilings sitting at the value are named, because reporting
    /// one of them means an operator who lowers the other sees no change.
    /// </remarks>
    public static TheoryData<long?, long?, long?, long?, BitrateCeiling> Ceilings => new()
    {
        { null, null, null, null, BitrateCeiling.None },
        { 3_000_000, null, null, 3_000_000, BitrateCeiling.Share },
        { null, 4_000_000, null, 4_000_000, BitrateCeiling.Account },
        { null, null, 5_000_000, 5_000_000, BitrateCeiling.ServerRemoteClientLimit },
        { 3_000_000, 4_000_000, null, 3_000_000, BitrateCeiling.Share },
        { 3_000_000, null, 2_000_000, 2_000_000, BitrateCeiling.ServerRemoteClientLimit },
        { null, 4_000_000, 5_000_000, 4_000_000, BitrateCeiling.Account },
        { 2_000_000, 2_000_000, 5_000_000, 2_000_000, BitrateCeiling.Share | BitrateCeiling.Account },
    };

    /// <summary>
    /// Each of #44's five widenings is refused to the account that legitimately
    /// reaches the shared item.
    /// </summary>
    /// <param name="relationship">The relationship being attacked, in the page's own words.</param>
    /// <param name="target">The request that attacks it.</param>
    /// <returns>A task that completes when the assertions have been made.</returns>
    [Theory]
    [MemberData(nameof(Widenings))]
    public async Task EachOfTheFiveWideningsIsRefused(string relationship, string target)
    {
        var context = ContextFor(target);

        await Filter(Store(LiveShare())).OnAuthorizationAsync(context);

        Assert.True(
            context.Result is NotFoundResult,
            relationship + " reached the account, and the request was " + target);
    }

    /// <summary>
    /// The item the share names is reached by the account it names. Without this
    /// every refusal above is satisfied by a filter that refuses everything, which
    /// would be a plugin that shares nothing.
    /// </summary>
    /// <returns>A task that completes when the assertions have been made.</returns>
    [Fact]
    public async Task TheSharedItemIsReachedByTheAccountTheShareNames()
    {
        var context = ContextFor("/Items/" + Shared);

        await Filter(Store(LiveShare())).OnAuthorizationAsync(context);

        Assert.Null(context.Result);
    }

    /// <summary>
    /// An account this plugin did not create is not confined at all. Confining it
    /// would take a person's own library away from them because an operator shared
    /// one item with them.
    /// </summary>
    /// <returns>A task that completes when the assertions have been made.</returns>
    [Fact]
    public async Task AnAccountThisPluginDidNotCreateIsNotConfined()
    {
        var context = ContextFor("/Items/" + Sibling, caller: Stranger);

        await Filter(Store(LiveShare()), Stranger).OnAuthorizationAsync(context);

        Assert.Null(context.Result);
    }

    /// <summary>
    /// An account a record invites but this plugin did not create is not confined
    /// either, and that is the case the test above cannot see: it uses an account
    /// no record names at all, so it passes whichever of the two sets membership
    /// is read from. Here the record invites the account and does not claim to
    /// have made it, which is the only shape that tells the two sets apart.
    /// </summary>
    /// <returns>A task that completes when the assertions have been made.</returns>
    [Fact]
    public async Task AnInvitedAccountThisPluginDidNotCreateIsNotConfined()
    {
        var record = InvitesWithoutCreating(Stranger);
        var context = ContextFor("/Items/" + Sibling, caller: Stranger);

        await Filter(Store(record), Stranger).OnAuthorizationAsync(context);

        Assert.Null(context.Result);
        Assert.Equal(
            GuestVerdict.NotAGuestOfThisPlugin,
            GuestConfinement.Decide(new[] { record }, Stranger, Sibling, null, null, Now).Verdict);
    }

    /// <summary>
    /// A guest whose last record has ended reaches nothing this filter judges.
    /// That is the state a guest lands in the moment the last record naming them
    /// ends, and it is the one #239 names as having to be decided while building.
    /// </summary>
    /// <returns>A task that completes when the assertions have been made.</returns>
    [Fact]
    public async Task AGuestWithNoLiveRecordLeftReachesNothingTheListJudges()
    {
        var stopped = LiveShare(revokedAt: Now.AddHours(-1));
        var context = ContextFor("/Items/" + Shared);

        await Filter(Store(stopped)).OnAuthorizationAsync(context);

        Assert.IsType<NotFoundResult>(context.Result);
    }

    /// <summary>
    /// A path the maintained list does not reach is not judged, and not-judged is
    /// not an allowance. The filter is not standing in front of it, which is the
    /// accepted cost of the mechanism, and this test exists so the cost is a
    /// measured property rather than a sentence.
    /// </summary>
    /// <returns>A task that completes when the assertions have been made.</returns>
    [Fact]
    public async Task APathTheListDoesNotReachIsNotJudged()
    {
        var context = ContextFor("/SomeRouteNobodyAddedToTheList/" + Sibling);

        await Filter(Store(LiveShare())).OnAuthorizationAsync(context);

        Assert.Null(context.Result);
        Assert.Equal(ConfinedRouteKind.NotJudged, ConfinedRoutes.Judge("/SomeRouteNobodyAddedToTheList/" + Sibling).Kind);
    }

    /// <summary>
    /// A store this plugin cannot read refuses. A filter that let a request
    /// through because it could not read the records would turn a fault into the
    /// widest permission it has.
    /// </summary>
    /// <returns>A task that completes when the assertions have been made.</returns>
    [Fact]
    public async Task AStoreThatCannotBeReadRefuses()
    {
        var context = ContextFor("/Items/" + Shared);

        await Filter(new AStoreThatCannotBeRead()).OnAuthorizationAsync(context);

        Assert.IsType<NotFoundResult>(context.Result);
    }

    /// <summary>
    /// The three ceilings of #64, set and unset in every combination, through the
    /// surface that applies them. Both the value and which ceiling produced it are
    /// asserted, because a caller that throws the second half away brings back the
    /// bug #64 is about.
    /// </summary>
    /// <param name="share">The share's own ceiling.</param>
    /// <param name="account">The account's own remote client limit.</param>
    /// <param name="server">The server configuration's remote client limit.</param>
    /// <param name="effective">The ceiling that should be in force.</param>
    /// <param name="applied">Which of the three should be named as producing it.</param>
    [Theory]
    [MemberData(nameof(Ceilings))]
    public void EveryCombinationOfTheThreeCeilingsProducesTheLowestAndNamesIt(
        long? share,
        long? account,
        long? server,
        long? effective,
        BitrateCeiling applied)
    {
        var decision = GuestConfinement.Decide(
            new[] { LiveShare(cap: share) },
            Guest,
            Shared,
            account,
            server,
            Now);

        Assert.Equal(GuestVerdict.Reaches, decision.Verdict);
        Assert.Equal(effective, decision.Cap.BitsPerSecond);
        Assert.Equal(applied, decision.Cap.Applied);
    }

    /// <summary>
    /// The route the server answers a ceiling on has the requested ceiling
    /// lowered, which is the interception leg of <c>docs/bitrate-cap.md</c>.
    /// </summary>
    /// <returns>A task that completes when the assertions have been made.</returns>
    [Fact]
    public async Task ThePlaybackInformationRequestIsLoweredToTheCeilingInForce()
    {
        var context = ContextFor("/Items/" + Shared + "/PlaybackInfo?maxStreamingBitrate=40000000");

        await Filter(Store(LiveShare(cap: 3_000_000))).OnAuthorizationAsync(context);

        Assert.Null(context.Result);
        Assert.Equal("3000000", context.HttpContext.Request.Query["maxStreamingBitrate"].ToString());
    }

    /// <summary>
    /// A playback information request that named no ceiling is given one. A
    /// request that named none would otherwise be answered with the server's own
    /// largest, which is the interception leg not happening.
    /// </summary>
    /// <returns>A task that completes when the assertions have been made.</returns>
    [Fact]
    public async Task APlaybackInformationRequestThatNamedNoCeilingIsGivenOne()
    {
        var context = ContextFor("/Items/" + Shared + "/PlaybackInfo");

        await Filter(Store(LiveShare(cap: 3_000_000))).OnAuthorizationAsync(context);

        Assert.Null(context.Result);
        Assert.Equal("3000000", context.HttpContext.Request.Query["maxStreamingBitrate"].ToString());
    }

    /// <summary>
    /// A request for bytes above the ceiling is refused rather than lowered,
    /// which is the refusal leg. This is the client that never asked politely, and
    /// it is the half of #61's third clause the interception cannot cover.
    /// </summary>
    /// <returns>A task that completes when the assertions have been made.</returns>
    [Fact]
    public async Task AStreamRequestAboveTheCeilingIsRefused()
    {
        var context = ContextFor("/Videos/" + Shared + "/stream?videoBitRate=40000000");

        await Filter(Store(LiveShare(cap: 3_000_000))).OnAuthorizationAsync(context);

        Assert.IsType<NotFoundResult>(context.Result);
    }

    /// <summary>
    /// A request for bytes inside the ceiling is served. Without this the refusal
    /// above is satisfied by refusing every stream, which caps a share at nothing.
    /// </summary>
    /// <returns>A task that completes when the assertions have been made.</returns>
    [Fact]
    public async Task AStreamRequestInsideTheCeilingIsNotRefused()
    {
        var context = ContextFor("/Videos/" + Shared + "/stream?videoBitRate=1000000");

        await Filter(Store(LiveShare(cap: 3_000_000))).OnAuthorizationAsync(context);

        Assert.Null(context.Result);
    }

    /// <summary>
    /// Gets the ceiling in force, one row per source it can come from, with the
    /// share, account and server numbers that produce it (#65).
    /// </summary>
    /// <remarks>
    /// The comparison a request is judged by does not read which of the three
    /// produced the number, and that is a claim about the operator rather than an
    /// assumption: it is handed one ceiling. Walking the boundary once per source
    /// is what says so out loud, and it is the difference between a boundary
    /// covered for the share and a boundary covered for each ceiling.
    /// </remarks>
    public static TheoryData<string, long?, int, int, long> InForce => new()
    {
        { "the share's", 3_000_000, 0, 0, 3_000_000 },
        { "the account's", null, 4_000_000, 0, 4_000_000 },
        { "the server's", null, 0, 5_000_000, 5_000_000 },
    };

    /// <summary>
    /// A request for bytes exactly at the ceiling is served, one bit below it is
    /// served, and one bit above it is refused (#65).
    /// </summary>
    /// <param name="source">Which ceiling is in force, for the failure message.</param>
    /// <param name="share">The share's cap, or <c>null</c> where it sets none.</param>
    /// <param name="account">The account's ceiling, zero for none.</param>
    /// <param name="server">The server's ceiling, zero for none.</param>
    /// <param name="ceiling">The ceiling those three produce.</param>
    /// <returns>A task that completes when the assertions have been made.</returns>
    /// <remarks>
    /// The interesting values are all within one bit of the instant a refusal
    /// starts, and the refusal is written as a strict comparison, so a boundary
    /// that moved by one would leave every other test in this file green. At the
    /// ceiling is the value a client that read what it was told asks for, which is
    /// why it is the one that must not be refused.
    /// </remarks>
    [Theory]
    [MemberData(nameof(InForce))]
    public async Task TheStreamBoundaryIsWalkedAtEachCeiling(
        string source,
        long? share,
        int account,
        int server,
        long ceiling)
    {
        var store = Store(LiveShare(cap: share));

        var below = ContextFor("/Videos/" + Shared + "/stream?videoBitRate=" + (ceiling - 1));
        var at = ContextFor("/Videos/" + Shared + "/stream?videoBitRate=" + ceiling);
        var above = ContextFor("/Videos/" + Shared + "/stream?videoBitRate=" + (ceiling + 1));

        await Filter(store, Guest, account, server).OnAuthorizationAsync(below);
        await Filter(store, Guest, account, server).OnAuthorizationAsync(at);
        await Filter(store, Guest, account, server).OnAuthorizationAsync(above);

        Assert.True(below.Result is null, "one bit below " + source + " ceiling was refused");
        Assert.True(at.Result is null, "exactly at " + source + " ceiling was refused, so a client that asked for what it was told cannot play");
        Assert.True(above.Result is NotFoundResult, "one bit above " + source + " ceiling was served");
    }

    /// <summary>
    /// A playback information request is left alone at the ceiling and below it,
    /// and lowered to it above it (#65).
    /// </summary>
    /// <param name="source">Which ceiling is in force, for the failure message.</param>
    /// <param name="share">The share's cap, or <c>null</c> where it sets none.</param>
    /// <param name="account">The account's ceiling, zero for none.</param>
    /// <param name="server">The server's ceiling, zero for none.</param>
    /// <param name="ceiling">The ceiling those three produce.</param>
    /// <returns>A task that completes when the assertions have been made.</returns>
    /// <remarks>
    /// The other leg of the same boundary. Lowering a request that was already
    /// inside the ceiling would rewrite what an honest client asked for, and a
    /// request exactly at the ceiling is the one most likely to be rewritten by a
    /// comparison that is one bit out.
    /// </remarks>
    [Theory]
    [MemberData(nameof(InForce))]
    public async Task ThePlaybackInformationBoundaryIsWalkedAtEachCeiling(
        string source,
        long? share,
        int account,
        int server,
        long ceiling)
    {
        var store = Store(LiveShare(cap: share));

        var below = ContextFor("/Items/" + Shared + "/PlaybackInfo?maxStreamingBitrate=" + (ceiling - 1));
        var at = ContextFor("/Items/" + Shared + "/PlaybackInfo?maxStreamingBitrate=" + ceiling);
        var above = ContextFor("/Items/" + Shared + "/PlaybackInfo?maxStreamingBitrate=" + (ceiling + 1));

        await Filter(store, Guest, account, server).OnAuthorizationAsync(below);
        await Filter(store, Guest, account, server).OnAuthorizationAsync(at);
        await Filter(store, Guest, account, server).OnAuthorizationAsync(above);

        Assert.Null(below.Result);
        Assert.Null(at.Result);
        Assert.Null(above.Result);

        Assert.Equal(
            (ceiling - 1).ToString(CultureInfo.InvariantCulture),
            below.HttpContext.Request.Query["maxStreamingBitrate"].ToString());
        Assert.Equal(
            ceiling.ToString(CultureInfo.InvariantCulture),
            at.HttpContext.Request.Query["maxStreamingBitrate"].ToString());
        Assert.Equal(
            ceiling.ToString(CultureInfo.InvariantCulture),
            above.HttpContext.Request.Query["maxStreamingBitrate"].ToString());
    }

    /// <summary>
    /// A guest invited to one item under two live records is held to the tighter
    /// of the two caps. The alternative is an operator lowering a cap and a second
    /// record nobody was looking at keeping the stream where it was.
    /// </summary>
    [Fact]
    public void TwoLiveRecordsForOneItemGiveTheTighterCap()
    {
        var decision = GuestConfinement.Decide(
            new[] { LiveShare(cap: 8_000_000), LiveShare(cap: 2_000_000) },
            Guest,
            Shared,
            null,
            null,
            Now);

        Assert.Equal(2_000_000, decision.Cap.BitsPerSecond);
        Assert.Equal(BitrateCeiling.Share, decision.Cap.Applied);
    }

    /// <summary>
    /// The two refusals are told apart from each other and from the account this
    /// plugin never made. They are one answer on the wire, which is #26, and three
    /// different things for an operator reading a log.
    /// </summary>
    [Fact]
    public void TheVerdictsAreToldApartEvenThoughTheAnswerOnTheWireIsOne()
    {
        var live = new[] { LiveShare() };
        var stopped = new[] { LiveShare(revokedAt: Now.AddHours(-1)) };

        Assert.Equal(GuestVerdict.NotAGuestOfThisPlugin, GuestConfinement.Decide(live, Stranger, Shared, null, null, Now).Verdict);
        Assert.Equal(GuestVerdict.RefusedNothingLive, GuestConfinement.Decide(stopped, Guest, Shared, null, null, Now).Verdict);
        Assert.Equal(GuestVerdict.RefusedItemNotShared, GuestConfinement.Decide(live, Guest, Sibling, null, null, Now).Verdict);
        Assert.Equal(GuestVerdict.RefusedRouteEnumerates, GuestConfinement.Decide(live, Guest, null, null, null, Now).Verdict);
        Assert.Equal(GuestVerdict.Reaches, GuestConfinement.Decide(live, Guest, Shared, null, null, Now).Verdict);
    }

    /// <summary>
    /// Every template in the maintained list is matched by a path built from it,
    /// and lands in the family it was written into. A template that matched
    /// nothing would be a line in a list that reads as coverage.
    /// </summary>
    [Fact]
    public void EveryTemplateInTheListMatchesAPathBuiltFromIt()
    {
        foreach (var template in ConfinedRoutes.NamingOneItem)
        {
            var judged = ConfinedRoutes.Judge(PathFrom(template));
            Assert.Equal(ConfinedRouteKind.NamesAnItem, judged.Kind);
            Assert.Equal(Shared, judged.Item);
        }

        foreach (var template in ConfinedRoutes.Enumerating)
        {
            Assert.Equal(ConfinedRouteKind.Enumerates, ConfinedRoutes.Judge(PathFrom(template)).Kind);
        }

        foreach (var template in ConfinedRoutes.ReportingACeiling)
        {
            Assert.True(ConfinedRoutes.ReportsACeiling(PathFrom(template)), template + " is in the reporting list and matches no path built from it");
        }

        foreach (var template in ConfinedRoutes.ServingAStream)
        {
            Assert.True(ConfinedRoutes.ServesAStream(PathFrom(template)), template + " is in the streaming list and matches no path built from it");
        }
    }

    /// <summary>
    /// The list is matched however the caller spelled the case, and a doubled or
    /// missing slash does not change which family a path lands in. Either would be
    /// a hole one character wide.
    /// </summary>
    [Fact]
    public void CaseAndSlashesDoNotChangeWhichFamilyAPathLandsIn()
    {
        Assert.Equal(ConfinedRouteKind.NamesAnItem, ConfinedRoutes.Judge("/items/" + Shared).Kind);
        Assert.Equal(ConfinedRouteKind.NamesAnItem, ConfinedRoutes.Judge("Items/" + Shared).Kind);
        Assert.Equal(ConfinedRouteKind.NamesAnItem, ConfinedRoutes.Judge("//Items//" + Shared).Kind);
        Assert.Equal(ConfinedRouteKind.Enumerates, ConfinedRoutes.Judge("/SEARCH/hints").Kind);
    }

    /// <summary>
    /// A segment where the list expects an item identifier and which is not one
    /// does not become an item route. <c>Items/Filters</c> is a listing and would
    /// otherwise be handed to the family that expects an identifier.
    /// </summary>
    [Fact]
    public void ASegmentThatIsNotAnIdentifierIsNotReadAsOne()
    {
        Assert.Equal(ConfinedRouteKind.Enumerates, ConfinedRoutes.Judge("/Items").Kind);
        Assert.Equal(ConfinedRouteKind.Enumerates, ConfinedRoutes.Judge("/Items/Filters").Kind);
        Assert.Null(ConfinedRoutes.Judge("/Items/Filters").Item);
    }

    /// <summary>
    /// The highest ceiling a request names is the one it is judged by. A request
    /// naming two has asked for the larger of them.
    /// </summary>
    [Fact]
    public void TheCeilingARequestNamedIsTheHighestOfTheOnesItCarries()
    {
        Assert.Equal(
            9_000_000,
            GuestConfinementFilter.CeilingAskedFor(new QueryCollection(new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>(StringComparer.Ordinal)
            {
                ["videoBitRate"] = "1000000",
                ["maxStreamingBitrate"] = "9000000",
            })));

        Assert.Null(GuestConfinementFilter.CeilingAskedFor(new QueryCollection()));
        Assert.Null(GuestConfinementFilter.CeilingAskedFor(new QueryCollection(new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>(StringComparer.Ordinal)
        {
            ["videoBitRate"] = "not a number",
        })));
    }

    // A path built from a template, with a real identifier where the template
    // wants one. The identifier is the shared item's, so a template in the item
    // family is expected to produce exactly that.
    private static string PathFrom(string template)
        => "/" + string.Join(
            "/",
            template.Split('/', StringSplitOptions.RemoveEmptyEntries)
                .Select(part => part switch
                {
                    "{itemId}" => Shared.ToString(),
                    "{userId}" => Operator.ToString(),
                    _ => part,
                }));

    // A live record that invites an account without claiming to have made it,
    // which is the only shape that separates InvitedUserIds from
    // PluginCreatedUserIds. Everything else in this file names one account in
    // both, so a membership check reading the wrong set passes every other test
    // here.
    private ShareRecord InvitesWithoutCreating(Guid invited) => new ShareRecord
    {
        SchemaVersion = ShareRecord.CurrentSchemaVersion,
        Id = Guid.NewGuid(),
        ItemId = Shared,
        InvitedUserIds = new[] { invited },
        PluginCreatedUserIds = new[] { Guest },
        CreatedByUserId = Operator,
        CreatedAt = Now.AddDays(-1),
        ExpiresAt = Now.AddDays(7),
        TokenHash = "a-hash",
    };

    private ShareRecord LiveShare(long? cap = null, DateTimeOffset? revokedAt = null) => new ShareRecord
    {
        SchemaVersion = ShareRecord.CurrentSchemaVersion,
        Id = Guid.NewGuid(),
        ItemId = Shared,
        InvitedUserIds = new[] { Guest },
        PluginCreatedUserIds = new[] { Guest },
        CreatedByUserId = Operator,
        CreatedAt = Now.AddDays(-1),
        ExpiresAt = Now.AddDays(7),
        RevokedAt = revokedAt,
        RevokedByUserId = revokedAt is null ? null : Operator,
        MaxBitrateBitsPerSecond = cap,
        TokenHash = "a-hash",
    };

    private static IShareStore Store(params ShareRecord[] records)
    {
        var store = new Mock<IShareStore>();
        store.Setup(s => s.ReadAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<ShareRecord>)records);
        return store.Object;
    }

    private static AuthorizationFilterContext ContextFor(string target, Guid? caller = null)
    {
        var http = new DefaultHttpContext();
        var split = target.Split('?', 2);
        http.Request.Path = split[0];
        http.Request.QueryString = split.Length == 2 ? new QueryString("?" + split[1]) : QueryString.Empty;

        return new AuthorizationFilterContext(
            new ActionContext(http, new RouteData(), new ActionDescriptor()),
            new List<IFilterMetadata>());
    }

    private static GuestConfinementFilter Filter(IShareStore store) => Filter(store, Guest);

    private static GuestConfinementFilter Filter(IShareStore store, Guid caller)
        => Filter(store, caller, accountCeiling: 0, serverCeiling: 0);

    // The two ceilings that are not the share's arrive as numbers rather than as
    // fixed zeroes, so a boundary can be walked around a ceiling whichever of the
    // three produced it. Zero is how the server spells no ceiling at all, which
    // is what every other test in this file passes.
    private static GuestConfinementFilter Filter(IShareStore store, Guid caller, int accountCeiling, int serverCeiling)
        => new GuestConfinementFilter(
            store,
            ContextSaying(caller),
            AccountsWhere(caller, accountCeiling),
            ServerSaying(serverCeiling),
            At(Now),
            NullLogger<GuestConfinementFilter>.Instance);

    private static TimeProvider At(DateTimeOffset instant)
    {
        var clock = new Mock<TimeProvider>();
        clock.Setup(provider => provider.GetUtcNow()).Returns(instant);
        return clock.Object;
    }

    private static IAuthorizationContext ContextSaying(Guid caller)
    {
        var context = new Mock<IAuthorizationContext>();
        context.Setup(c => c.GetAuthorizationInfo(It.IsAny<HttpRequest>()))
            .ReturnsAsync(new AuthorizationInfo
            {
                IsAuthenticated = true,
                User = new User("a caller", "provider", "reset") { Id = caller },
            });

        return context.Object;
    }

    private static IUserManager AccountsWhere(Guid caller) => AccountsWhere(caller, 0);

    private static IUserManager AccountsWhere(Guid caller, int ceiling)
    {
        var users = new Mock<IUserManager>();
        users.Setup(manager => manager.GetUserById(It.IsAny<Guid>()))
            .Returns((Guid id) => id == caller
                ? new User("a caller", "provider", "reset") { Id = id, RemoteClientBitrateLimit = ceiling }
                : null);
        return users.Object;
    }

    private static IServerConfigurationManager ServerSaying(int remoteClientBitrateLimit)
    {
        var configuration = new Mock<IServerConfigurationManager>();
        configuration.SetupGet(manager => manager.Configuration)
            .Returns(new ServerConfiguration { RemoteClientBitrateLimit = remoteClientBitrateLimit });
        return configuration.Object;
    }

    // A store whose read always fails, so the filter meets the fault it has to
    // refuse on rather than pass through.
    private sealed class AStoreThatCannotBeRead : IShareStore
    {
        public Task<IReadOnlyList<ShareRecord>> ReadAsync(CancellationToken cancellationToken = default)
            => throw new ShareStoreUnreadableException("a path", "the read failed");

        public Task<IReadOnlyList<ShareRecord>> MutateAsync(
            Func<IReadOnlyList<ShareRecord>, IReadOnlyList<ShareRecord>> change,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException("the confinement filter never writes");
    }
}
