using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.ShareLinks;
using MediaBrowser.Model.Plugins;
using Xunit;

namespace Jellyfin.Plugin.ShareLinks.Tests;

/// <summary>
/// The five ways invitation and expiry can disagree, and the outcome each one has
/// (#54).
/// </summary>
/// <remarks>
/// <para>
/// A row here is a change rather than a state. <c>DecisionTableTests</c> holds the
/// product of the five inputs the decision takes, which answers whether any
/// combination is unhandled; this holds what happens when the world moves under a
/// share that was working, which is a different question and the one an operator
/// actually meets. Both read the same routine and neither derives its expectations
/// from it.
/// </para>
/// <para>
/// So every row is two requests: one before the change and one after it, with the
/// change in between and nothing else touched. A row that only asserted the second
/// request would pass just as well against a share that never worked.
/// </para>
/// <para>
/// Nothing here sleeps and nothing reads the machine clock. The clock is a seam
/// (#36), so standing either side of an instant costs a subtraction.
/// </para>
/// <para>
/// Two of the five rows are about an account rather than about a record, and half
/// of each is the server's. That the server identifies nobody as a disabled or
/// deleted account is the server's behaviour and is not measured here. What is
/// measured is this plugin's half: what the decision does when the server
/// identifies nobody, and that neither event edits the record.
/// <c>docs/invitation-and-expiry.md</c> says the same thing where an operator
/// reads it.
/// </para>
/// </remarks>
public class InvitationAndExpiryTests
{
    private static readonly DateTimeOffset Expiry = new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset WhileLive = Expiry.AddDays(-1);
    private static readonly DateTimeOffset AfterExpiry = Expiry.AddTicks(1);
    private static readonly Guid Guest = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid TheAccountThatReplacedIt = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly byte[] Key = Enumerable.Range(0, ShareTokenHash.MinimumKeyBytes).Select(value => (byte)value).ToArray();

    /// <summary>
    /// Gets one row per combination the issue names, with the answer before the
    /// change and the answer after it.
    /// </summary>
    public static TheoryData<string, ShareRefusal, ShareRefusal> TheCombinations =>
        new TheoryData<string, ShareRefusal, ShareRefusal>
        {
            { "the invitation is withdrawn while the share is live", ShareRefusal.None, ShareRefusal.CallerNotInvited },
            { "the share expires while the guest is still invited", ShareRefusal.None, ShareRefusal.Expired },
            { "the guest account is disabled by the operator", ShareRefusal.None, ShareRefusal.CallerNotSignedIn },
            { "the guest account is deleted", ShareRefusal.None, ShareRefusal.CallerNotSignedIn },
            { "the guest is invited again after the share expired", ShareRefusal.Expired, ShareRefusal.Expired },
        };

    [Theory]
    [MemberData(nameof(TheCombinations))]
    public void EveryCombinationHasTheOutcomeTheDocumentGivesIt(string combination, ShareRefusal before, ShareRefusal after)
    {
        var (first, second) = Play(combination);

        Assert.Equal(before, first.Refusal);
        Assert.Equal(after, second.Refusal);

        // A refusal hands back no share, and a success hands one back. Asserting
        // the reason alone would pass an implementation that refused and returned
        // the record anyway.
        Assert.Equal(before == ShareRefusal.None, first.IsResolved);
        Assert.Equal(after == ShareRefusal.None, second.IsResolved);
    }

    /// <summary>
    /// The clause the issue ends on: no combination reaches a branch that succeeds
    /// without both conditions holding.
    /// </summary>
    /// <remarks>
    /// Asserted over the product of the two conditions rather than by reading the
    /// routine, because what this refuses is a later edit that makes one of them
    /// sufficient. The success case is in the product too, so a routine that
    /// refused everything would fail here rather than pass for the wrong reason.
    /// </remarks>
    [Theory]
    [InlineData(true, true, true)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, false)]
    public void SuccessNeedsBothConditionsAndNeitherOneAlone(bool named, bool beforeTheInstant, bool expected)
    {
        var record = ARecord(invited: named ? new[] { Guest } : Array.Empty<Guid>());

        var result = ShareResolution.Resolve(
            new[] { record },
            Key,
            "a-token",
            Guest,
            PluginStatus.Active,
            At(beforeTheInstant ? WhileLive : AfterExpiry));

        Assert.Equal(expected, result.IsResolved);
    }

    /// <summary>
    /// Disabling an account is not withdrawing an invitation, so the record still
    /// names the guest and an account the server identifies again resolves the
    /// share again.
    /// </summary>
    /// <remarks>
    /// This is the non-inference the issue's rule is about, in the direction that
    /// would be easy to get wrong: a plugin that treated "cannot sign in" as
    /// "no longer invited" would edit the record on an event that is not its own,
    /// and an operator who re-enabled the account would find the share gone.
    /// </remarks>
    [Fact]
    public void AnAccountEnabledAgainResolvesTheShareAgain()
    {
        var record = ARecord();

        var whileDisabled = Resolve(record, caller: null, at: WhileLive);
        var afterItIsEnabledAgain = Resolve(record, caller: Guest, at: WhileLive);

        Assert.Equal(ShareRefusal.CallerNotSignedIn, whileDisabled.Refusal);
        Assert.True(afterItIsEnabledAgain.IsResolved);
        Assert.Equal(new[] { Guest }, record.InvitedUserIds);
    }

    /// <summary>
    /// An account made again after the invited one was deleted is a different
    /// account, and the invitation does not follow the name.
    /// </summary>
    /// <remarks>
    /// The record names an identifier. An operator who deletes a guest and makes a
    /// new account with the same name has made somebody else, and a share that
    /// resolved for them would be a share resolving for an account it never named.
    /// </remarks>
    [Fact]
    public void AnAccountMadeAgainUnderTheSameNameIsNotTheInvitedOne()
    {
        var record = ARecord();

        var result = Resolve(record, caller: TheAccountThatReplacedIt, at: WhileLive);

        Assert.Equal(ShareRefusal.CallerNotInvited, result.Refusal);
        Assert.False(result.IsResolved);
    }

    /// <summary>
    /// Withdrawing an invitation takes effect on the next request, with nothing
    /// swept and nothing else changed.
    /// </summary>
    /// <remarks>
    /// The same property revocation has in #46, asserted the same way: the two
    /// requests are one instant apart on a clock that did not move, so the only
    /// thing that changed is the record.
    /// </remarks>
    [Fact]
    public void WithdrawingAnInvitationTakesEffectOnTheNextRequestWithNoSweep()
    {
        var invited = ARecord();
        var withdrawn = ARecord(invited: Array.Empty<Guid>());
        var clock = At(WhileLive);

        var before = ShareResolution.Resolve(new[] { invited }, Key, "a-token", Guest, PluginStatus.Active, clock);
        var after = ShareResolution.Resolve(new[] { withdrawn }, Key, "a-token", Guest, PluginStatus.Active, clock);

        Assert.True(before.IsResolved);
        Assert.Equal(ShareRefusal.CallerNotInvited, after.Refusal);
    }

    /// <summary>
    /// Inviting an account to a share that has expired does not revive it.
    /// </summary>
    /// <remarks>
    /// The instant is on the record and nothing here moves it, which is
    /// <c>docs/expiry.md</c>'s rule that extending a link is issuing a link. A
    /// share that came back because somebody was invited again would extend every
    /// copy of the old link with it.
    /// </remarks>
    [Fact]
    public void InvitingSomebodyAgainDoesNotMoveTheInstant()
    {
        var expired = ARecord();

        var withTheInvitationAdded = ARecord(invited: new[] { Guest, TheAccountThatReplacedIt });

        Assert.Equal(ShareRefusal.Expired, Resolve(expired, Guest, AfterExpiry).Refusal);
        Assert.Equal(ShareRefusal.Expired, Resolve(withTheInvitationAdded, Guest, AfterExpiry).Refusal);
        Assert.Equal(ShareRefusal.Expired, Resolve(withTheInvitationAdded, TheAccountThatReplacedIt, AfterExpiry).Refusal);
    }

    /// <summary>
    /// Every row in the table is one this method builds, and every combination the
    /// issue names is a row.
    /// </summary>
    /// <remarks>
    /// A row nobody built would throw, so the first half holds itself. This is the
    /// second half: the five combinations the issue lists are the five rows, so a
    /// row quietly dropped from the table is a red test rather than a table that
    /// looks complete.
    /// </remarks>
    [Fact]
    public void TheTableHoldsEveryCombinationTheIssueNames()
    {
        var rows = TheCombinations
            .Select(row => (string)row[0])
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            [
                "the guest account is deleted",
                "the guest account is disabled by the operator",
                "the guest is invited again after the share expired",
                "the invitation is withdrawn while the share is live",
                "the share expires while the guest is still invited",
            ],
            rows);
    }

    private static (ShareResolutionResult Before, ShareResolutionResult After) Play(string combination) => combination switch
    {
        "the invitation is withdrawn while the share is live"
            => (Resolve(ARecord(), Guest, WhileLive), Resolve(ARecord(invited: Array.Empty<Guid>()), Guest, WhileLive)),

        "the share expires while the guest is still invited"
            => (Resolve(ARecord(), Guest, WhileLive), Resolve(ARecord(), Guest, AfterExpiry)),

        // Disabled and deleted reach the decision the same way, and the way is the
        // server identifying nobody. The record is untouched in both, which is what
        // the two assertions beside this table are about.
        "the guest account is disabled by the operator"
            => (Resolve(ARecord(), Guest, WhileLive), Resolve(ARecord(), null, WhileLive)),

        "the guest account is deleted"
            => (Resolve(ARecord(), Guest, WhileLive), Resolve(ARecord(), null, WhileLive)),

        "the guest is invited again after the share expired"
            => (Resolve(ARecord(), Guest, AfterExpiry), Resolve(ARecord(invited: new[] { Guest }), Guest, AfterExpiry)),

        _ => throw new ArgumentOutOfRangeException(nameof(combination), combination, "The table names a combination this method does not build."),
    };

    private static ShareResolutionResult Resolve(ShareRecord record, Guid? caller, DateTimeOffset at) =>
        ShareResolution.Resolve(new[] { record }, Key, "a-token", caller, PluginStatus.Active, At(at));

    private static ShareRecord ARecord(IReadOnlyList<Guid>? invited = null) => new ShareRecord
    {
        SchemaVersion = ShareRecord.CurrentSchemaVersion,
        Id = Guid.NewGuid(),
        ItemId = Guid.NewGuid(),
        InvitedUserIds = invited ?? new[] { Guest },
        CreatedByUserId = Guid.NewGuid(),
        CreatedAt = Expiry.AddDays(-7),
        ExpiresAt = Expiry,
        TokenHash = ShareTokenHash.Compute(Key, "a-token"),
    };

    private static TimeProvider At(DateTimeOffset instant) => new FixedClock(instant);

    /// <summary>
    /// A clock that stands still wherever it is put.
    /// </summary>
    /// <remarks>
    /// The seam is the framework's own <see cref="TimeProvider"/>, so nothing here
    /// invents a clock interface.
    /// </remarks>
    private sealed class FixedClock : TimeProvider
    {
        private readonly DateTimeOffset _instant;

        public FixedClock(DateTimeOffset instant) => _instant = instant;

        public override DateTimeOffset GetUtcNow() => _instant;
    }
}
