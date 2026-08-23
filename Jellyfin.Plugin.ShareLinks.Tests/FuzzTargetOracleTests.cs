using System;
using System.Collections.Generic;
using System.Text;
using Jellyfin.Plugin.ShareLinks.Fuzz;
using Xunit;

namespace Jellyfin.Plugin.ShareLinks.Tests;

/// <summary>
/// The fuzz target's property bites, for the reason it names (#19).
/// </summary>
/// <remarks>
/// <para>
/// A scheduled job that finds nothing and a scheduled job whose oracle asserts
/// nothing report the same thing, and the second one goes on reporting it for
/// years. Every earlier reading on #19 refused to land a harness for exactly that
/// reason, so the harness is landed with the two inputs that break its property
/// standing beside it.
/// </para>
/// <para>
/// These are not the planted crash the issue's Done-when asks for. That one is
/// planted in <c>ShareResolution.Resolve</c> itself and found by libFuzzer in a
/// dispatched run, which is a different claim: that the JOB finds a defect on the
/// path. What these two hold is that the TARGET would say so if it met one.
/// </para>
/// </remarks>
public sealed class FuzzTargetOracleTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// An input that resolves a share breaks the target.
    /// </summary>
    /// <remarks>
    /// The fixture the harness holds is built so no input can reach this, which is
    /// what makes the clause worth asserting somewhere it can.
    /// </remarks>
    [Fact]
    public void AnInputThatNamesAShareBreaksTheTarget()
    {
        var key = ShareTokens.MintKeyBytes();
        var token = ShareTokens.Mint();

        var thrown = Assert.Throws<FuzzedOracleBrokenException>(
            () => ShareTokenFuzzTarget.Check(
                new[] { Live(key, token) },
                key,
                Encoding.UTF8.GetBytes(token)));

        Assert.Contains("resolved a share", thrown.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// An input refused for a second reason breaks the target.
    /// </summary>
    /// <remarks>
    /// This is the half that would notice a shape check appearing in front of the
    /// hash comparison, which is what #26 rules out. A revoked record is used to
    /// produce the second reason because it is the cheapest one to build; what the
    /// clause refuses is a second reason at all, whatever produced it.
    /// </remarks>
    [Fact]
    public void AnInputRefusedForASecondReasonBreaksTheTarget()
    {
        var key = ShareTokens.MintKeyBytes();
        var token = ShareTokens.Mint();

        var thrown = Assert.Throws<FuzzedOracleBrokenException>(
            () => ShareTokenFuzzTarget.Check(
                new[] { Revoked(key, token) },
                key,
                Encoding.UTF8.GetBytes(token)));

        Assert.Contains("rather than NoSuchShare", thrown.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// An input that names nothing passes, so the two above fail for what they
    /// name rather than for reaching the target at all.
    /// </summary>
    [Fact]
    public void AnInputThatNamesNothingLeavesTheTargetAlone()
    {
        var key = ShareTokens.MintKeyBytes();

        ShareTokenFuzzTarget.Check(
            new[] { Live(key, ShareTokens.Mint()) },
            key,
            Encoding.UTF8.GetBytes(ShareTokens.Mint()));
    }

    private static ShareRecord Live(byte[] key, string token) => Build(key, token, revokedAt: null);

    private static ShareRecord Revoked(byte[] key, string token) => Build(key, token, revokedAt: Now.AddHours(-1));

    private static ShareRecord Build(byte[] key, string token, DateTimeOffset? revokedAt) =>
        new()
        {
            SchemaVersion = ShareRecord.CurrentSchemaVersion,
            Id = Guid.Parse("66666666-6666-6666-6666-666666666666"),
            ItemId = Guid.Parse("22222222-2222-2222-2222-222222222222"),
            InvitedUserIds = new List<Guid> { ShareTokenFuzzTarget.Caller },
            CreatedByUserId = ShareTokenFuzzTarget.Caller,
            CreatedAt = Now.AddDays(-1),
            ExpiresAt = Now.AddDays(1),
            RevokedAt = revokedAt,
            TokenHash = ShareTokenHash.Compute(key, token),
        };
}
