using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.ShareLinks;
using MediaBrowser.Model.Plugins;
using Xunit;

namespace Jellyfin.Plugin.ShareLinks.Tests;

/// <summary>
/// The enumeration #48 asks for: every combination the one resolution routine has
/// to answer, and the answer it gives.
/// </summary>
/// <remarks>
/// <para>
/// Written as a table rather than as a test per condition, because the failure
/// this issue is about is two conditions disagreeing, and a table is the shape
/// where a missing row is visible. Each row names the situation in the words the
/// issue uses.
/// </para>
/// <para>
/// Nothing here sleeps and nothing reads the machine clock. The clock is a seam
/// (#36), so standing one tick either side of an expiry costs a subtraction.
/// </para>
/// </remarks>
public class ShareResolutionTests
{
    private static readonly DateTimeOffset Expiry = new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid Invited = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Stranger = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly byte[] Key = Enumerable.Range(0, ShareTokenHash.MinimumKeyBytes).Select(b => (byte)b).ToArray();

    /// <summary>
    /// One row per situation the decision has to answer for.
    /// </summary>
    /// <returns>The situation, and what the routine has to say about it.</returns>
    public static TheoryData<string, ShareRefusal> TheDecisionTable() => new TheoryData<string, ShareRefusal>
    {
        { "a valid token, from the account the share names, before it expires", ShareRefusal.None },
        { "no token at all", ShareRefusal.NoTokenPresented },
        { "an empty token", ShareRefusal.NoTokenPresented },
        { "a token no record answers for", ShareRefusal.NoSuchShare },
        { "a well-formed token for a store with nothing in it", ShareRefusal.NoSuchShare },
        { "a share that was revoked", ShareRefusal.Revoked },
        { "a share at the instant it expires", ShareRefusal.Expired },
        { "a share one tick past the instant it expires", ShareRefusal.Expired },
        { "a caller the server has not identified", ShareRefusal.CallerNotSignedIn },
        { "a caller the share does not name", ShareRefusal.CallerNotInvited },
        { "a live share while the plugin is not active", ShareRefusal.PluginNotActive },
    };

    [Theory]
    [MemberData(nameof(TheDecisionTable))]
    public void TheDecisionAnswersEverySituationTheSameWayEveryTime(string situation, ShareRefusal expected)
    {
        var result = Decide(situation);

        Assert.Equal(expected, result.Refusal);
        Assert.Equal(expected == ShareRefusal.None, result.IsResolved);
    }

    /// <summary>
    /// The boundary belongs to the refusal, which <c>docs/expiry.md</c> decides:
    /// live strictly before the instant, refused at it and after it.
    /// </summary>
    [Fact]
    public void OneTickBeforeTheInstantIsLiveAndTheInstantItselfIsNot()
    {
        var records = new[] { ARecord() };

        var justBefore = ShareResolution.Resolve(records, Key, "a-token", Invited, PluginStatus.Active, At(Expiry.AddTicks(-1)));
        var atTheInstant = ShareResolution.Resolve(records, Key, "a-token", Invited, PluginStatus.Active, At(Expiry));

        Assert.True(justBefore.IsResolved);
        Assert.Equal(ShareRefusal.Expired, atTheInstant.Refusal);
    }

    /// <summary>
    /// A share the server no longer holds the item of is refused, and it is
    /// refused by the decision rather than by the route that supplies the
    /// question (#39).
    /// </summary>
    /// <remarks>
    /// Driven here on the overload that takes the key as bytes, so what is
    /// asserted is the decision alone: no key file, no store and no controller
    /// stand between the answer and the assertion.
    /// </remarks>
    [Fact]
    public void AShareWhoseItemTheServerNoLongerHoldsIsRefusedByTheDecision()
    {
        var records = new[] { ARecord() };

        var result = ShareResolution.Resolve(
            records,
            Key,
            "a-token",
            Invited,
            PluginStatus.Active,
            At(Expiry.AddTicks(-1)),
            _ => false);

        Assert.Equal(ShareRefusal.ItemGone, result.Refusal);
        Assert.False(result.IsResolved);
    }

    /// <summary>
    /// The same decision with the item still there resolves, so the refusal
    /// above is the answer to the question and not a routine that refuses
    /// whenever it is asked one.
    /// </summary>
    [Fact]
    public void TheSameDecisionResolvesWhileTheServerStillHoldsTheItem()
    {
        var records = new[] { ARecord() };

        var result = ShareResolution.Resolve(
            records,
            Key,
            "a-token",
            Invited,
            PluginStatus.Active,
            At(Expiry.AddTicks(-1)),
            _ => true);

        Assert.True(result.IsResolved);
        Assert.Equal(ShareRefusal.None, result.Refusal);
    }

    /// <summary>
    /// The item question is not asked of a refusal, whichever of the earlier
    /// reasons produced it. A caller who was refused anyway does not make the
    /// server look anything up, and a token naming a live share must not cost
    /// more than one naming nothing (#26).
    /// </summary>
    [Fact]
    public void NoRefusalReachesTheItemQuestion()
    {
        var asked = 0;
        var records = new[] { ARecord(), ARecord(token: "revoked-token", revokedAt: Expiry.AddDays(-1)) };

        bool Count(Guid _)
        {
            asked++;
            return true;
        }

        var live = At(Expiry.AddTicks(-1));
        ShareResolution.Resolve(records, Key, "not-the-token", Invited, PluginStatus.Active, live, Count);
        ShareResolution.Resolve(records, Key, "a-token", Stranger, PluginStatus.Active, live, Count);
        ShareResolution.Resolve(records, Key, "a-token", null, PluginStatus.Active, live, Count);
        ShareResolution.Resolve(records, Key, "revoked-token", Invited, PluginStatus.Active, live, Count);
        ShareResolution.Resolve(records, Key, "a-token", Invited, PluginStatus.Active, At(Expiry), Count);
        ShareResolution.Resolve(records, Key, "a-token", Invited, PluginStatus.Disabled, live, Count);
        ShareResolution.Resolve(records, Key, string.Empty, Invited, PluginStatus.Active, live, Count);

        Assert.Equal(0, asked);

        ShareResolution.Resolve(records, Key, "a-token", Invited, PluginStatus.Active, live, Count);

        Assert.Equal(1, asked);
    }

    /// <summary>
    /// A decision handed no way to ask about the item refuses to be made at
    /// all, rather than treating the absent question as a yes.
    /// </summary>
    [Fact]
    public void TheItemQuestionIsRequiredWhereTheOverloadTakingItIsUsed()
    {
        var records = new[] { ARecord() };

        Assert.Throws<ArgumentNullException>(() => ShareResolution.Resolve(
            records,
            Key,
            "a-token",
            Invited,
            PluginStatus.Active,
            At(Expiry.AddTicks(-1)),
            theServerStillHoldsTheItem: null!));
    }
    /// <summary>
    /// A revoked share reports revocation even where it has also expired. The
    /// order is argued on the routine: revocation is the answer that does not
    /// depend on a clock, and a clock is the thing that steps.
    /// </summary>
    [Fact]
    public void RevocationIsTheReasonReportedForAShareThatIsAlsoExpired()
    {
        var records = new[] { ARecord(revokedAt: Expiry.AddDays(-1)) };

        var result = ShareResolution.Resolve(records, Key, "a-token", Invited, PluginStatus.Active, At(Expiry.AddDays(1)));

        Assert.Equal(ShareRefusal.Revoked, result.Refusal);
    }

    /// <summary>
    /// Whether the caller is invited is asked after the record's state, so a
    /// stranger presenting a token for an expired share learns nothing about the
    /// share that a stranger presenting one for a live share does not.
    /// </summary>
    [Fact]
    public void TheRecordsStateIsDecidedBeforeTheCallerIs()
    {
        var records = new[] { ARecord() };

        var result = ShareResolution.Resolve(records, Key, "a-token", Stranger, PluginStatus.Active, At(Expiry.AddDays(1)));

        Assert.Equal(ShareRefusal.Expired, result.Refusal);
    }

    /// <summary>
    /// A share the operator disabled the plugin over stops resolving on the next
    /// request rather than after the next restart, which is what
    /// <c>docs/plugin-lifecycle.md</c> asks of this routine, and it is the test
    /// #38 names.
    /// </summary>
    /// <param name="status">A status that is not Active.</param>
    [Theory]
    [InlineData(PluginStatus.Disabled)]
    [InlineData(PluginStatus.Restart)]
    [InlineData(PluginStatus.Malfunctioned)]
    [InlineData(PluginStatus.NotSupported)]
    [InlineData(PluginStatus.Superseded)]
    [InlineData(PluginStatus.Deleted)]
    public void APluginThatIsNotActiveRefusesALiveShare(PluginStatus status)
    {
        var records = new[] { ARecord() };

        var result = ShareResolution.Resolve(records, Key, "a-token", Invited, status, At(Expiry.AddDays(-1)));

        Assert.Equal(ShareRefusal.PluginNotActive, result.Refusal);
        Assert.Null(result.Share);
    }

    /// <summary>
    /// Restart is the status a plugin an operator has just disabled carries in
    /// memory, which is why it is in the list above rather than treated as a
    /// plugin that is on its way back.
    /// </summary>
    [Fact]
    public void OnlyActiveResolves()
    {
        var records = new[] { ARecord() };

        var result = ShareResolution.Resolve(records, Key, "a-token", Invited, PluginStatus.Active, At(Expiry.AddDays(-1)));

        Assert.True(result.IsResolved);
    }

    /// <summary>
    /// The share the routine hands back is the record the token names, and not
    /// one of its neighbours in the store.
    /// </summary>
    [Fact]
    public void TheShareHandedBackIsTheOneTheTokenNames()
    {
        var wanted = ARecord(token: "a-token");
        var records = new[] { ARecord(token: "another-token"), wanted, ARecord(token: "a-third-token") };

        var result = ShareResolution.Resolve(records, Key, "a-token", Invited, PluginStatus.Active, At(Expiry.AddDays(-1)));

        Assert.Same(wanted, result.Share);
    }

    /// <summary>
    /// A verdict carries a share or a reason and never both, so a caller cannot
    /// read the record off a refusal.
    /// </summary>
    /// <param name="situation">A situation from the table that refuses.</param>
    [Theory]
    [InlineData("a token no record answers for")]
    [InlineData("a caller the share does not name")]
    [InlineData("a share that was revoked")]
    [InlineData("a live share while the plugin is not active")]
    public void ARefusalCarriesNoShare(string situation)
    {
        var result = Decide(situation);

        Assert.Null(result.Share);
        Assert.False(result.IsResolved);
    }

    private static ShareResolutionResult Decide(string situation)
    {
        var live = At(Expiry.AddDays(-1));

        return situation switch
        {
            "a valid token, from the account the share names, before it expires"
                => ShareResolution.Resolve(new[] { ARecord() }, Key, "a-token", Invited, PluginStatus.Active, live),
            "no token at all"
                => ShareResolution.Resolve(new[] { ARecord() }, Key, null, Invited, PluginStatus.Active, live),
            "an empty token"
                => ShareResolution.Resolve(new[] { ARecord() }, Key, string.Empty, Invited, PluginStatus.Active, live),
            "a token no record answers for"
                => ShareResolution.Resolve(new[] { ARecord() }, Key, "some-other-token", Invited, PluginStatus.Active, live),
            "a well-formed token for a store with nothing in it"
                => ShareResolution.Resolve(Array.Empty<ShareRecord>(), Key, "a-token", Invited, PluginStatus.Active, live),
            "a share that was revoked"
                => ShareResolution.Resolve(new[] { ARecord(revokedAt: Expiry.AddDays(-2)) }, Key, "a-token", Invited, PluginStatus.Active, live),
            "a share at the instant it expires"
                => ShareResolution.Resolve(new[] { ARecord() }, Key, "a-token", Invited, PluginStatus.Active, At(Expiry)),
            "a share one tick past the instant it expires"
                => ShareResolution.Resolve(new[] { ARecord() }, Key, "a-token", Invited, PluginStatus.Active, At(Expiry.AddTicks(1))),
            "a caller the server has not identified"
                => ShareResolution.Resolve(new[] { ARecord() }, Key, "a-token", null, PluginStatus.Active, live),
            "a caller the share does not name"
                => ShareResolution.Resolve(new[] { ARecord() }, Key, "a-token", Stranger, PluginStatus.Active, live),
            "a live share while the plugin is not active"
                => ShareResolution.Resolve(new[] { ARecord() }, Key, "a-token", Invited, PluginStatus.Disabled, live),
            _ => throw new ArgumentOutOfRangeException(nameof(situation), situation, "The table names a situation this method does not build."),
        };
    }

    private static ShareRecord ARecord(string token = "a-token", DateTimeOffset? revokedAt = null) => new ShareRecord
    {
        SchemaVersion = ShareRecord.CurrentSchemaVersion,
        Id = Guid.NewGuid(),
        ItemId = Guid.NewGuid(),
        InvitedUserIds = new[] { Invited },
        CreatedByUserId = Guid.NewGuid(),
        CreatedAt = Expiry.AddDays(-7),
        ExpiresAt = Expiry,
        RevokedAt = revokedAt,
        TokenHash = ShareTokenHash.Compute(Key, token),
    };

    private static TimeProvider At(DateTimeOffset instant) => new FixedClock(instant);

    /// <summary>
    /// A clock that stands still wherever it is put.
    /// </summary>
    /// <remarks>
    /// The seam is the framework's own <see cref="TimeProvider"/>, so nothing here
    /// invents a clock interface. What this overrides is the one method the
    /// decision calls.
    /// </remarks>
    private sealed class FixedClock : TimeProvider
    {
        private readonly DateTimeOffset _instant;

        public FixedClock(DateTimeOffset instant) => _instant = instant;

        public override DateTimeOffset GetUtcNow() => _instant;
    }
}
