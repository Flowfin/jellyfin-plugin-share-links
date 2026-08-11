using System;
using System.Globalization;
using System.IO;
using System.Xml.Serialization;
using Jellyfin.Plugin.ShareLinks.Configuration;
using Xunit;

namespace Jellyfin.Plugin.ShareLinks.Tests;

/// <summary>
/// The bitrate ceiling's configuration surface: the unit an operator writes, the
/// unit a record keeps, the bounds between them, and what an absent value means
/// (#62).
/// </summary>
/// <remarks>
/// <para>
/// The mistake being defended against is a unit mistake, so the tests are written
/// as the two directions it goes in rather than as one valid value and one
/// invalid one. A number typed in bits per second lands a million times too high
/// and a number that meant kilobits lands a thousand times too low, and both have
/// to be refused by a bound rather than served.
/// </para>
/// <para>
/// Nothing here was measured against a server, a client or a media file. Whether
/// a given ceiling plays a given file is not a question these tests ask, and the
/// bounds are a judgement about which numbers are worth accepting at all.
/// </para>
/// </remarks>
public class BitrateCapTests
{
    [Fact]
    public void ANewInstallHasNoCeilingForNewShares()
    {
        // Absent rather than a number, so an install that was never configured
        // does not quietly cap what an operator did not ask to cap.
        Assert.Null(new PluginConfiguration().DefaultMaxBitrateMbps);
        Assert.Null(BitrateCap.DefaultForNewShares(new PluginConfiguration()));
    }

    [Fact]
    public void NoValueIsNoCeilingRatherThanACeilingOfZero()
    {
        Assert.Null(BitrateCap.Refuse(null));
        Assert.Null(BitrateCap.InBitsPerSecond(null));
    }

    [Theory]
    [InlineData(8, 8_000_000L)]
    [InlineData(0.1, 100_000L)]
    [InlineData(1000, 1_000_000_000L)]
    [InlineData(2.5, 2_500_000L)]
    public void AnOperatorsMegabitsBecomeTheBitsARecordKeeps(double megabits, long expected)
    {
        Assert.Equal(expected, BitrateCap.InBitsPerSecond(megabits));
    }

    [Fact]
    public void AFractionOfABitRoundsRatherThanBeingDroppedDownwards()
    {
        // 0.1234567 Mbit/s is 123456.7 bit/s. Truncating would hand back a ceiling
        // below the one asked for, which is a change in the direction nobody
        // notices.
        Assert.Equal(123_457L, BitrateCap.InBitsPerSecond(0.1234567));
    }

    [Fact]
    public void ACeilingOfZeroIsRefusedRatherThanReadAsNoCeiling()
    {
        var refusal = BitrateCap.Refuse(0);

        Assert.NotNull(refusal);
        Assert.Contains("lowest that may be set", refusal, StringComparison.Ordinal);
        Assert.Throws<ArgumentOutOfRangeException>(() => BitrateCap.InBitsPerSecond(0));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(-0.5)]
    [InlineData(0.09)]
    public void ACeilingBelowWhatWouldPlayAnythingIsRefusedAndTheMessageNamesTheBound(double megabits)
    {
        var refusal = BitrateCap.Refuse(megabits);

        Assert.NotNull(refusal);
        Assert.Contains(
            string.Create(CultureInfo.InvariantCulture, $"{BitrateCap.MinimumMegabitsPerSecond:0.###} Mbit/s"),
            refusal,
            StringComparison.Ordinal);
    }

    [Fact]
    public void TheUnitMistakeThisSettingExistsAgainstIsRefusedAndTheMessageNamesTheBound()
    {
        // Eight megabits typed into the field as bits per second. Accepted, it is a
        // ceiling of eight million megabits, which is no ceiling at all and reads
        // like an ordinary number in a configuration file.
        var refusal = BitrateCap.Refuse(8_000_000);

        Assert.NotNull(refusal);
        Assert.Contains("highest that may be set", refusal, StringComparison.Ordinal);
        Assert.Contains(
            string.Create(CultureInfo.InvariantCulture, $"{BitrateCap.MaximumMegabitsPerSecond:0.###} Mbit/s"),
            refusal,
            StringComparison.Ordinal);
        Assert.Throws<ArgumentOutOfRangeException>(() => BitrateCap.InBitsPerSecond(8_000_000));
    }

    [Fact]
    public void BothEndsOfTheRangeAreInsideIt()
    {
        // The bounds refuse what is past them and not what is on them, which is the
        // off-by-one somebody writing a comparison actually makes.
        Assert.Null(BitrateCap.Refuse(BitrateCap.MinimumMegabitsPerSecond));
        Assert.Null(BitrateCap.Refuse(BitrateCap.MaximumMegabitsPerSecond));
    }

    [Fact]
    public void SomethingThatIsNotANumberIsRefusedRatherThanConverted()
    {
        // A comparison against NaN is false in both directions, so a ceiling of NaN
        // walks through a range check written the obvious way and becomes a
        // conversion that produces nothing meaningful.
        Assert.NotNull(BitrateCap.Refuse(double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => BitrateCap.InBitsPerSecond(double.NaN));
    }

    [Fact]
    public void AConfiguredCeilingOutsideTheBoundsIsRefusedByTheNameOfTheSetting()
    {
        var configuration = new PluginConfiguration { DefaultMaxBitrateMbps = 0 };

        var refused = Assert.Throws<ArgumentOutOfRangeException>(() => BitrateCap.DefaultForNewShares(configuration));

        // The setting an operator has to go and edit, not just the bound it missed.
        // A message about a ceiling out of range leaves them looking for which of
        // several numbers in the file it was about.
        Assert.Contains(nameof(PluginConfiguration.DefaultMaxBitrateMbps), refused.Message, StringComparison.Ordinal);
        Assert.Contains("lowest that may be set", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AConfiguredCeilingInsideTheBoundsIsWhatANewShareWouldGet()
    {
        var configuration = new PluginConfiguration { DefaultMaxBitrateMbps = 6 };

        Assert.Equal(6_000_000L, BitrateCap.DefaultForNewShares(configuration));
    }

    [Fact]
    public void TheCeilingSurvivesTheSerialiserTheServerUses()
    {
        var serialiser = new XmlSerializer(typeof(PluginConfiguration));
        using var written = new StringWriter(CultureInfo.InvariantCulture);
        serialiser.Serialize(written, new PluginConfiguration { DefaultMaxBitrateMbps = 12.5 });

        using var read = new StringReader(written.ToString());
        var restored = Assert.IsType<PluginConfiguration>(serialiser.Deserialize(read));

        Assert.Equal(12.5, restored.DefaultMaxBitrateMbps);
    }

    [Fact]
    public void NoCeilingSurvivesTheSerialiserAsNoCeiling()
    {
        // The case that would go wrong quietly: an operator who set no ceiling gets
        // one after a restart, or the absence turns into a zero, which this setting
        // refuses.
        var serialiser = new XmlSerializer(typeof(PluginConfiguration));
        using var written = new StringWriter(CultureInfo.InvariantCulture);
        serialiser.Serialize(written, new PluginConfiguration());

        using var read = new StringReader(written.ToString());
        var restored = Assert.IsType<PluginConfiguration>(serialiser.Deserialize(read));

        Assert.Null(restored.DefaultMaxBitrateMbps);
    }

    [Fact]
    public void AConfigurationFileWithNoSuchElementReadsAsNoCeiling()
    {
        // What an install upgrading from a version before this setting existed
        // meets, and what an operator who deletes the line gets.
        var serialiser = new XmlSerializer(typeof(PluginConfiguration));
        using var read = new StringReader("<PluginConfiguration><MaxLiveShares>4</MaxLiveShares></PluginConfiguration>");

        var restored = Assert.IsType<PluginConfiguration>(serialiser.Deserialize(read));

        Assert.Null(restored.DefaultMaxBitrateMbps);
        Assert.Equal(4, restored.MaxLiveShares);
    }

    [Fact]
    public void ABlankedOutElementIsRefusedByTheSerialiserRatherThanReadAsNoCeiling()
    {
        // Measured rather than assumed, and it is the reason docs/configuration.md
        // tells an operator to delete the line rather than empty it. The serialiser
        // is the server's, so this refusal happens before any of this plugin's code
        // runs and nothing here can soften it.
        var serialiser = new XmlSerializer(typeof(PluginConfiguration));
        using var read = new StringReader("<PluginConfiguration><DefaultMaxBitrateMbps></DefaultMaxBitrateMbps></PluginConfiguration>");

        Assert.Throws<InvalidOperationException>(() => serialiser.Deserialize(read));
    }
}
