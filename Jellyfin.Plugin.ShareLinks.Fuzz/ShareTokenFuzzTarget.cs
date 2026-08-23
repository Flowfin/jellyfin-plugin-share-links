using System;
using System.Collections.Generic;
using System.Text;
using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.ShareLinks.Fuzz;

/// <summary>
/// The routine libFuzzer drives, and the property it holds (#19).
/// </summary>
/// <remarks>
/// <para>
/// The target is <see cref="ShareResolution.Resolve"/>, which is where the guest
/// route hands the path segment a caller wrote. There is no decode step in front
/// of it: the token is checked for null or empty and its UTF-8 bytes go into a
/// keyed hash, and a length or alphabet check ahead of that comparison is what
/// #26 rules out, because it would make a wrong-shaped token refuse more cheaply
/// than a right-shaped one that names nothing. So this holds a property rather
/// than watching a parser.
/// </para>
/// <para>
/// The property, in three parts. No input throws. No input resolves. And every
/// non-empty input is refused with the same reason as every other non-empty
/// input, which is the half that would notice a shape check appearing in front of
/// the comparison.
/// </para>
/// <para>
/// What it cannot hold: that two refusals cost the same. Time is not something a
/// libFuzzer oracle can read, and <c>ShareLookupCostTests</c> is where the
/// comparison's shape is judged instead.
/// </para>
/// </remarks>
public static class ShareTokenFuzzTarget
{
    /// <summary>
    /// The instant every record below is judged against.
    /// </summary>
    /// <remarks>
    /// Fixed, and not the machine's. A harness whose fixture ages is one whose
    /// reproducers stop reproducing.
    /// </remarks>
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static readonly TimeProvider Clock = new FixedClock(Now);

    /// <summary>
    /// The install's key. Minted once per process through the one routine that
    /// owns token material, so nothing here draws its own bytes.
    /// </summary>
    private static readonly byte[] Key = ShareTokens.MintKeyBytes();

    /// <summary>
    /// The account the server identified the caller as.
    /// </summary>
    /// <remarks>
    /// Public because a test that shows the property below biting has to build a
    /// record this caller is invited to, and inventing its own identifier would
    /// make the test pass for a reason the harness does not share.
    /// </remarks>
    public static readonly Guid Caller = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static readonly Guid Item = Guid.Parse("22222222-2222-2222-2222-222222222222");

    /// <summary>
    /// The store as the route read it: one live share, one revoked, one expired.
    /// </summary>
    /// <remarks>
    /// Three rather than one, because <see cref="ShareLookup.ByToken"/> walks every
    /// record without stopping early and a single-record store would not exercise
    /// the walk. None of their tokens is derivable from anything committed here,
    /// so no input the fuzzer can construct resolves one.
    /// </remarks>
    private static readonly IReadOnlyList<ShareRecord> Records = BuildRecords();

    /// <summary>
    /// Runs the target over one input, against the fixture this harness holds.
    /// </summary>
    /// <param name="data">The bytes libFuzzer produced.</param>
    public static void Run(ReadOnlySpan<byte> data) => Check(Records, Key, data);

    /// <summary>
    /// Runs the target over one input, against a store and a key a caller supplies.
    /// </summary>
    /// <remarks>
    /// Public so the property below can be shown to bite. A test hands it a store
    /// holding a record the input DOES name, which is the one thing the fixture
    /// above is built so that no input can reach, and the exception it gets back is
    /// the proof that the oracle is doing something. Without that seam the only
    /// evidence a run gives is that nothing was found, which is what a target
    /// pointed at nothing also reports.
    /// </remarks>
    /// <param name="records">The store, as the route read it.</param>
    /// <param name="key">The install's key.</param>
    /// <param name="data">The bytes to present as a token.</param>
    public static void Check(IReadOnlyList<ShareRecord> records, ReadOnlySpan<byte> key, ReadOnlySpan<byte> data)
    {
        // The same decode the route's path segment goes through. A byte run that is
        // not valid UTF-8 arrives here as replacement characters rather than as an
        // exception, which is the behaviour being fuzzed rather than a shortcut.
        var presented = Encoding.UTF8.GetString(data);

        var verdict = ShareResolution.Resolve(
            records,
            key,
            presented,
            Caller,
            PluginStatus.Active,
            Clock,
            _ => true);

        if (verdict.IsResolved)
        {
            throw new FuzzedOracleBrokenException(
                "an input resolved a share. No committed byte derives a token in the fixture, so under the fuzz run this is either a lookup that stopped comparing hashes or a preimage.");
        }

        var expected = presented.Length == 0 ? ShareRefusal.NoTokenPresented : ShareRefusal.NoSuchShare;
        if (verdict.Refusal != expected)
        {
            throw new FuzzedOracleBrokenException(
                FormattableString.Invariant(
                    $"an input of {data.Length} byte(s) was refused as {verdict.Refusal} rather than {expected}. A token that names no share has to be refused the same way whatever shape it has (#26), so a second reason here is a way of telling one wrong token from another."));
        }
    }

    private static IReadOnlyList<ShareRecord> BuildRecords()
    {
        var key = Key;
        return new[]
        {
            Record(Guid.Parse("33333333-3333-3333-3333-333333333333"), key, Now.AddDays(1), revokedAt: null),
            Record(Guid.Parse("44444444-4444-4444-4444-444444444444"), key, Now.AddDays(1), revokedAt: Now.AddHours(-1)),
            Record(Guid.Parse("55555555-5555-5555-5555-555555555555"), key, Now.AddHours(-1), revokedAt: null),
        };
    }

    private static ShareRecord Record(Guid id, byte[] key, DateTimeOffset expiresAt, DateTimeOffset? revokedAt) =>
        new()
        {
            SchemaVersion = ShareRecord.CurrentSchemaVersion,
            Id = id,
            ItemId = Item,
            InvitedUserIds = new[] { Caller },
            CreatedByUserId = Caller,
            CreatedAt = Now.AddDays(-1),
            ExpiresAt = expiresAt,
            RevokedAt = revokedAt,
            TokenHash = ShareTokenHash.Compute(key, ShareTokens.Mint()),
        };

    /// <summary>
    /// A clock that does not move and does not ask the machine.
    /// </summary>
    private sealed class FixedClock : TimeProvider
    {
        private readonly DateTimeOffset _instant;

        public FixedClock(DateTimeOffset instant) => _instant = instant;

        public override DateTimeOffset GetUtcNow() => _instant;
    }
}
