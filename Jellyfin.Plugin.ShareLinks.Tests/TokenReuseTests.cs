using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using MediaBrowser.Model.Plugins;
using Xunit;

namespace Jellyfin.Plugin.ShareLinks.Tests;

/// <summary>
/// The reuse rule from <c>docs/threat-model.md</c>, asserted (#25).
/// </summary>
/// <remarks>
/// <para>
/// The rule is that a token works as often as it is presented until the instant
/// it expires or until it is revoked, and that a presentation is bound to neither
/// a device nor a session. The ordinary case it exists for is a guest opening the
/// link on a phone and again on a television, so a rule that burned the token on
/// first use would break the feature rather than protect it.
/// </para>
/// <para>
/// Two of the three cases the issue names, the same token twice in one session
/// and twice in two sessions, are the same call here, and that is the rule rather
/// than a shortcut. Nothing about a session or a device is an input to the
/// decision, which <see cref="NoDeviceOrSessionReachesTheDecision"/> asserts off
/// the signature instead of asserting it twice off two fixtures that differ in
/// nothing.
/// </para>
/// <para>
/// What none of this watches is a guard, because reuse is allowed by the absence
/// of state rather than refused by a check. The mutation that reds these tests is
/// an added one: a routine that remembered which tokens it had seen. That is what
/// they are here to catch, and the pull request records the run.
/// </para>
/// </remarks>
public class TokenReuseTests
{
    private static readonly DateTimeOffset Expiry = new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid Phone = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid Television = Guid.Parse("55555555-5555-5555-5555-555555555555");
    private static readonly byte[] Key = Enumerable.Range(0, ShareTokenHash.MinimumKeyBytes).Select(value => (byte)value).ToArray();

    /// <summary>
    /// The same token presented twice in a row resolves twice.
    /// </summary>
    [Fact]
    public void TheSameTokenResolvesEveryTimeItIsPresented()
    {
        var records = new[] { ARecord() };
        var clock = At(Expiry.AddHours(-1));

        var first = ShareResolution.Resolve(records, Key, "a-token", Phone, PluginStatus.Active, clock);
        var second = ShareResolution.Resolve(records, Key, "a-token", Phone, PluginStatus.Active, clock);

        Assert.True(first.IsResolved);
        Assert.True(second.IsResolved);
        Assert.Same(first.Share, second.Share);
    }

    /// <summary>
    /// A pause between two presentations changes nothing while the share is live,
    /// which is the phone and the television with a night in between.
    /// </summary>
    [Fact]
    public void APauseBetweenTwoPresentationsChangesNothingWhileTheShareIsLive()
    {
        var records = new[] { ARecord() };

        var onThePhone = ShareResolution.Resolve(records, Key, "a-token", Phone, PluginStatus.Active, At(Expiry.AddDays(-2)));
        var onTheTelevision = ShareResolution.Resolve(records, Key, "a-token", Phone, PluginStatus.Active, At(Expiry.AddTicks(-1)));

        Assert.True(onThePhone.IsResolved);
        Assert.True(onTheTelevision.IsResolved);
    }

    /// <summary>
    /// Neither a device nor a session is an input to the decision, so a
    /// presentation cannot be bound to one.
    /// </summary>
    /// <remarks>
    /// This is the assertion that makes "twice in one session" and "twice in two
    /// sessions" one case rather than two fixtures that differ in nothing. It is
    /// read off the signature, because the rule is that the vocabulary is absent
    /// and not that some particular value of it is ignored.
    /// </remarks>
    [Fact]
    public void NoDeviceOrSessionReachesTheDecision()
    {
        var vocabulary = new[] { "session", "device", "address", "client", "agent", "fingerprint" };

        var parameters = typeof(ShareResolution)
            .GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Where(method => method.Name == nameof(ShareResolution.Resolve))
            .SelectMany(method => method.GetParameters())
            .Select(parameter => parameter.Name ?? string.Empty)
            .ToList();

        Assert.NotEmpty(parameters);

        foreach (var word in vocabulary)
        {
            Assert.DoesNotContain(parameters, name => name.Contains(word, StringComparison.OrdinalIgnoreCase));
        }
    }

    /// <summary>
    /// The record carries nothing a redemption could be written into, so reuse is
    /// allowed by the shape of the record and not only by the routine.
    /// </summary>
    /// <remarks>
    /// Every property on the record is <c>init</c>-only, so a resolution has
    /// nowhere to put a mark even if one were wanted. Burning a token on first use
    /// is therefore a change to the record as well as to the decision, which is
    /// the size it should be.
    /// </remarks>
    [Fact]
    public void TheRecordCarriesNothingAPresentationCouldBeWrittenInto()
    {
        var vocabulary = new[] { "used", "redeem", "seen", "presented", "session", "device" };

        var properties = typeof(ShareRecord).GetProperties(BindingFlags.Public | BindingFlags.Instance);

        Assert.NotEmpty(properties);

        foreach (var property in properties)
        {
            Assert.False(
                vocabulary.Any(word => property.Name.Contains(word, StringComparison.OrdinalIgnoreCase)),
                $"ShareRecord.{property.Name} reads as a mark left by a presentation, and docs/threat-model.md says a token is not burned by one");

            Assert.True(
                property.SetMethod is null || property.SetMethod.ReturnParameter.GetRequiredCustomModifiers().Any(modifier => modifier.Name == "IsExternalInit"),
                $"ShareRecord.{property.Name} can be assigned after construction, so a resolution could mark the record it read");
        }
    }

    /// <summary>
    /// The same token resolves for every account the share names, which is what a
    /// household means and what the invited set in decision 5 of #94 asks for.
    /// </summary>
    [Fact]
    public void TheSameTokenResolvesForEveryAccountTheShareNames()
    {
        var records = new[] { ARecord(invited: new[] { Phone, Television }) };
        var clock = At(Expiry.AddHours(-1));

        var one = ShareResolution.Resolve(records, Key, "a-token", Phone, PluginStatus.Active, clock);
        var other = ShareResolution.Resolve(records, Key, "a-token", Television, PluginStatus.Active, clock);

        Assert.True(one.IsResolved);
        Assert.True(other.IsResolved);
    }

    /// <summary>
    /// The same token arriving many times at once resolves every time, which is
    /// the two addresses at once the issue asks about.
    /// </summary>
    /// <remarks>
    /// Nothing here waits. The presentations are run together to assert that the
    /// answer does not depend on how many are in flight, which is what it would if
    /// a first presentation took something away from the ones behind it.
    /// </remarks>
    [Fact]
    public void ManyPresentationsAtOnceAllResolve()
    {
        var records = new[] { ARecord(invited: new[] { Phone, Television }) };
        var clock = At(Expiry.AddHours(-1));
        var refusals = new ConcurrentBag<ShareRefusal>();

        Parallel.For(0, 64, index =>
        {
            var caller = index % 2 == 0 ? Phone : Television;
            refusals.Add(ShareResolution.Resolve(records, Key, "a-token", caller, PluginStatus.Active, clock).Refusal);
        });

        Assert.Equal(64, refusals.Count);
        Assert.All(refusals, refusal => Assert.Equal(ShareRefusal.None, refusal));
    }

    /// <summary>
    /// A token that has worked stops working once the share is revoked.
    /// </summary>
    [Fact]
    public void ATokenThatHasWorkedStopsWorkingOnceTheShareIsRevoked()
    {
        var clock = At(Expiry.AddHours(-1));

        var before = ShareResolution.Resolve(new[] { ARecord() }, Key, "a-token", Phone, PluginStatus.Active, clock);
        var after = ShareResolution.Resolve(new[] { ARecord(revokedAt: Expiry.AddHours(-2)) }, Key, "a-token", Phone, PluginStatus.Active, clock);

        Assert.True(before.IsResolved);
        Assert.Equal(ShareRefusal.Revoked, after.Refusal);
        Assert.Null(after.Share);
    }

    /// <summary>
    /// Using a share does not extend it. However many times a token has worked, it
    /// stops at the instant the record names.
    /// </summary>
    [Fact]
    public void PresentingATokenDoesNotMoveTheInstantItStopsAt()
    {
        var records = new[] { ARecord() };

        for (var presentation = 0; presentation < 8; presentation++)
        {
            Assert.True(ShareResolution.Resolve(records, Key, "a-token", Phone, PluginStatus.Active, At(Expiry.AddMinutes(-presentation - 1))).IsResolved);
        }

        var atTheInstant = ShareResolution.Resolve(records, Key, "a-token", Phone, PluginStatus.Active, At(Expiry));

        Assert.Equal(ShareRefusal.Expired, atTheInstant.Refusal);
    }

    private static ShareRecord ARecord(DateTimeOffset? revokedAt = null, Guid[]? invited = null) => new ShareRecord
    {
        SchemaVersion = ShareRecord.CurrentSchemaVersion,
        Id = Guid.NewGuid(),
        ItemId = Guid.NewGuid(),
        InvitedUserIds = invited ?? new[] { Phone },
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
