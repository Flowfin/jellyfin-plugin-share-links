using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using MediaBrowser.Model.Plugins;
using Xunit;

namespace Jellyfin.Plugin.ShareLinks.Tests;

/// <summary>
/// Every combination of the five inputs the resolution decision has, and the one
/// answer it gives to each (#77).
/// </summary>
/// <remarks>
/// <para>
/// The decision has a handful of inputs and one answer, which is the shape a
/// table covers completely rather than anecdotally. The five axes are the ones
/// the issue names: the plugin's state, the token's state, the share's state, the
/// caller's state and the invitation's state. The product of them is the table
/// below, written out row by row rather than derived, so a row is something a
/// reader checks against the document instead of something a helper computed from
/// the same source it is meant to be judging.
/// </para>
/// <para>
/// This is a separate file from <c>ShareResolutionTests</c> on purpose. That one
/// holds the situations #48 was written against and the arguments about the order
/// the conditions are taken in. This one holds the product, and it answers a
/// different question: not whether each situation is handled, but whether any
/// combination of them is unhandled.
/// </para>
/// <para>
/// One axis is inert over part of the table and those rows are kept rather than
/// dropped. Where the server has identified nobody, whether the record names the
/// caller is not a state the plugin can be in, because there is no caller for a
/// record to name. The rows are here with the answer they give, and
/// <see cref="TheInvitationAxisIsInertWhereNobodyIsSignedIn"/> asserts the pair
/// really is one situation rather than two that happen to agree.
/// </para>
/// <para>
/// Two of the decision's inputs are not axes here, and both are named so the
/// omission is a decision rather than a gap. An unreadable key is a state of the
/// install rather than one of the five, and it is asserted on the overload that
/// reads the key file. The clock is not an axis either: it reaches the decision
/// through the share's state, because what the decision asks a clock is whether
/// this share has reached its instant.
/// </para>
/// </remarks>
public class DecisionTableTests
{
    private static readonly DateTimeOffset Expiry = new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid Invited = Guid.Parse("66666666-6666-6666-6666-666666666666");
    private static readonly Guid Stranger = Guid.Parse("77777777-7777-7777-7777-777777777777");
    private static readonly byte[] Key = Enumerable.Range(0, ShareTokenHash.MinimumKeyBytes).Select(value => (byte)value).ToArray();

    private static readonly string[] PluginStates = ["Active", "NotActive"];
    private static readonly string[] TokenStates = ["Absent", "Empty", "Unmatched", "Matched"];
    private static readonly string[] ShareStates = ["Live", "Revoked", "Expired", "RevokedAndExpired"];
    private static readonly string[] CallerStates = ["SignedIn", "NotSignedIn"];
    private static readonly string[] InvitationStates = ["Named", "NotNamed"];

    /// <summary>
    /// Gets the whole table: one row per combination, in the order plugin, token,
    /// share, caller, invitation.
    /// </summary>
    public static TheoryData<string, ShareRefusal> TheTable => new TheoryData<string, ShareRefusal>
    {
        { "Active Absent Live SignedIn Named", ShareRefusal.NoTokenPresented },
        { "Active Absent Live SignedIn NotNamed", ShareRefusal.NoTokenPresented },
        { "Active Absent Live NotSignedIn Named", ShareRefusal.NoTokenPresented },
        { "Active Absent Live NotSignedIn NotNamed", ShareRefusal.NoTokenPresented },
        { "Active Absent Revoked SignedIn Named", ShareRefusal.NoTokenPresented },
        { "Active Absent Revoked SignedIn NotNamed", ShareRefusal.NoTokenPresented },
        { "Active Absent Revoked NotSignedIn Named", ShareRefusal.NoTokenPresented },
        { "Active Absent Revoked NotSignedIn NotNamed", ShareRefusal.NoTokenPresented },
        { "Active Absent Expired SignedIn Named", ShareRefusal.NoTokenPresented },
        { "Active Absent Expired SignedIn NotNamed", ShareRefusal.NoTokenPresented },
        { "Active Absent Expired NotSignedIn Named", ShareRefusal.NoTokenPresented },
        { "Active Absent Expired NotSignedIn NotNamed", ShareRefusal.NoTokenPresented },
        { "Active Absent RevokedAndExpired SignedIn Named", ShareRefusal.NoTokenPresented },
        { "Active Absent RevokedAndExpired SignedIn NotNamed", ShareRefusal.NoTokenPresented },
        { "Active Absent RevokedAndExpired NotSignedIn Named", ShareRefusal.NoTokenPresented },
        { "Active Absent RevokedAndExpired NotSignedIn NotNamed", ShareRefusal.NoTokenPresented },
        { "Active Empty Live SignedIn Named", ShareRefusal.NoTokenPresented },
        { "Active Empty Live SignedIn NotNamed", ShareRefusal.NoTokenPresented },
        { "Active Empty Live NotSignedIn Named", ShareRefusal.NoTokenPresented },
        { "Active Empty Live NotSignedIn NotNamed", ShareRefusal.NoTokenPresented },
        { "Active Empty Revoked SignedIn Named", ShareRefusal.NoTokenPresented },
        { "Active Empty Revoked SignedIn NotNamed", ShareRefusal.NoTokenPresented },
        { "Active Empty Revoked NotSignedIn Named", ShareRefusal.NoTokenPresented },
        { "Active Empty Revoked NotSignedIn NotNamed", ShareRefusal.NoTokenPresented },
        { "Active Empty Expired SignedIn Named", ShareRefusal.NoTokenPresented },
        { "Active Empty Expired SignedIn NotNamed", ShareRefusal.NoTokenPresented },
        { "Active Empty Expired NotSignedIn Named", ShareRefusal.NoTokenPresented },
        { "Active Empty Expired NotSignedIn NotNamed", ShareRefusal.NoTokenPresented },
        { "Active Empty RevokedAndExpired SignedIn Named", ShareRefusal.NoTokenPresented },
        { "Active Empty RevokedAndExpired SignedIn NotNamed", ShareRefusal.NoTokenPresented },
        { "Active Empty RevokedAndExpired NotSignedIn Named", ShareRefusal.NoTokenPresented },
        { "Active Empty RevokedAndExpired NotSignedIn NotNamed", ShareRefusal.NoTokenPresented },
        { "Active Unmatched Live SignedIn Named", ShareRefusal.NoSuchShare },
        { "Active Unmatched Live SignedIn NotNamed", ShareRefusal.NoSuchShare },
        { "Active Unmatched Live NotSignedIn Named", ShareRefusal.NoSuchShare },
        { "Active Unmatched Live NotSignedIn NotNamed", ShareRefusal.NoSuchShare },
        { "Active Unmatched Revoked SignedIn Named", ShareRefusal.NoSuchShare },
        { "Active Unmatched Revoked SignedIn NotNamed", ShareRefusal.NoSuchShare },
        { "Active Unmatched Revoked NotSignedIn Named", ShareRefusal.NoSuchShare },
        { "Active Unmatched Revoked NotSignedIn NotNamed", ShareRefusal.NoSuchShare },
        { "Active Unmatched Expired SignedIn Named", ShareRefusal.NoSuchShare },
        { "Active Unmatched Expired SignedIn NotNamed", ShareRefusal.NoSuchShare },
        { "Active Unmatched Expired NotSignedIn Named", ShareRefusal.NoSuchShare },
        { "Active Unmatched Expired NotSignedIn NotNamed", ShareRefusal.NoSuchShare },
        { "Active Unmatched RevokedAndExpired SignedIn Named", ShareRefusal.NoSuchShare },
        { "Active Unmatched RevokedAndExpired SignedIn NotNamed", ShareRefusal.NoSuchShare },
        { "Active Unmatched RevokedAndExpired NotSignedIn Named", ShareRefusal.NoSuchShare },
        { "Active Unmatched RevokedAndExpired NotSignedIn NotNamed", ShareRefusal.NoSuchShare },
        { "Active Matched Live SignedIn Named", ShareRefusal.None },
        { "Active Matched Live SignedIn NotNamed", ShareRefusal.CallerNotInvited },
        { "Active Matched Live NotSignedIn Named", ShareRefusal.CallerNotSignedIn },
        { "Active Matched Live NotSignedIn NotNamed", ShareRefusal.CallerNotSignedIn },
        { "Active Matched Revoked SignedIn Named", ShareRefusal.Revoked },
        { "Active Matched Revoked SignedIn NotNamed", ShareRefusal.Revoked },
        { "Active Matched Revoked NotSignedIn Named", ShareRefusal.Revoked },
        { "Active Matched Revoked NotSignedIn NotNamed", ShareRefusal.Revoked },
        { "Active Matched Expired SignedIn Named", ShareRefusal.Expired },
        { "Active Matched Expired SignedIn NotNamed", ShareRefusal.Expired },
        { "Active Matched Expired NotSignedIn Named", ShareRefusal.Expired },
        { "Active Matched Expired NotSignedIn NotNamed", ShareRefusal.Expired },
        { "Active Matched RevokedAndExpired SignedIn Named", ShareRefusal.Revoked },
        { "Active Matched RevokedAndExpired SignedIn NotNamed", ShareRefusal.Revoked },
        { "Active Matched RevokedAndExpired NotSignedIn Named", ShareRefusal.Revoked },
        { "Active Matched RevokedAndExpired NotSignedIn NotNamed", ShareRefusal.Revoked },
        { "NotActive Absent Live SignedIn Named", ShareRefusal.PluginNotActive },
        { "NotActive Absent Live SignedIn NotNamed", ShareRefusal.PluginNotActive },
        { "NotActive Absent Live NotSignedIn Named", ShareRefusal.PluginNotActive },
        { "NotActive Absent Live NotSignedIn NotNamed", ShareRefusal.PluginNotActive },
        { "NotActive Absent Revoked SignedIn Named", ShareRefusal.PluginNotActive },
        { "NotActive Absent Revoked SignedIn NotNamed", ShareRefusal.PluginNotActive },
        { "NotActive Absent Revoked NotSignedIn Named", ShareRefusal.PluginNotActive },
        { "NotActive Absent Revoked NotSignedIn NotNamed", ShareRefusal.PluginNotActive },
        { "NotActive Absent Expired SignedIn Named", ShareRefusal.PluginNotActive },
        { "NotActive Absent Expired SignedIn NotNamed", ShareRefusal.PluginNotActive },
        { "NotActive Absent Expired NotSignedIn Named", ShareRefusal.PluginNotActive },
        { "NotActive Absent Expired NotSignedIn NotNamed", ShareRefusal.PluginNotActive },
        { "NotActive Absent RevokedAndExpired SignedIn Named", ShareRefusal.PluginNotActive },
        { "NotActive Absent RevokedAndExpired SignedIn NotNamed", ShareRefusal.PluginNotActive },
        { "NotActive Absent RevokedAndExpired NotSignedIn Named", ShareRefusal.PluginNotActive },
        { "NotActive Absent RevokedAndExpired NotSignedIn NotNamed", ShareRefusal.PluginNotActive },
        { "NotActive Empty Live SignedIn Named", ShareRefusal.PluginNotActive },
        { "NotActive Empty Live SignedIn NotNamed", ShareRefusal.PluginNotActive },
        { "NotActive Empty Live NotSignedIn Named", ShareRefusal.PluginNotActive },
        { "NotActive Empty Live NotSignedIn NotNamed", ShareRefusal.PluginNotActive },
        { "NotActive Empty Revoked SignedIn Named", ShareRefusal.PluginNotActive },
        { "NotActive Empty Revoked SignedIn NotNamed", ShareRefusal.PluginNotActive },
        { "NotActive Empty Revoked NotSignedIn Named", ShareRefusal.PluginNotActive },
        { "NotActive Empty Revoked NotSignedIn NotNamed", ShareRefusal.PluginNotActive },
        { "NotActive Empty Expired SignedIn Named", ShareRefusal.PluginNotActive },
        { "NotActive Empty Expired SignedIn NotNamed", ShareRefusal.PluginNotActive },
        { "NotActive Empty Expired NotSignedIn Named", ShareRefusal.PluginNotActive },
        { "NotActive Empty Expired NotSignedIn NotNamed", ShareRefusal.PluginNotActive },
        { "NotActive Empty RevokedAndExpired SignedIn Named", ShareRefusal.PluginNotActive },
        { "NotActive Empty RevokedAndExpired SignedIn NotNamed", ShareRefusal.PluginNotActive },
        { "NotActive Empty RevokedAndExpired NotSignedIn Named", ShareRefusal.PluginNotActive },
        { "NotActive Empty RevokedAndExpired NotSignedIn NotNamed", ShareRefusal.PluginNotActive },
        { "NotActive Unmatched Live SignedIn Named", ShareRefusal.PluginNotActive },
        { "NotActive Unmatched Live SignedIn NotNamed", ShareRefusal.PluginNotActive },
        { "NotActive Unmatched Live NotSignedIn Named", ShareRefusal.PluginNotActive },
        { "NotActive Unmatched Live NotSignedIn NotNamed", ShareRefusal.PluginNotActive },
        { "NotActive Unmatched Revoked SignedIn Named", ShareRefusal.PluginNotActive },
        { "NotActive Unmatched Revoked SignedIn NotNamed", ShareRefusal.PluginNotActive },
        { "NotActive Unmatched Revoked NotSignedIn Named", ShareRefusal.PluginNotActive },
        { "NotActive Unmatched Revoked NotSignedIn NotNamed", ShareRefusal.PluginNotActive },
        { "NotActive Unmatched Expired SignedIn Named", ShareRefusal.PluginNotActive },
        { "NotActive Unmatched Expired SignedIn NotNamed", ShareRefusal.PluginNotActive },
        { "NotActive Unmatched Expired NotSignedIn Named", ShareRefusal.PluginNotActive },
        { "NotActive Unmatched Expired NotSignedIn NotNamed", ShareRefusal.PluginNotActive },
        { "NotActive Unmatched RevokedAndExpired SignedIn Named", ShareRefusal.PluginNotActive },
        { "NotActive Unmatched RevokedAndExpired SignedIn NotNamed", ShareRefusal.PluginNotActive },
        { "NotActive Unmatched RevokedAndExpired NotSignedIn Named", ShareRefusal.PluginNotActive },
        { "NotActive Unmatched RevokedAndExpired NotSignedIn NotNamed", ShareRefusal.PluginNotActive },
        { "NotActive Matched Live SignedIn Named", ShareRefusal.PluginNotActive },
        { "NotActive Matched Live SignedIn NotNamed", ShareRefusal.PluginNotActive },
        { "NotActive Matched Live NotSignedIn Named", ShareRefusal.PluginNotActive },
        { "NotActive Matched Live NotSignedIn NotNamed", ShareRefusal.PluginNotActive },
        { "NotActive Matched Revoked SignedIn Named", ShareRefusal.PluginNotActive },
        { "NotActive Matched Revoked SignedIn NotNamed", ShareRefusal.PluginNotActive },
        { "NotActive Matched Revoked NotSignedIn Named", ShareRefusal.PluginNotActive },
        { "NotActive Matched Revoked NotSignedIn NotNamed", ShareRefusal.PluginNotActive },
        { "NotActive Matched Expired SignedIn Named", ShareRefusal.PluginNotActive },
        { "NotActive Matched Expired SignedIn NotNamed", ShareRefusal.PluginNotActive },
        { "NotActive Matched Expired NotSignedIn Named", ShareRefusal.PluginNotActive },
        { "NotActive Matched Expired NotSignedIn NotNamed", ShareRefusal.PluginNotActive },
        { "NotActive Matched RevokedAndExpired SignedIn Named", ShareRefusal.PluginNotActive },
        { "NotActive Matched RevokedAndExpired SignedIn NotNamed", ShareRefusal.PluginNotActive },
        { "NotActive Matched RevokedAndExpired NotSignedIn Named", ShareRefusal.PluginNotActive },
        { "NotActive Matched RevokedAndExpired NotSignedIn NotNamed", ShareRefusal.PluginNotActive },
    };

    /// <summary>
    /// Every row answers the way the table says, and a row that refuses hands back
    /// nothing beyond the refusal itself.
    /// </summary>
    /// <param name="situation">The five axis values, separated by spaces.</param>
    /// <param name="expected">The answer the table records for them.</param>
    [Theory]
    [MemberData(nameof(TheTable))]
    public void EveryCombinationAnswersTheWayTheTableSays(string situation, ShareRefusal expected)
    {
        var result = Decide(situation);

        Assert.Equal(expected, result.Refusal);
        Assert.Equal(expected == ShareRefusal.None, result.IsResolved);

        if (expected != ShareRefusal.None)
        {
            // The whole of what a refusal carries, asserted on every row. Two
            // refusals for two different reasons are the same object to anybody
            // who cannot read the reason, and ShareRefusal says the reason is the
            // server's rather than the caller's.
            Assert.Null(result.Share);
            Assert.False(result.IsResolved);
        }
    }

    /// <summary>
    /// The table is the product of the five axes and not a selection from it.
    /// </summary>
    [Fact]
    public void TheTableIsTheWholeProductOfTheFiveAxes()
    {
        var situations = TheTable.Select(row => (string)row[0]!).ToList();

        Assert.Equal(situations.Count, situations.Distinct(StringComparer.Ordinal).Count());

        var expected =
            from plugin in PluginStates
            from token in TokenStates
            from share in ShareStates
            from caller in CallerStates
            from invitation in InvitationStates
            select string.Join(' ', plugin, token, share, caller, invitation);

        Assert.Equal(
            expected.OrderBy(situation => situation, StringComparer.Ordinal).ToList(),
            situations.OrderBy(situation => situation, StringComparer.Ordinal).ToList());
    }

    /// <summary>
    /// Where the server has identified nobody, the invitation axis is not a state
    /// the plugin can be in, so the two rows are one situation.
    /// </summary>
    /// <remarks>
    /// This is what keeps those rows honest. Without it the table would carry
    /// pairs of rows agreeing because the fixture builds the same call twice, and
    /// a reader would have no way to tell that from two situations that happen to
    /// have the same answer.
    /// </remarks>
    [Fact]
    public void TheInvitationAxisIsInertWhereNobodyIsSignedIn()
    {
        var groups = TheTable
            .Select(row => (string)row[0]!)
            .Where(situation => situation.Contains("NotSignedIn", StringComparison.Ordinal))
            .GroupBy(
                situation => situation.Replace(" Named", " *", StringComparison.Ordinal).Replace(" NotNamed", " *", StringComparison.Ordinal),
                StringComparer.Ordinal)
            .ToList();

        Assert.NotEmpty(groups);

        foreach (var group in groups)
        {
            var answers = group.Select(situation => Decide(situation).Refusal).Distinct().ToList();

            Assert.True(
                answers.Count == 1,
                string.Create(CultureInfo.InvariantCulture, $"{group.Key} answers {answers.Count} ways, so the invitation axis is not inert where the table writes it as inert"));
        }
    }

    /// <summary>
    /// A refusal has one shape, so a reason cannot reach a caller through a member
    /// somebody adds later.
    /// </summary>
    /// <remarks>
    /// The per-row assertion checks that the shape was used. This checks that
    /// there is only one shape to use: the share, whether there is one, and the
    /// reason the server reads. A fourth member carrying anything about why reds
    /// here rather than being caught in review.
    /// </remarks>
    [Fact]
    public void ARefusalHasOneShapeWhateverTheReason()
    {
        var members = typeof(ShareResolutionResult)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(property => property.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        Assert.Equal(new[] { "IsResolved", "Refusal", "Share" }, members);
    }

    private static ShareResolutionResult Decide(string situation)
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

        var records = new[] { ARecord(revoked ? Expiry.AddDays(-2) : null) };
        var clock = At(expired ? Expiry.AddHours(1) : Expiry.AddHours(-1));

        return ShareResolution.Resolve(records, Key, token, caller, status, clock);
    }

    private static ShareRecord ARecord(DateTimeOffset? revokedAt) => new ShareRecord
    {
        SchemaVersion = ShareRecord.CurrentSchemaVersion,
        Id = Guid.NewGuid(),
        ItemId = Guid.NewGuid(),
        InvitedUserIds = new[] { Invited },
        CreatedByUserId = Guid.NewGuid(),
        CreatedAt = Expiry.AddDays(-7),
        ExpiresAt = Expiry,
        RevokedAt = revokedAt,
        TokenHash = ShareTokenHash.Compute(Key, "a-token"),
    };

    private static TimeProvider At(DateTimeOffset instant) => new FixedClock(instant);

    private sealed class FixedClock : TimeProvider
    {
        private readonly DateTimeOffset _instant;

        public FixedClock(DateTimeOffset instant) => _instant = instant;

        public override DateTimeOffset GetUtcNow() => _instant;
    }
}
