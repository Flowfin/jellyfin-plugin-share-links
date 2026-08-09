using System;
using System.Collections.Generic;
using System.Linq;
using MediaBrowser.Model.Plugins;
using Xunit;

namespace Jellyfin.Plugin.ShareLinks.Tests;

/// <summary>
/// The boundary cases #79 asks for, driven by the injected clock rather than by
/// the machine's.
/// </summary>
/// <remarks>
/// <para>
/// Everything expiry does is a comparison against an instant, so the values worth
/// asserting are all within one tick of one. Reaching them by waiting would make
/// the suite slow where it is cheap and flaky where it is exact, and a boundary
/// nobody can stand on is a boundary nobody tests. The seam is
/// <see cref="TimeProvider"/> (#36), so standing either side of an instant costs
/// a subtraction.
/// </para>
/// <para>
/// One clock is used per test and it is stepped rather than replaced. That is the
/// difference between this file and the tables in <c>ShareResolutionTests</c> and
/// <c>ShareRevocationTests</c>, which stand a separate fixed clock at each
/// situation. A stepped clock is what the last two cases here need at all: a
/// sweep that runs between two reads, and a clock that moves backwards, are both
/// statements about one clock changing rather than about two clocks disagreeing.
/// </para>
/// <para>
/// What the sweep is, in this file. It is <see cref="ShareBounds.Retained"/>,
/// which is the routine <c>ShareStoreExtensions.AddAsync</c> runs on the way to
/// every write. It is called here directly rather than through a store, because
/// what is being asserted is which records survive an instant and not what a file
/// does, and putting a file in the middle would make a clock test fail for
/// reasons about disks.
/// </para>
/// </remarks>
public class ClockBoundaryTests
{
    private static readonly DateTimeOffset Expiry = new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid Invited = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly byte[] Key = Enumerable.Range(0, ShareTokenHash.MinimumKeyBytes).Select(value => (byte)value).ToArray();

    /// <summary>
    /// One clock walked from before the instant to after it changes the answer
    /// once, at the instant and not beside it.
    /// </summary>
    /// <remarks>
    /// Half-open, which is <c>docs/expiry.md</c>'s decision: live strictly before,
    /// refused at it and after it. The three positions are asserted off one clock
    /// so that a routine reading a second clock of its own would have to agree
    /// with this one to pass.
    /// </remarks>
    [Fact]
    public void AClockWalkedAcrossTheInstantChangesTheAnswerAtTheInstantAndNotBesideIt()
    {
        var records = new[] { ARecord() };
        var clock = new SteppableClock(Expiry.AddTicks(-1));

        var justBefore = Resolve(records, clock);

        clock.Step(TimeSpan.FromTicks(1));
        var atTheInstant = Resolve(records, clock);

        clock.Step(TimeSpan.FromTicks(1));
        var justAfter = Resolve(records, clock);

        Assert.True(justBefore.IsResolved);
        Assert.Equal(ShareRefusal.Expired, atTheInstant.Refusal);
        Assert.Equal(ShareRefusal.Expired, justAfter.Refusal);
    }

    /// <summary>
    /// A sweep landing between the read and the decision cannot turn a refusal
    /// into a resolution.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A request reads the records and then decides, and a sweep is free to run in
    /// between, because the sweep is a write and the read is not held against it.
    /// What that race may cost is the refusal reason: the request holding the
    /// older list refuses the share as expired, and a request reading after the
    /// sweep refuses it because no record answers for the token. Both refuse, and
    /// that is the property worth holding rather than the reason.
    /// </para>
    /// <para>
    /// The reason moving is not a defect here and is deliberately asserted rather
    /// than smoothed over, because it is what an operator reading two log lines
    /// about one token will see.
    /// </para>
    /// </remarks>
    [Fact]
    public void ASweepRunningWhileARequestIsInFlightRefusesEitherWay()
    {
        var bounds = new ShareBounds(100, 10, 30, 0);
        var clock = new SteppableClock(Expiry.AddTicks(-1));

        // What the request read, before the sweep ran.
        IReadOnlyList<ShareRecord> inFlight = new[] { ARecord() };

        clock.Step(TimeSpan.FromTicks(1));
        var afterTheSweep = bounds.Retained(inFlight, clock.GetUtcNow());

        var decidedOnWhatWasRead = Resolve(inFlight, clock);
        var decidedOnWhatSurvived = Resolve(afterTheSweep, clock);

        Assert.Empty(afterTheSweep);
        Assert.Equal(ShareRefusal.Expired, decidedOnWhatWasRead.Refusal);
        Assert.Equal(ShareRefusal.NoSuchShare, decidedOnWhatSurvived.Refusal);
    }

    /// <summary>
    /// A sweep never removes a record that still answers, so a request in flight
    /// cannot lose a live share to one.
    /// </summary>
    [Fact]
    public void ASweepLeavesALiveRecordWhereARequestCanStillFindIt()
    {
        var bounds = new ShareBounds(100, 10, 30, 0);
        var clock = new SteppableClock(Expiry.AddTicks(-1));
        IReadOnlyList<ShareRecord> records = new[] { ARecord() };

        var afterTheSweep = bounds.Retained(records, clock.GetUtcNow());

        Assert.Single(afterTheSweep);
        Assert.True(Resolve(afterTheSweep, clock).IsResolved);
    }

    /// <summary>
    /// A clock that steps backwards makes an expired share live again, which is
    /// what <c>docs/expiry.md</c> says it does.
    /// </summary>
    /// <remarks>
    /// This asserts the documented behaviour rather than the absence of it. The
    /// document weighs the alternative, a flag persisted the first time a share is
    /// seen expired, and refuses it: it costs a store write on a read path and it
    /// is still wrong for a share that expired while the server was down. So the
    /// residual is accepted, and a test asserting that a backwards step cannot
    /// revive a share would contradict the decision rather than guard it. What
    /// bounds the residual is the sweep, which the next test is about.
    /// </remarks>
    [Fact]
    public void AClockThatStepsBackwardsMakesAnExpiredShareLiveAgain()
    {
        var records = new[] { ARecord() };
        var clock = new SteppableClock(Expiry.AddHours(1));

        var whileExpired = Resolve(records, clock);

        clock.Step(TimeSpan.FromHours(-2));
        var afterTheStepBack = Resolve(records, clock);

        Assert.Equal(ShareRefusal.Expired, whileExpired.Refusal);
        Assert.True(afterTheStepBack.IsResolved);
    }

    /// <summary>
    /// Once the sweep has removed the record, a backwards step brings nothing
    /// back.
    /// </summary>
    /// <remarks>
    /// This is the bound on the residual above, and it is the reason the sweep
    /// interval is where that residual actually lives: a record that is gone is
    /// not compared against any clock.
    /// </remarks>
    [Fact]
    public void AClockThatStepsBackwardsDoesNotBringBackWhatTheSweepRemoved()
    {
        var bounds = new ShareBounds(100, 10, 30, 0);
        var clock = new SteppableClock(Expiry.AddHours(1));

        var afterTheSweep = bounds.Retained(new[] { ARecord() }, clock.GetUtcNow());

        clock.Step(TimeSpan.FromHours(-2));
        var afterTheStepBack = Resolve(afterTheSweep, clock);

        Assert.Empty(afterTheSweep);
        Assert.Equal(ShareRefusal.NoSuchShare, afterTheStepBack.Refusal);
    }

    /// <summary>
    /// A revoked share stays refused however the clock moves.
    /// </summary>
    /// <remarks>
    /// Revocation is a recorded state rather than an expiry set to now (#46), so
    /// nothing about it is a comparison against a clock. That is the property
    /// <c>docs/expiry.md</c> relies on when it says a backwards step affects
    /// expiry and not revocation, and it is asserted here rather than assumed.
    /// </remarks>
    [Fact]
    public void RevocationSurvivesAClockThatMovesInEitherDirection()
    {
        var records = new[] { ARecord(revokedAt: Expiry.AddDays(-1)) };
        var clock = new SteppableClock(Expiry.AddDays(-2));

        var beforeTheRevocationInstant = Resolve(records, clock);

        clock.Step(TimeSpan.FromDays(3));
        var wellAfterIt = Resolve(records, clock);

        clock.Step(TimeSpan.FromDays(-30));
        var longBeforeTheShareWasEvenMade = Resolve(records, clock);

        Assert.Equal(ShareRefusal.Revoked, beforeTheRevocationInstant.Refusal);
        Assert.Equal(ShareRefusal.Revoked, wellAfterIt.Refusal);
        Assert.Equal(ShareRefusal.Revoked, longBeforeTheShareWasEvenMade.Refusal);
    }

    private static ShareResolutionResult Resolve(IReadOnlyList<ShareRecord> records, TimeProvider clock) =>
        ShareResolution.Resolve(records, Key, "a-token", Invited, PluginStatus.Active, clock);

    private static ShareRecord ARecord(DateTimeOffset? revokedAt = null) => new ShareRecord
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

    /// <summary>
    /// A clock that stands where it is put and moves when it is told to, in
    /// either direction.
    /// </summary>
    /// <remarks>
    /// The seam is the framework's own <see cref="TimeProvider"/>, so nothing here
    /// invents a clock interface. Stepping is a method rather than a settable
    /// property so that a step reads as an event in the test, which is what the
    /// backwards cases are about.
    /// </remarks>
    private sealed class SteppableClock : TimeProvider
    {
        private DateTimeOffset _instant;

        public SteppableClock(DateTimeOffset instant) => _instant = instant;

        public override DateTimeOffset GetUtcNow() => _instant;

        public void Step(TimeSpan by) => _instant += by;
    }
}
