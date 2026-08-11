using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Users;
using Xunit;

namespace Jellyfin.Plugin.ShareLinks.Tests;

/// <summary>
/// Every combination of the three ceilings being set and unset, with the value
/// and the ceilings that produced it (#64).
/// </summary>
/// <remarks>
/// <para>
/// The table is the test. Eight rows are the combinations the issue names, and
/// the rest are the cases the combinations do not reach: the lowest arriving from
/// each of the three positions in turn, and ties, which the issue leaves open and
/// which are answered here by reporting every ceiling at the value rather than
/// choosing one.
/// </para>
/// <para>
/// Both halves of the answer are asserted in one row. A table that compared only
/// the number would pass an implementation that guessed which ceiling produced
/// it, and guessing is the defect this issue exists against.
/// </para>
/// </remarks>
public class EffectiveBitrateTests
{
    private const long Low = 2_000_000;
    private const long Middle = 5_000_000;
    private const long High = 9_000_000;

    /// <summary>
    /// Gets the share ceiling, the account ceiling, the server ceiling, the effective value and the ceilings that produced it.
    /// </summary>
    public static TheoryData<long?, long?, long?, long?, BitrateCeiling> Table => new()
    {
        // The eight combinations of set and unset.
        { null, null, null, null, BitrateCeiling.None },
        { Middle, null, null, Middle, BitrateCeiling.Share },
        { null, Middle, null, Middle, BitrateCeiling.Account },
        { null, null, Middle, Middle, BitrateCeiling.ServerRemoteClientLimit },
        { Low, High, null, Low, BitrateCeiling.Share },
        { Low, null, High, Low, BitrateCeiling.Share },
        { null, Low, High, Low, BitrateCeiling.Account },
        { Low, Middle, High, Low, BitrateCeiling.Share },

        // The lowest arriving from each position, so that a routine returning the
        // first value it was handed is refused rather than accidentally right.
        { High, Low, Middle, Low, BitrateCeiling.Account },
        { High, Middle, Low, Low, BitrateCeiling.ServerRemoteClientLimit },
        { Middle, High, Low, Low, BitrateCeiling.ServerRemoteClientLimit },

        // Ties. Every ceiling at the value is named, because an operator who
        // lowers one that was not reported would otherwise see nothing change.
        { Low, Low, null, Low, BitrateCeiling.Share | BitrateCeiling.Account },
        { Low, null, Low, Low, BitrateCeiling.Share | BitrateCeiling.ServerRemoteClientLimit },
        { null, Low, Low, Low, BitrateCeiling.Account | BitrateCeiling.ServerRemoteClientLimit },
        { Low, Low, Low, Low, BitrateCeiling.Share | BitrateCeiling.Account | BitrateCeiling.ServerRemoteClientLimit },
        { Low, Low, High, Low, BitrateCeiling.Share | BitrateCeiling.Account },
        { High, Low, Low, Low, BitrateCeiling.Account | BitrateCeiling.ServerRemoteClientLimit }
    };

    [Theory]
    [MemberData(nameof(Table))]
    public void TheLowestCeilingWinsAndEveryCeilingAtItIsNamed(
        long? share,
        long? account,
        long? server,
        long? expected,
        BitrateCeiling applied)
    {
        var effective = EffectiveBitrate.Lowest(share, account, server);

        Assert.Equal(expected, effective.BitsPerSecond);
        Assert.Equal(applied, effective.Applied);
    }

    [Fact]
    public void NoCeilingAnywhereIsNoCeilingRatherThanZero()
    {
        // Read back as its own row above as well. It is repeated here because
        // the two halves say different things: no number, and nothing to name.
        var effective = EffectiveBitrate.Lowest(null, null, null);

        Assert.Null(effective.BitsPerSecond);
        Assert.Equal(BitrateCeiling.None, effective.Applied);
    }

    [Theory]
    [InlineData(0, null)]
    [InlineData(-1, null)]
    [InlineData(int.MinValue, null)]
    [InlineData(1, 1L)]
    [InlineData(8_000_000, 8_000_000L)]
    public void AServerFieldCarryingNothingIsReadAsNoCeiling(int field, long? expected)
    {
        // Taken literally, a zero here would cap every share on a server whose
        // administrator has set no limit at nothing at all.
        Assert.Equal(expected, EffectiveBitrate.FromServerValue(field));
    }

    [Fact]
    public void TheTwoServerFieldsSpellNoCeilingAsZero()
    {
        // The fact the reading above rests on, asserted against the server this
        // plugin compiles against rather than taken on trust. Both are plain
        // integers with no way to be absent, so an untouched one has to say so
        // with a value, and zero is the value it says it with.
        Assert.Equal(0, new UserPolicy().RemoteClientBitrateLimit);
        Assert.Equal(0, new ServerConfiguration().RemoteClientBitrateLimit);
    }
}
