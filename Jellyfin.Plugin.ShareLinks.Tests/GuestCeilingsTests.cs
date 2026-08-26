using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Jellyfin.Plugin.ShareLinks.Tests;

/// <summary>
/// The ceiling the administrator surface reports for each account a share names (#64).
/// </summary>
/// <remarks>
/// <para>
/// #64's first paragraph is the failure these are about: an operator lowers a
/// number and nothing changes, because one of the other two ceilings was the one
/// holding. So every assertion here compares the pair rather than the number. One
/// that compared the number alone would pass an implementation that named the
/// wrong ceiling, which is the bug wearing a different face.
/// </para>
/// <para>
/// The arithmetic is <c>EffectiveBitrateTests</c>' and is not repeated. What is
/// judged here is the step above it: that the three inputs handed to it are the
/// three that would be handed to it on a request, that the answer is per account
/// rather than per share, and that an account this plugin does not cap is a
/// different answer from an account with no ceiling.
/// </para>
/// <para>
/// Nothing here reaches a server. The account's own limit arrives as a function
/// the caller supplies, which is <c>ServerCeilings.OfAccount</c> on a request and
/// a dictionary here, and <c>docs/testing.md</c> is where the rule that keeps it
/// that way is written.
/// </para>
/// </remarks>
public class GuestCeilingsTests
{
    private static readonly DateTimeOffset Now = new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid Operator = new Guid("33333333-3333-3333-3333-333333333333");
    private static readonly Guid Guest = new Guid("11111111-1111-1111-1111-111111111111");
    private static readonly Guid SecondGuest = new Guid("22222222-2222-2222-2222-222222222222");
    private static readonly Guid Item = new Guid("55555555-5555-5555-5555-555555555555");

    /// <summary>
    /// Gets the ceilings in play, against the number and the names that come out.
    /// </summary>
    /// <remarks>
    /// Every combination of the three being set and unset, then one row per
    /// position for the lowest arriving from that position, then the ties. It is
    /// the shape <c>EffectiveBitrateTests</c> has, driven through the routine the
    /// surface actually calls, because that is where an input can go missing
    /// without the arithmetic noticing.
    /// </remarks>
    public static TheoryData<long?, long?, long?, long?, BitrateCeiling> Ceilings => new()
    {
        { null, null, null, null, BitrateCeiling.None },
        { 6_000_000, null, null, 6_000_000, BitrateCeiling.Share },
        { null, 4_000_000, null, 4_000_000, BitrateCeiling.Account },
        { null, null, 2_000_000, 2_000_000, BitrateCeiling.ServerRemoteClientLimit },
        { 6_000_000, 4_000_000, null, 4_000_000, BitrateCeiling.Account },
        { 6_000_000, null, 2_000_000, 2_000_000, BitrateCeiling.ServerRemoteClientLimit },
        { null, 4_000_000, 2_000_000, 2_000_000, BitrateCeiling.ServerRemoteClientLimit },
        { 6_000_000, 4_000_000, 2_000_000, 2_000_000, BitrateCeiling.ServerRemoteClientLimit },
        { 1_000_000, 4_000_000, 2_000_000, 1_000_000, BitrateCeiling.Share },
        { 6_000_000, 1_000_000, 2_000_000, 1_000_000, BitrateCeiling.Account },
        { 6_000_000, 4_000_000, 1_000_000, 1_000_000, BitrateCeiling.ServerRemoteClientLimit },
        { 3_000_000, 3_000_000, null, 3_000_000, BitrateCeiling.Share | BitrateCeiling.Account },
        { 3_000_000, null, 3_000_000, 3_000_000, BitrateCeiling.Share | BitrateCeiling.ServerRemoteClientLimit },
        { null, 3_000_000, 3_000_000, 3_000_000, BitrateCeiling.Account | BitrateCeiling.ServerRemoteClientLimit },
        {
            3_000_000, 3_000_000, 3_000_000, 3_000_000,
            BitrateCeiling.Share | BitrateCeiling.Account | BitrateCeiling.ServerRemoteClientLimit
        },
    };

    /// <summary>
    /// The number the surface reports and the ceilings it names, over every
    /// combination of the three.
    /// </summary>
    /// <param name="share">The share's own ceiling.</param>
    /// <param name="account">The invited account's own remote client limit.</param>
    /// <param name="server">The server configuration's remote client limit.</param>
    /// <param name="expected">The number that should come out.</param>
    /// <param name="applied">The ceilings that should be named.</param>
    [Theory]
    [MemberData(nameof(Ceilings))]
    public async Task TheCeilingReportedIsTheLowestAndEveryCeilingAtItIsNamed(
        long? share,
        long? account,
        long? server,
        long? expected,
        BitrateCeiling applied)
    {
        var record = ALiveShare(cap: share);

        var answer = Assert.Single(await Answers(
            new[] { record },
            record,
            _ => account,
            server,
            Now));

        Assert.Equal(Guest, answer.UserId);
        Assert.Equal(GuestVerdict.Reaches, answer.Reach);
        Assert.Equal(expected, answer.Cap.BitsPerSecond);
        Assert.Equal(applied, answer.Cap.Applied);
    }

    /// <summary>
    /// Two guests on one share get two answers, because the account limit is the
    /// one input of the three that is not a property of the share.
    /// </summary>
    /// <remarks>
    /// This is why the answer is a list. A single number on the row would be
    /// right for one of these two and wrong for the other, and nothing on the
    /// page would say which.
    /// </remarks>
    [Fact]
    public async Task TwoGuestsOnOneShareGetTheirOwnAnswers()
    {
        var record = ALiveShare(cap: 6_000_000, invited: new[] { Guest, SecondGuest });
        var limits = new Dictionary<Guid, long?> { [Guest] = 4_000_000, [SecondGuest] = null };

        var answers = await Answers(new[] { record }, record, account => limits[account], null, Now);

        Assert.Equal(new[] { Guest, SecondGuest }, answers.Select(answer => answer.UserId).ToArray());
        Assert.Equal(4_000_000, answers[0].Cap.BitsPerSecond);
        Assert.Equal(BitrateCeiling.Account, answers[0].Cap.Applied);
        Assert.Equal(6_000_000, answers[1].Cap.BitsPerSecond);
        Assert.Equal(BitrateCeiling.Share, answers[1].Cap.Applied);
    }

    /// <summary>
    /// A second live share of the same item, to the same guest, with a tighter
    /// ceiling, is part of this share's answer.
    /// </summary>
    /// <remarks>
    /// This is #64's own failure at its sharpest, and it is the reason the answer
    /// is computed over the whole store rather than over the record being reported
    /// on. An operator lowers the ceiling on the share they are looking at, the
    /// row moves, and the guest keeps watching at the other share's number. The
    /// rule is <see cref="GuestConfinement.Decide"/>'s, so the surface and the
    /// filter cannot disagree about it.
    /// </remarks>
    [Fact]
    public async Task ASecondLiveShareOfTheSameItemLowersWhatThisOneReports()
    {
        var looked = ALiveShare(cap: 6_000_000);
        var other = ALiveShare(cap: 2_000_000);

        var answer = Assert.Single(await Answers(new[] { looked, other }, looked, _ => null, null, Now));

        Assert.Equal(2_000_000, answer.Cap.BitsPerSecond);
        Assert.Equal(BitrateCeiling.Share, answer.Cap.Applied);
    }

    /// <summary>
    /// An invited account this plugin did not create is not capped by this plugin
    /// at all, and that is not the same answer as no ceiling being set.
    /// </summary>
    /// <remarks>
    /// The filter stands in front of the accounts this plugin made and no others,
    /// which is <c>docs/guest-confinement.md</c>'s rule and the reason
    /// <see cref="GuestVerdict.NotAGuestOfThisPlugin"/> exists. A row that showed
    /// this account the share's own ceiling would be telling an operator a number
    /// is enforced on somebody it is not enforced on.
    /// </remarks>
    [Fact]
    public async Task AnInvitedAccountThisPluginDidNotMakeIsNotCappedByIt()
    {
        var record = ALiveShare(cap: 6_000_000, pluginCreated: Array.Empty<Guid>());

        var answer = Assert.Single(await Answers(new[] { record }, record, _ => 4_000_000, 2_000_000, Now));

        Assert.Equal(GuestVerdict.NotAGuestOfThisPlugin, answer.Reach);
        Assert.Null(answer.Cap.BitsPerSecond);
        Assert.Equal(BitrateCeiling.None, answer.Cap.Applied);
    }

    /// <summary>
    /// A share that has stopped has no ceiling in force, whichever way it stopped.
    /// </summary>
    /// <param name="revoked">Whether the share was revoked rather than expired.</param>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AShareThatHasStoppedHasNoCeilingInForce(bool revoked)
    {
        var record = revoked
            ? ALiveShare(cap: 6_000_000, revokedAt: Now.AddHours(-1))
            : ALiveShare(cap: 6_000_000, expiresAt: Now.AddHours(-1));

        var answer = Assert.Single(await Answers(new[] { record }, record, _ => 4_000_000, 2_000_000, Now));

        Assert.Equal(GuestVerdict.RefusedNothingLive, answer.Reach);
        Assert.Null(answer.Cap.BitsPerSecond);
        Assert.Equal(BitrateCeiling.None, answer.Cap.Applied);
    }

    /// <summary>
    /// A share that has stopped while the same guest still holds a live share of
    /// another item reports nothing for this item rather than that item's ceiling.
    /// </summary>
    /// <remarks>
    /// The nearer mistake than the one above. A routine asking only whether the
    /// guest has any live share left would answer this row with a number belonging
    /// to a different item, and the row would read as a share still capping
    /// something.
    /// </remarks>
    [Fact]
    public async Task AStoppedShareIsNotAnsweredWithAnotherItemsCeiling()
    {
        var stopped = ALiveShare(cap: 6_000_000, revokedAt: Now.AddHours(-1));
        var elsewhere = ALiveShare(cap: 1_000_000, item: Guid.NewGuid());

        var answer = Assert.Single(await Answers(new[] { stopped, elsewhere }, stopped, _ => null, null, Now));

        Assert.Equal(GuestVerdict.RefusedItemNotShared, answer.Reach);
        Assert.Null(answer.Cap.BitsPerSecond);
    }

    /// <summary>
    /// A share naming nobody answers with nothing rather than with one entry
    /// carrying an empty account.
    /// </summary>
    [Fact]
    public async Task AShareNamingNobodyAnswersWithNoCeilings()
    {
        var record = ALiveShare(invited: Array.Empty<Guid>(), pluginCreated: Array.Empty<Guid>());

        Assert.Empty(await Answers(new[] { record }, record, _ => 4_000_000, 2_000_000, Now));
    }

    /// <summary>
    /// The three arguments that may not be missing are refused where they are,
    /// rather than as a null reference somewhere further in.
    /// </summary>
    [Fact]
    public async Task TheArgumentsThatMayNotBeMissingAreRefused()
    {
        var record = ALiveShare();

        await Assert.ThrowsAsync<ArgumentNullException>(() => Answers(null!, record, _ => null, null, Now));
        await Assert.ThrowsAsync<ArgumentNullException>(() => Answers(new[] { record }, null!, _ => null, null, Now));
        await Assert.ThrowsAsync<ArgumentNullException>(() => Answers(new[] { record }, record, null!, null, Now));

        // The fourth is #286's, and it is refused in the same place rather than
        // being read as "ask nothing". A caller with no way to ask whether a
        // ceiling can be met is a caller whose column would be silently wrong.
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => GuestCeilings.OfAsync(new[] { record }, record, _ => null, null, null!, Now));
    }

    /// <summary>
    /// A ceiling in force, with the item playable well below it: it can be met,
    /// and the column says which of the ways rather than only yes.
    /// </summary>
    /// <returns>A task that completes when the assertions have been made.</returns>
    [Fact]
    public async Task ACeilingTheItemFitsUnderSaysAVersionIsWithinIt()
    {
        var record = ALiveShare(cap: 6_000_000);

        var answer = Assert.Single(await Answers(
            new[] { record },
            record,
            _ => null,
            null,
            Now,
            Playing(new PlayableVersion(1_500_000, false))));

        Assert.Equal(6_000_000, answer.Cap.BitsPerSecond);
        Assert.Equal(CapReach.AVersionIsWithinIt, answer.CanBeMet);
    }

    /// <summary>
    /// The condition this column exists for. Every version is above the ceiling
    /// and none of them can be brought under it, so the share is one nothing can
    /// be served through.
    /// </summary>
    /// <returns>A task that completes when the assertions have been made.</returns>
    [Fact]
    public async Task ACeilingBelowEveryVersionSaysNothingCanBeServed()
    {
        var record = ALiveShare(cap: 200_000);

        var answer = Assert.Single(await Answers(
            new[] { record },
            record,
            _ => null,
            null,
            Now,
            Playing(new PlayableVersion(4_000_000, false), new PlayableVersion(1_500_000, false))));

        Assert.Equal(CapReach.NothingCanBeServed, answer.CanBeMet);
    }

    /// <summary>
    /// The same ceiling and the same version, with the account permitted to
    /// transcode: the ceiling can be met, by transcoding. Without this the test
    /// above is satisfied by a column that says nothing can be served whenever a
    /// ceiling is set at all.
    /// </summary>
    /// <returns>A task that completes when the assertions have been made.</returns>
    [Fact]
    public async Task TheSameCeilingIsMetByTranscodingWhereTheAccountMay()
    {
        var record = ALiveShare(cap: 200_000);

        var answer = Assert.Single(await Answers(
            new[] { record },
            record,
            _ => null,
            null,
            Now,
            Playing(true, new PlayableVersion(4_000_000, true))));

        Assert.Equal(CapReach.OnlyByTranscoding, answer.CanBeMet);
    }

    /// <summary>
    /// A share with no ceiling anywhere has nothing to meet, and the server is
    /// never asked. Two guests, two accounts, and no lookup at all.
    /// </summary>
    /// <remarks>
    /// The delegate counts rather than answers, so this is a statement about what
    /// was asked rather than about what came back. It is the cost paragraph of
    /// #286 held to by a test: the library call is paid where there is a ceiling
    /// and nowhere else.
    /// </remarks>
    /// <returns>A task that completes when the assertions have been made.</returns>
    [Fact]
    public async Task AShareWithNoCeilingAsksTheServerNothing()
    {
        var record = ALiveShare(cap: null, invited: new[] { Guest, SecondGuest }, pluginCreated: new[] { Guest, SecondGuest });
        var asked = new List<Guid>();

        var answers = await GuestCeilings.OfAsync(
            new[] { record },
            record,
            _ => null,
            null,
            account =>
            {
                asked.Add(account);
                return Task.FromResult(AccountPlayback.Nothing);
            },
            Now);

        Assert.Empty(asked);
        Assert.All(answers, answer => Assert.Equal(CapReach.NoCeilingIsSet, answer.CanBeMet));
    }

    /// <summary>
    /// The same share with a ceiling on it does ask, once per invited account,
    /// so the emptiness above is the absence of a ceiling and not a column that
    /// never asks anything.
    /// </summary>
    /// <returns>A task that completes when the assertions have been made.</returns>
    [Fact]
    public async Task AShareWithACeilingAsksOncePerInvitedAccount()
    {
        var record = ALiveShare(cap: 6_000_000, invited: new[] { Guest, SecondGuest }, pluginCreated: new[] { Guest, SecondGuest });
        var asked = new List<Guid>();

        await GuestCeilings.OfAsync(
            new[] { record },
            record,
            _ => null,
            null,
            account =>
            {
                asked.Add(account);
                return Task.FromResult(new AccountPlayback(new[] { new PlayableVersion(1_000_000, false) }, false));
            },
            Now);

        Assert.Equal(new[] { Guest, SecondGuest }, asked);
    }

    /// <summary>
    /// An invited account this plugin does not cap has nothing to meet either,
    /// and the two absences are the same member here on purpose: there is no
    /// ceiling of this plugin's on that account, so there is nothing for the item
    /// to fit under.
    /// </summary>
    /// <returns>A task that completes when the assertions have been made.</returns>
    [Fact]
    public async Task AnAccountThisPluginDoesNotCapHasNothingToMeet()
    {
        var record = ALiveShare(cap: 200_000, pluginCreated: Array.Empty<Guid>());

        var answer = Assert.Single(await Answers(
            new[] { record },
            record,
            _ => null,
            null,
            Now,
            Playing(new PlayableVersion(4_000_000, false))));

        Assert.Equal(GuestVerdict.NotAGuestOfThisPlugin, answer.Reach);
        Assert.Equal(CapReach.NoCeilingIsSet, answer.CanBeMet);
    }

    /// <summary>
    /// A server that said nothing about the item is a question nobody answered
    /// rather than a share that cannot be served.
    /// </summary>
    /// <returns>A task that completes when the assertions have been made.</returns>
    [Fact]
    public async Task AnItemTheServerSaidNothingAboutIsNotKnownRatherThanRefused()
    {
        var record = ALiveShare(cap: 200_000);

        var answer = Assert.Single(await Answers(
            new[] { record },
            record,
            _ => null,
            null,
            Now,
            _ => Task.FromResult(AccountPlayback.Nothing)));

        Assert.Equal(CapReach.NotKnown, answer.CanBeMet);
    }

    // The ceilings, with a playback answer that says nothing unless a test hands
    // one in. Nothing here reaches a server, which is docs/testing.md's rule, and
    // the delegate is what keeps it that way on this routine as well.
    private static Task<IReadOnlyList<GuestCeiling>> Answers(
        IReadOnlyList<ShareRecord> records,
        ShareRecord record,
        Func<Guid, long?> accountCeiling,
        long? serverCeiling,
        DateTimeOffset now,
        Func<Guid, Task<AccountPlayback>>? playback = null)
        => GuestCeilings.OfAsync(
            records,
            record,
            accountCeiling,
            serverCeiling,
            playback ?? (_ => Task.FromResult(AccountPlayback.Nothing)),
            now);

    private static Func<Guid, Task<AccountPlayback>> Playing(params PlayableVersion[] versions)
        => Playing(false, versions);

    private static Func<Guid, Task<AccountPlayback>> Playing(bool mayTranscode, params PlayableVersion[] versions)
        => _ => Task.FromResult(new AccountPlayback(versions, mayTranscode));

    private static ShareRecord ALiveShare(
        long? cap = null,
        IReadOnlyList<Guid>? invited = null,
        IReadOnlyList<Guid>? pluginCreated = null,
        Guid? item = null,
        DateTimeOffset? expiresAt = null,
        DateTimeOffset? revokedAt = null) => new ShareRecord
        {
            SchemaVersion = ShareRecord.CurrentSchemaVersion,
            Id = Guid.NewGuid(),
            ItemId = item ?? Item,
            InvitedUserIds = invited ?? new[] { Guest },
            PluginCreatedUserIds = pluginCreated ?? invited ?? new[] { Guest },
            CreatedByUserId = Operator,
            CreatedAt = Now.AddDays(-1),
            ExpiresAt = expiresAt ?? Now.AddDays(7),
            RevokedAt = revokedAt,
            RevokedByUserId = revokedAt is null ? null : Operator,
            MaxBitrateBitsPerSecond = cap,
            TokenHash = "a-hash",
        };
}
