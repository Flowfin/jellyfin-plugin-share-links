using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Database.Implementations.Entities;
using MediaBrowser.Common.Api;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Net;
using MediaBrowser.Controller.Session;
using MediaBrowser.Model.Users;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.ShareLinks.Tests;

/// <summary>
/// What the administrator routes admit, what they answer, and what they refuse
/// to hand out (#67).
/// </summary>
/// <remarks>
/// <para>
/// The actions are driven directly rather than through a server, which is what
/// <c>docs/testing.md</c> requires of everything here. What that reaches is this
/// plugin's part: the store read, the summary, and the revocation. What it does
/// not reach is the server's own refusal of a caller who is not an administrator,
/// which happens in the filter pipeline in front of the action. That half is
/// asserted over the compiled attributes instead, which is the metadata the
/// server itself reads.
/// </para>
/// <para>
/// The revocation reaches two things outside the store, and both are judged from
/// here because both are properties of the route rather than of the routine
/// behind them: which sessions it asks the server to end, which is #55, and which
/// accounts it disables, which is #58. The arithmetic each of those rests on is
/// judged on its own in <c>GuestSessionsTests</c> and <c>GuestAccountsTests</c>.
/// </para>
/// <para>
/// The create route is judged in <c>ShareCreationTests</c> rather than here. It
/// is the one action on this controller that changes something outside this
/// plugin's own store, so its fixture carries a server to change, and keeping
/// that fixture out of these tests is what keeps the listing and the revocation
/// judged against nothing but a store.
/// </para>
/// </remarks>
public sealed class AdministratorRouteTests : IDisposable
{
    private static readonly DateTimeOffset Now = new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid Operator = new Guid("33333333-3333-3333-3333-333333333333");
    private static readonly Guid AnotherOperator = new Guid("44444444-4444-4444-4444-444444444444");
    private static readonly Guid Invited = new Guid("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Item = new Guid("55555555-5555-5555-5555-555555555555");

    private readonly string _directory;
    private readonly ShareKeyFile _keyFile;
    private readonly byte[] _key;

    public AdministratorRouteTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "share-links-admin-route-" + Guid.NewGuid().ToString("N"));
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
    /// Every administrator action is reached only under the server's own
    /// elevation policy. This is #67's second clause, asserted over the compiled
    /// attribute rather than over the source, because an attribute that is
    /// missing looks exactly like one that is present in a diff. The set is
    /// written out rather than counted, so an action added here without an
    /// attribute of its own reds this rather than being judged by nothing (#243).
    /// </summary>
    [Fact]
    public void EveryAdministratorActionIsReachedOnlyUnderTheServersOwnElevationPolicy()
    {
        var judged = RoutePolicy.Judge(typeof(ShareLinksAdminController));

        Assert.Equal(
            new[] { "Create", "List", "Revoke", "RotateKey" },
            judged.Select(action => action.Action).OrderBy(name => name, StringComparer.Ordinal).ToArray());

        foreach (var action in judged)
        {
            Assert.Equal(RouteVerdict.RequiresElevation, action.Verdict);
            Assert.Equal(Policies.RequiresElevation, action.Detail);
            Assert.False(action.IsRefused);
        }
    }

    /// <summary>
    /// The policy name is the server's own constant and not a copy of the text
    /// it holds. A copy is a name that goes on compiling after the server renames
    /// the policy, and what it produces is a route the server refuses everybody
    /// on.
    /// </summary>
    [Fact]
    public void ThePolicyIsSpelledWithTheServersConstantRatherThanACopyOfIt()
    {
        var declared = typeof(ShareLinksAdminController)
            .GetCustomAttributes<AuthorizeAttribute>(inherit: true)
            .Select(attribute => attribute.Policy)
            .ToList();

        Assert.Equal(new[] { Policies.RequiresElevation }, declared);
    }

    /// <summary>
    /// Every record is listed, and the state says which of them still resolve.
    /// This is #67's "list shares with their state" and #39's third clause: a
    /// share that can no longer resolve must not look live.
    /// </summary>
    /// <returns>A task that completes when the assertions have been made.</returns>
    [Fact]
    public async Task TheListingCarriesEveryRecordAndSaysWhichOfThemStillResolve()
    {
        using var store = new ShareStore(StorePath);
        await store.MutateAsync(_ => new[]
        {
            ARecord(token: "live-token"),
            ARecord(token: "expired-token", expiresAt: Now.AddDays(-1)),
            ARecord(token: "revoked-token", revokedAt: Now.AddHours(-1)),
        });

        var listing = await Listing(store);

        Assert.Equal(
            new[] { ShareState.Live, ShareState.Expired, ShareState.Revoked },
            listing.Select(row => row.State).ToArray());
    }

    /// <summary>
    /// A share revoked after it had already expired reads as expired, because
    /// expiry is what stopped it. The fields that record the revocation are still
    /// on the row, so nothing is hidden by the state being the earlier of the two.
    /// </summary>
    /// <returns>A task that completes when the assertions have been made.</returns>
    [Fact]
    public async Task AShareRevokedAfterItHadAlreadyExpiredReadsAsExpiredAndKeepsItsRevocation()
    {
        using var store = new ShareStore(StorePath);
        await store.MutateAsync(_ => new[]
        {
            ARecord(expiresAt: Now.AddDays(-2), revokedAt: Now.AddDays(-1)),
        });

        var row = Assert.Single(await Listing(store));

        Assert.Equal(ShareState.Expired, row.State);
        Assert.Equal(Now.AddDays(-1), row.RevokedAt);
    }

    /// <summary>
    /// The listing carries neither the token nor the keyed hash of it, in any
    /// field and anywhere in the bytes the caller receives. This is the half of
    /// #67's third clause this change carries.
    /// </summary>
    /// <returns>A task that completes when the assertions have been made.</returns>
    [Fact]
    public async Task TheListingCarriesNeitherTheTokenNorTheHashOfIt()
    {
        using var store = new ShareStore(StorePath);
        await store.MutateAsync(_ => new[] { ARecord(token: "a-token") });

        var stored = Assert.Single(await store.ReadAsync());
        var written = JsonSerializer.Serialize(await Listing(store));

        // Both directions, because a field named something else still carries the
        // value, and a field carrying nothing still carries the name a script
        // would read.
        Assert.DoesNotContain("a-token", written, StringComparison.Ordinal);
        Assert.DoesNotContain(stored.TokenHash, written, StringComparison.Ordinal);
        Assert.DoesNotContain("Hash", written, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The members of the summary are the ones argued for, rather than whatever
    /// the record happens to hold. A field added to the record and copied here
    /// without being argued for reds this rather than shipping.
    /// </summary>
    [Fact]
    public void TheSummaryCarriesExactlyTheMembersThatWereArguedFor()
        => Assert.Equal(
            new[]
            {
                "CreatedAt",
                "CreatedByUserId",
                "ExpiresAt",
                "Id",
                "InvitedUserIds",
                "ItemId",
                "MaxBitrateBitsPerSecond",
                "RevocationReason",
                "RevokedAt",
                "RevokedByUserId",
                "State",
            },
            typeof(ShareSummary).GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Select(property => property.Name)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray());

    /// <summary>
    /// A store that cannot be read is an error to an operator rather than an
    /// empty listing. An empty listing is the answer a server with no shares on
    /// it gives, and the two must not look the same to the person who has to act.
    /// </summary>
    /// <returns>A task that completes when the assertions have been made.</returns>
    [Fact]
    public async Task AStoreThatCannotBeReadIsAnErrorRatherThanAnEmptyListing()
    {
        await File.WriteAllTextAsync(StorePath, "{ this is not a store");
        using var store = new ShareStore(StorePath);

        Assert.Equal("500 headers:[] body:[]", await Bytes((await Controller(store).List(CancellationToken.None)).Result!));
    }

    /// <summary>
    /// Revoking stops the share, records who pressed it and what they wrote, and
    /// the answer is the share as it stands afterwards.
    /// </summary>
    /// <returns>A task that completes when the assertions have been made.</returns>
    [Fact]
    public async Task RevokingStopsTheShareAndRecordsWhoPressedItAndWhy()
    {
        using var store = new ShareStore(StorePath);
        var share = ARecord();
        await store.MutateAsync(_ => new[] { share });

        var answer = await Controller(store).Revoke(share.Id, new ShareRevocationRequest { Reason = "sent to the wrong person" }, CancellationToken.None);

        var row = Assert.IsType<ShareSummary>(Assert.IsType<OkObjectResult>(answer.Result).Value);
        Assert.Equal(ShareState.Revoked, row.State);
        Assert.Equal(Now, row.RevokedAt);
        Assert.Equal(Operator, row.RevokedByUserId);
        Assert.Equal("sent to the wrong person", row.RevocationReason);

        // And in the store, because a route that answered without writing would
        // pass every assertion above.
        Assert.Equal(Now, Assert.Single(await store.ReadAsync()).RevokedAt);
    }

    /// <summary>
    /// A revocation with no body succeeds. An operator who has nothing to write
    /// still has a share to stop, and a surface that demanded a reason would
    /// collect a full stop.
    /// </summary>
    /// <returns>A task that completes when the assertions have been made.</returns>
    [Fact]
    public async Task RevokingWithNothingWrittenAgainstItStillStopsTheShare()
    {
        using var store = new ShareStore(StorePath);
        var share = ARecord();
        await store.MutateAsync(_ => new[] { share });

        var answer = await Controller(store).Revoke(share.Id, request: null, CancellationToken.None);

        var row = Assert.IsType<ShareSummary>(Assert.IsType<OkObjectResult>(answer.Result).Value);
        Assert.Equal(ShareState.Revoked, row.State);
        Assert.Null(row.RevocationReason);
    }

    /// <summary>
    /// Pressing it twice succeeds and changes nothing, and the second answer is
    /// the first press rather than the caller's own. An operator who pressed
    /// twice and saw their own name would believe they had stopped it, when what
    /// stopped it was somebody else an hour earlier.
    /// </summary>
    /// <returns>A task that completes when the assertions have been made.</returns>
    [Fact]
    public async Task PressingItTwiceSucceedsAndTheAnswerIsStillTheFirstPress()
    {
        using var store = new ShareStore(StorePath);
        var share = ARecord();
        await store.MutateAsync(_ => new[] { share });

        await Controller(store, Operator, At(Now.AddHours(-1))).Revoke(share.Id, new ShareRevocationRequest { Reason = "the first press" }, CancellationToken.None);
        var second = await Controller(store, AnotherOperator).Revoke(share.Id, new ShareRevocationRequest { Reason = "the second press" }, CancellationToken.None);

        var row = Assert.IsType<ShareSummary>(Assert.IsType<OkObjectResult>(second.Result).Value);
        Assert.Equal(Now.AddHours(-1), row.RevokedAt);
        Assert.Equal(Operator, row.RevokedByUserId);
        Assert.Equal("the first press", row.RevocationReason);
    }

    /// <summary>
    /// Revoking signs the share's own guests out of the server and asks nothing
    /// about anybody else. This is #55's second clause in the form that is
    /// reachable without a server: what a test here can see is which accounts this
    /// plugin asked the session manager about, and the session list itself is the
    /// server's.
    /// </summary>
    /// <returns>A task that completes when the assertions have been made.</returns>
    [Fact]
    public async Task RevokingSignsOutTheGuestsThisPluginMadeForTheShareAndNobodyElse()
    {
        var guest = Guid.NewGuid();
        var somebodyElsesAccount = Guid.NewGuid();
        var otherShare = ARecord(token: "another-token", invited: new[] { Guid.NewGuid() }, pluginCreated: new[] { Guid.NewGuid() });

        using var store = new ShareStore(StorePath);
        var share = ARecord(
            invited: new[] { guest, somebodyElsesAccount },
            pluginCreated: new[] { guest });
        await store.MutateAsync(_ => new[] { share, otherShare });

        var sessions = new RecordingSessions();
        await Controller(store, Operator, At(Now), sessions).Revoke(share.Id, request: null, CancellationToken.None);

        // The guest this plugin made, and only that one. The invited account it
        // did not make belongs to a person who uses this server, and signing them
        // out is a change to a person rather than to a share. The other share's
        // guest is untouched because nothing about it stopped.
        Assert.Equal(new[] { guest }, sessions.Revoked);
    }

    /// <summary>
    /// A guest who still holds another live share keeps watching it. Revoking one
    /// share is not a reason to stop somebody's other stream, and the account is
    /// the only handle this plugin has, so the check has to be made before the ask
    /// rather than inside it.
    /// </summary>
    /// <returns>A task that completes when the assertions have been made.</returns>
    [Fact]
    public async Task AGuestWhoStillHoldsAnotherLiveShareIsNotSignedOut()
    {
        var guest = Guid.NewGuid();

        using var store = new ShareStore(StorePath);
        var revoked = ARecord(invited: new[] { guest }, pluginCreated: new[] { guest });
        var stillLive = ARecord(token: "another-token", invited: new[] { guest }, pluginCreated: new[] { guest });
        await store.MutateAsync(_ => new[] { revoked, stillLive });

        var sessions = new RecordingSessions();
        await Controller(store, Operator, At(Now), sessions).Revoke(revoked.Id, request: null, CancellationToken.None);

        Assert.Empty(sessions.Revoked);
    }

    /// <summary>
    /// The other share having expired is not the other share still being live. A
    /// reading that counted records naming the account rather than live ones would
    /// leave a guest signed in with nothing left to watch, which is the state this
    /// issue is about.
    /// </summary>
    /// <returns>A task that completes when the assertions have been made.</returns>
    [Fact]
    public async Task AGuestWhoseOtherShareHasAlreadyExpiredIsSignedOut()
    {
        var guest = Guid.NewGuid();

        using var store = new ShareStore(StorePath);
        var revoked = ARecord(invited: new[] { guest }, pluginCreated: new[] { guest });
        var expired = ARecord(token: "another-token", expiresAt: Now.AddDays(-1), invited: new[] { guest }, pluginCreated: new[] { guest });
        await store.MutateAsync(_ => new[] { revoked, expired });

        var sessions = new RecordingSessions();
        await Controller(store, Operator, At(Now), sessions).Revoke(revoked.Id, request: null, CancellationToken.None);

        Assert.Equal(new[] { guest }, sessions.Revoked);
    }

    /// <summary>
    /// Nothing is spared. The second argument of the server's member is the token
    /// to keep, and this plugin holds none: the caller is an administrator on a
    /// session of their own and no session of theirs is under a guest account. A
    /// token accidentally passed there is a guest left signed in by the call that
    /// exists to sign them out.
    /// </summary>
    /// <returns>A task that completes when the assertions have been made.</returns>
    [Fact]
    public async Task TheSignOutSparesNoToken()
    {
        var guest = Guid.NewGuid();

        using var store = new ShareStore(StorePath);
        var share = ARecord(invited: new[] { guest }, pluginCreated: new[] { guest });
        await store.MutateAsync(_ => new[] { share });

        var sessions = new RecordingSessions();
        await Controller(store, Operator, At(Now), sessions).Revoke(share.Id, request: null, CancellationToken.None);

        Assert.Equal(new[] { string.Empty }, sessions.Spared);
    }

    /// <summary>
    /// A revocation that missed signs nobody out. Pressing revoke on an identifier
    /// the store does not hold is an operator's typing mistake, and a call that
    /// signed somebody out on the way to answering not found would be stopping a
    /// stream nobody asked to stop.
    /// </summary>
    /// <returns>A task that completes when the assertions have been made.</returns>
    [Fact]
    public async Task ARevocationThatFindsNoShareSignsNobodyOut()
    {
        var guest = Guid.NewGuid();

        using var store = new ShareStore(StorePath);
        await store.MutateAsync(_ => new[] { ARecord(invited: new[] { guest }, pluginCreated: new[] { guest }) });

        var sessions = new RecordingSessions();
        var answer = await Controller(store, Operator, At(Now), sessions).Revoke(Guid.NewGuid(), request: null, CancellationToken.None);

        Assert.IsType<NotFoundResult>(answer.Result);
        Assert.Empty(sessions.Revoked);
    }

    /// <summary>
    /// A caller the server has not identified signs nobody out either, for the
    /// same reason nothing is written for them.
    /// </summary>
    /// <returns>A task that completes when the assertions have been made.</returns>
    [Fact]
    public async Task ACallerTheServerHasNotIdentifiedSignsNobodyOut()
    {
        var guest = Guid.NewGuid();

        using var store = new ShareStore(StorePath);
        var share = ARecord(invited: new[] { guest }, pluginCreated: new[] { guest });
        await store.MutateAsync(_ => new[] { share });

        var sessions = new RecordingSessions();
        await Controller(store, caller: null, At(Now), sessions).Revoke(share.Id, request: null, CancellationToken.None);

        Assert.Empty(sessions.Revoked);
    }

    /// <summary>
    /// Pressing revoke on a share that had already stopped signs its guests out
    /// again. It is the same idempotence the store already has, carried one step
    /// further: a first press that wrote the record and then failed to reach the
    /// session manager leaves a guest watching, and the only way an operator has
    /// to try again is the button they already pressed.
    /// </summary>
    /// <returns>A task that completes when the assertions have been made.</returns>
    [Fact]
    public async Task PressingRevokeOnAShareThatHadAlreadyStoppedSignsItsGuestsOutAgain()
    {
        var guest = Guid.NewGuid();

        using var store = new ShareStore(StorePath);
        var share = ARecord(
            revokedAt: Now.AddHours(-1),
            invited: new[] { guest },
            pluginCreated: new[] { guest });
        await store.MutateAsync(_ => new[] { share });

        var sessions = new RecordingSessions();
        await Controller(store, Operator, At(Now), sessions).Revoke(share.Id, request: null, CancellationToken.None);

        Assert.Equal(new[] { guest }, sessions.Revoked);
    }

    /// <summary>
    /// Revoking a share the store does not hold is not found, which the guest
    /// route never says about anything. Here it is right: an operator who cannot
    /// tell a revocation that missed from one that worked will press it again and
    /// believe the second press.
    /// </summary>
    /// <returns>A task that completes when the assertions have been made.</returns>
    [Fact]
    public async Task RevokingAShareTheStoreDoesNotHoldIsNotFound()
    {
        using var store = new ShareStore(StorePath);
        await store.MutateAsync(_ => new[] { ARecord() });

        var answer = await Controller(store).Revoke(Guid.NewGuid(), request: null, CancellationToken.None);

        Assert.IsType<NotFoundResult>(answer.Result);
    }

    /// <summary>
    /// A caller the server has not identified is refused before anything is
    /// written. The elevation policy has already refused one in front of the
    /// action, so this cannot happen on a server; what it stops is the empty
    /// identifier being written into the field that says who revoked the share.
    /// </summary>
    /// <returns>A task that completes when the assertions have been made.</returns>
    [Fact]
    public async Task ACallerTheServerHasNotIdentifiedRevokesNothing()
    {
        using var store = new ShareStore(StorePath);
        var share = ARecord();
        await store.MutateAsync(_ => new[] { share });

        var answer = await Controller(store, caller: null).Revoke(share.Id, request: null, CancellationToken.None);

        Assert.Equal(StatusCodes.Status403Forbidden, Assert.IsType<StatusCodeResult>(answer.Result).StatusCode);
        Assert.Null(Assert.Single(await store.ReadAsync()).RevokedAt);
    }

    /// <summary>
    /// A store that cannot be read is an error on the revocation path too, and it
    /// is the same answer as on the listing path. A revocation that failed and
    /// answered as though it had worked is an operator who stops looking at a
    /// share that is still live.
    /// </summary>
    /// <returns>A task that completes when the assertions have been made.</returns>
    [Fact]
    public async Task ARevocationAgainstAStoreThatCannotBeReadIsAnError()
    {
        await File.WriteAllTextAsync(StorePath, "{ this is not a store");
        using var store = new ShareStore(StorePath);

        var answer = await Controller(store).Revoke(Guid.NewGuid(), request: null, CancellationToken.None);

        Assert.Equal("500 headers:[] body:[]", await Bytes(answer.Result!));
    }

    /// <summary>
    /// The three routes are where <c>docs/api.md</c> says they are. The page and
    /// the assembly are compared as a set by <c>ApiSurfaceTests</c>; this names the
    /// templates, so a route moved by one segment is a failure that says which
    /// one moved.
    /// </summary>
    [Fact]
    public void TheRouteTemplatesAreTheOnesThePageDescribes()
    {
        Assert.Equal(
            "ShareLinks",
            typeof(ShareLinksAdminController).GetCustomAttributes<RouteAttribute>().Single().Template);

        Assert.Equal(
            "Shares",
            typeof(ShareLinksAdminController).GetMethod(nameof(ShareLinksAdminController.Create))!
                .GetCustomAttributes<HttpPostAttribute>().Single().Template);

        Assert.Equal(
            "Shares",
            typeof(ShareLinksAdminController).GetMethod(nameof(ShareLinksAdminController.List))!
                .GetCustomAttributes<HttpGetAttribute>().Single().Template);

        Assert.Equal(
            "Shares/{shareId}/Revoke",
            typeof(ShareLinksAdminController).GetMethod(nameof(ShareLinksAdminController.Revoke))!
                .GetCustomAttributes<HttpPostAttribute>().Single().Template);
    }

    /// <summary>
    /// One account, two live shares, one of them revoked. The account is left
    /// enabled and nothing is written onto it at all, because an account named by
    /// two shares stays live while either does, or revoking one share would
    /// quietly break the other (#58).
    /// </summary>
    /// <returns>A task that completes when the assertions have been made.</returns>
    [Fact]
    public async Task EndingOneOfTwoLiveSharesLeavesTheAccountEnabled()
    {
        var guest = Guid.NewGuid();

        using var store = new ShareStore(StorePath);
        var revoked = ARecord(invited: new[] { guest }, pluginCreated: new[] { guest });
        var stillLive = ARecord(token: "another-token", invited: new[] { guest }, pluginCreated: new[] { guest });
        await store.MutateAsync(_ => new[] { revoked, stillLive });

        var accounts = new RecordingAccounts { Carries = { [guest] = GuestPolicy.DefaultMaxActiveSessions } };
        await Controller(store, Operator, At(Now), new RecordingSessions(), accounts)
            .Revoke(revoked.Id, request: null, CancellationToken.None);

        // No policy at all rather than one carrying IsDisabled false. A write that
        // said "still enabled" would be this plugin rewriting the policy of an
        // account whose share has not ended, which is what the rule forbids.
        Assert.Empty(accounts.Written);
    }

    /// <summary>
    /// Revoking the last live share naming an account sets <c>IsDisabled</c> on it
    /// and changes nothing else on the policy (#58).
    /// </summary>
    /// <returns>A task that completes when the assertions have been made.</returns>
    [Fact]
    public async Task RevokingTheLastLiveShareDisablesTheAccountAndChangesNothingElse()
    {
        var guest = Guid.NewGuid();

        using var store = new ShareStore(StorePath);
        var share = ARecord(invited: new[] { guest }, pluginCreated: new[] { guest });
        await store.MutateAsync(_ => new[] { share });

        var carried = GuestPolicy.DefaultMaxActiveSessions;
        var accounts = new RecordingAccounts { Carries = { [guest] = carried } };
        await Controller(store, Operator, At(Now), new RecordingSessions(), accounts)
            .Revoke(share.Id, request: null, CancellationToken.None);

        Assert.Equal(new[] { guest }, accounts.Written);

        // Against the policy the create writes rather than against a list typed
        // out here, so a switch that moves in GuestPolicy moves in both and this
        // goes on judging the difference rather than the contents.
        AssertIsTheGuestPolicyWithNothingButTheSwitchMoved(accounts.Policies[guest], carried);
    }

    /// <summary>
    /// The same, where the last live share ended by reaching its expiry instant
    /// rather than by being revoked. Nothing in this plugin runs at that instant,
    /// so the account is caught up with the next time the routine runs, which is
    /// the revocation of some other share (#58).
    /// </summary>
    /// <returns>A task that completes when the assertions have been made.</returns>
    [Fact]
    public async Task AnAccountWhoseLastShareExpiredIsDisabledToo()
    {
        var expiredGuest = Guid.NewGuid();
        var revokedGuest = Guid.NewGuid();

        using var store = new ShareStore(StorePath);
        var expired = ARecord(
            expiresAt: Now.AddDays(-1),
            invited: new[] { expiredGuest },
            pluginCreated: new[] { expiredGuest });
        var toRevoke = ARecord(
            token: "another-token",
            invited: new[] { revokedGuest },
            pluginCreated: new[] { revokedGuest });
        await store.MutateAsync(_ => new[] { expired, toRevoke });

        var carried = GuestPolicy.DefaultMaxActiveSessions;
        var accounts = new RecordingAccounts
        {
            Carries = { [expiredGuest] = carried, [revokedGuest] = carried },
        };
        await Controller(store, Operator, At(Now), new RecordingSessions(), accounts)
            .Revoke(toRevoke.Id, request: null, CancellationToken.None);

        // The expired share's account is the point. It belongs to no record this
        // call touched, and a routine that only looked at the record it had just
        // revoked would leave it enabled for as long as the server stands.
        Assert.Equal(new[] { expiredGuest, revokedGuest }, accounts.Written);
        AssertIsTheGuestPolicyWithNothingButTheSwitchMoved(accounts.Policies[expiredGuest], carried);
    }

    /// <summary>
    /// An invited account the record does not claim under
    /// <c>WasCreatedByThisPlugin</c> is left untouched, on every one of these. It
    /// belongs to somebody who made it, and switching it off is done to that
    /// person rather than to a share (#58).
    /// </summary>
    /// <returns>A task that completes when the assertions have been made.</returns>
    [Fact]
    public async Task AnInvitedAccountThisPluginDidNotMakeIsNotDisabled()
    {
        var guest = Guid.NewGuid();
        var somebodyElse = Guid.NewGuid();

        using var store = new ShareStore(StorePath);
        var share = ARecord(
            invited: new[] { guest, somebodyElse },
            pluginCreated: new[] { guest });
        await store.MutateAsync(_ => new[] { share });

        var accounts = new RecordingAccounts
        {
            Carries = { [guest] = GuestPolicy.DefaultMaxActiveSessions, [somebodyElse] = 3 },
        };
        await Controller(store, Operator, At(Now), new RecordingSessions(), accounts)
            .Revoke(share.Id, request: null, CancellationToken.None);

        Assert.Equal(new[] { guest }, accounts.Written);
        Assert.DoesNotContain(somebodyElse, accounts.Policies.Keys);
    }

    // The policy a disable writes, compared against the policy the create writes
    // for the same ceiling, member by member. Exactly IsDisabled may differ.
    //
    // Reflection rather than a list of properties, because a switch added to
    // GuestPolicy and forgotten here is the way this assertion stops covering what
    // it says it covers.
    private static void AssertIsTheGuestPolicyWithNothingButTheSwitchMoved(UserPolicy written, int ceiling)
    {
        var asCreated = GuestPolicy.Create(ceiling);

        Assert.True(written.IsDisabled, "the account was not disabled, so the end of its last live share did nothing to it.");
        Assert.False(asCreated.IsDisabled, "the guest policy now disables the accounts the create makes, and this comparison would then report a difference of nothing.");

        var moved = typeof(UserPolicy).GetProperties()
            .Where(property => property.CanRead && property.GetIndexParameters().Length == 0)
            .Where(property => !string.Equals(property.Name, nameof(UserPolicy.IsDisabled), StringComparison.Ordinal))
            .Where(property => !AreTheSame(property.GetValue(written), property.GetValue(asCreated)))
            .Select(property => property.Name)
            .ToArray();

        Assert.Empty(moved);
    }

    private static bool AreTheSame(object? written, object? asCreated)
    {
        if (written is System.Collections.IEnumerable left
            && asCreated is System.Collections.IEnumerable right
            && written is not string)
        {
            return left.Cast<object>().SequenceEqual(right.Cast<object>());
        }

        return Equals(written, asCreated);
    }

    private async Task<IReadOnlyList<ShareSummary>> Listing(IShareStore store)
    {
        var answer = await Controller(store).List(CancellationToken.None);

        return Assert.IsAssignableFrom<IReadOnlyList<ShareSummary>>(Assert.IsType<OkObjectResult>(answer.Result).Value);
    }

    private ShareRecord ARecord(
        string token = "a-token",
        DateTimeOffset? expiresAt = null,
        DateTimeOffset? revokedAt = null,
        IReadOnlyList<Guid>? invited = null,
        IReadOnlyList<Guid>? pluginCreated = null) => new ShareRecord
        {
            SchemaVersion = ShareRecord.CurrentSchemaVersion,
            Id = Guid.NewGuid(),
            ItemId = Item,
            InvitedUserIds = invited ?? new[] { Invited },
            PluginCreatedUserIds = pluginCreated ?? Array.Empty<Guid>(),
            CreatedByUserId = Operator,
            CreatedAt = Now.AddDays(-1),
            ExpiresAt = expiresAt ?? Now.AddDays(7),
            RevokedAt = revokedAt,
            RevokedByUserId = revokedAt is null ? null : AnotherOperator,
            TokenHash = ShareTokenHash.Compute(_key, token),
        };

    private ShareLinksAdminController Controller(IShareStore store)
        => Controller(store, Operator, At(Now));

    private ShareLinksAdminController Controller(IShareStore store, Guid? caller)
        => Controller(store, caller, At(Now));

        // The two actions these tests drive read a store, a clock and the accounts
        // a revocation switches off, and nothing else. The library and the
        // configuration are handed over as fakes that answer nothing, which is what
        // makes a listing that reached for one fail here rather than pass quietly;
        // the create route, which does reach for them, is in ShareCreationTests
        // with a fixture that can answer.
        private ShareLinksAdminController Controller(IShareStore store, Guid? caller, TimeProvider clock)
            => Controller(store, caller, clock, new RecordingSessions());

        private ShareLinksAdminController Controller(
            IShareStore store,
            Guid? caller,
            TimeProvider clock,
            RecordingSessions sessions)
            => Controller(store, caller, clock, sessions, new RecordingAccounts());

        private ShareLinksAdminController Controller(
            IShareStore store,
            Guid? caller,
            TimeProvider clock,
            RecordingSessions sessions,
            RecordingAccounts accounts)
        => new ShareLinksAdminController(
            store,
            _keyFile,
            accounts.Manager,
            Mock.Of<ILibraryManager>(MockBehavior.Strict),
            Mock.Of<IPluginConfigurationSource>(MockBehavior.Strict),
            ContextFor(caller),
            sessions.Manager,
            clock,
            NullLogger<ShareLinksAdminController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };

    // The accounts a server holds, as far as the revocation can see them: what
    // each one carries as a session ceiling, and the policy it was last written.
    // Strict, so a route reaching for a member nobody expected fails here rather
    // than passing quietly.
    //
    // GetUserById answers with the ceiling this fake was told the account carries,
    // which is the mapping GuestAccounts.DisableAsync states as a claim rather
    // than a measurement: the server's own translation between a policy and the
    // account row is not in this tree and was not read.
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
                .Callback((Guid id, UserPolicy policy) =>
                {
                    Written.Add(id);
                    Policies[id] = policy;
                })
                .Returns(Task.CompletedTask);
        }

        public IUserManager Manager => _manager.Object;

        // The accounts a policy was written onto, in the order they were written,
        // so a second write on one account is visible rather than collapsed.
        public List<Guid> Written { get; } = new List<Guid>();

        public Dictionary<Guid, UserPolicy> Policies { get; } = new Dictionary<Guid, UserPolicy>();

        public Dictionary<Guid, int> Carries { get; } = new Dictionary<Guid, int>();
    }

    // A session manager that answers every ask and writes down which accounts it
    // was asked about, in the order it was asked. Nothing here reaches a server:
    // what the assertions below judge is which accounts this plugin asked to be
    // signed out, which is the reachable half of #55 and the one
    // `docs/refused-tests.md` names as standing in for the segment the server
    // holds open.
    private sealed class RecordingSessions
    {
        private readonly List<Guid> _revoked = new List<Guid>();
        private readonly Mock<ISessionManager> _manager = new Mock<ISessionManager>();

        public RecordingSessions()
        {
            _manager
                .Setup(manager => manager.RevokeUserTokens(It.IsAny<Guid>(), It.IsAny<string>()))
                .Callback((Guid account, string spared) =>
                {
                    _revoked.Add(account);
                    Spared.Add(spared);
                })
                .Returns(Task.CompletedTask);
        }

        public ISessionManager Manager => _manager.Object;

        public IReadOnlyList<Guid> Revoked => _revoked;

        public List<string> Spared { get; } = new List<string>();
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

    // What the result would write, as the string two of them are compared by.
    // The same shape GuestRouteTests uses, so an answer here can be compared with
    // an answer there without translating one of them.
    private static async Task<string> Bytes(ActionResult answer)
    {
        var body = new MemoryStream();
        var http = new DefaultHttpContext
        {
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

    private static TimeProvider At(DateTimeOffset instant) => new FixedClock(instant);

    private sealed class FixedClock : TimeProvider
    {
        private readonly DateTimeOffset _instant;

        public FixedClock(DateTimeOffset instant) => _instant = instant;

        public override DateTimeOffset GetUtcNow() => _instant;
    }
}
