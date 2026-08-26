using System;
using System.Collections.Generic;
using MediaBrowser.Model.Dto;
using Xunit;

namespace Jellyfin.Plugin.ShareLinks.Tests;

/// <summary>
/// Whether a share's ceiling can be met for the item it names (#285).
/// </summary>
/// <remarks>
/// <para>
/// The rows are the whole test. What is being held is that exactly one of the
/// five answers is a refusal, and that the two ways of not knowing - a version
/// the server reported no bitrate for, and no versions at all - come out as
/// <see cref="CapReach.NotKnown"/> rather than as one.
/// </para>
/// <para>
/// A refusal manufactured out of an unknown is the failure this routine exists
/// against, so the rows that carry an unreported bitrate are the ones to read
/// first: every one of them sits beside a row that is identical except for the
/// unknown and does refuse.
/// </para>
/// </remarks>
public class BitrateCapReachTests
{
    private const long Ceiling = 2_000_000;

    /// <summary>
    /// Gets the ceiling, the versions, whether the account may transcode, and the answer.
    /// </summary>
    public static TheoryData<long?, PlayableVersion[], bool, CapReach> Table => new()
    {
        // No ceiling. Nothing to meet, whatever the item is or is not.
        { null, Array.Empty<PlayableVersion>(), false, CapReach.NoCeilingIsSet },
        { null, new[] { Above() }, false, CapReach.NoCeilingIsSet },
        { null, new[] { Unreported() }, true, CapReach.NoCeilingIsSet },

        // A version inside the ceiling. Nothing else in the list matters, and
        // the transcode permission is not read.
        { Ceiling, new[] { Below() }, false, CapReach.AVersionIsWithinIt },
        { Ceiling, new[] { Above(), Below() }, false, CapReach.AVersionIsWithinIt },
        { Ceiling, new[] { Unreported(), Below() }, false, CapReach.AVersionIsWithinIt },

        // Exactly at the ceiling is inside it. A guest may be held to the
        // ceiling rather than kept under it.
        { Ceiling, new[] { At() }, false, CapReach.AVersionIsWithinIt },

        // One over is outside it, which is the other half of the same boundary.
        { Ceiling, new[] { OneOver() }, false, CapReach.NothingCanBeServed },

        // Everything above the ceiling, and the transcode permission decides.
        { Ceiling, new[] { Above() }, true, CapReach.OnlyByTranscoding },
        { Ceiling, new[] { Above() }, false, CapReach.NothingCanBeServed },
        { Ceiling, new[] { AboveAndFixed() }, true, CapReach.NothingCanBeServed },
        { Ceiling, new[] { AboveAndFixed() }, false, CapReach.NothingCanBeServed },
        { Ceiling, new[] { AboveAndFixed(), Above() }, true, CapReach.OnlyByTranscoding },
        { Ceiling, new[] { AboveAndFixed(), Above() }, false, CapReach.NothingCanBeServed },

        // The two unknowns, each sitting beside the row it would otherwise be.
        { Ceiling, new[] { Unreported() }, false, CapReach.NotKnown },
        { Ceiling, new[] { Unreported(), Above() }, false, CapReach.NotKnown },
        { Ceiling, new[] { Unreported(), AboveAndFixed() }, true, CapReach.NotKnown },
        { Ceiling, Array.Empty<PlayableVersion>(), false, CapReach.NotKnown },
        { Ceiling, Array.Empty<PlayableVersion>(), true, CapReach.NotKnown }
    };

    [Theory]
    [MemberData(nameof(Table))]
    public void OnlyAnItemNothingCanBeBroughtUnderIsRefused(
        long? ceiling,
        PlayableVersion[] versions,
        bool accountMayTranscode,
        CapReach expected)
    {
        Assert.Equal(expected, BitrateCapReach.Of(ceiling, versions, accountMayTranscode));
    }

    [Fact]
    public void AnUnreportedBitrateBesideOneAboveTheCeilingDoesNotRefuse()
    {
        // The row above says the same thing. It is repeated as its own case
        // because it is the one this routine exists for: the same call with the
        // unknown removed does refuse, and a routine that read an absent bitrate
        // as a large one would pass every other row here.
        var withTheUnknown = new[] { Unreported(), Above() };
        var withoutIt = new[] { Above() };

        Assert.Equal(CapReach.NotKnown, BitrateCapReach.Of(Ceiling, withTheUnknown, false));
        Assert.Equal(CapReach.NothingCanBeServed, BitrateCapReach.Of(Ceiling, withoutIt, false));
    }

    [Theory]
    [InlineData(0, null)]
    [InlineData(-1, null)]
    [InlineData(int.MinValue, null)]
    [InlineData(1, 1L)]
    [InlineData(8_000_000, 8_000_000L)]
    public void ABitrateFieldCarryingNothingIsReadAsUnreported(int field, long? expected)
    {
        // The server's bitrate field is an ordinary integer, so a version it
        // knows nothing about arrives as a zero rather than as an absence. Read
        // literally that is a version at no bitrate at all, which is under every
        // ceiling, and the share would be reported as fine.
        Assert.Equal(expected, PlayableVersion.From(field, false).BitsPerSecond);
    }

    [Fact]
    public void AVersionTheServerDidNotReportOnAtAllIsUnreported()
    {
        Assert.Null(PlayableVersion.From(null, true).BitsPerSecond);
        Assert.True(PlayableVersion.From(null, true).SupportsTranscoding);
    }

    [Fact]
    public void AZeroBitrateVersionIsNotReadAsFittingUnderTheCeiling()
    {
        // The two halves above joined up, because this is the shape the
        // conversion exists to stop: a version the server said nothing about,
        // read through the factory, must not answer that the cap is met.
        var version = PlayableVersion.From(0, false);

        Assert.Equal(CapReach.NotKnown, BitrateCapReach.Of(Ceiling, new[] { version }, false));
    }

    [Fact]
    public void TheServersOwnSourceCarriesNoBitrateAndClaimsTranscoding()
    {
        // The two fields #284 will read, asserted against the server this plugin
        // compiles against rather than taken on trust, because the reading above
        // rests on what an untouched one carries.
        //
        // The bitrate is genuinely absent, so an unprobed source arrives as an
        // unknown rather than as a zero, and the conversion through
        // EffectiveBitrate.FromServerValue covers the case where the server
        // writes one instead.
        //
        // The transcoding flag defaults the OTHER way, to true, which is worth
        // stating because it is not what a reader assumes and because it decides
        // which direction an unprobed source fails in. A source above the ceiling
        // that nobody has looked at claims it can be transcoded, so it comes out
        // as OnlyByTranscoding for an account permitted to transcode rather than
        // as a refusal. That is the safe direction: this routine refuses a share
        // only where the server has said something definite about every version.
        var source = new MediaSourceInfo();

        Assert.Null(source.Bitrate);
        Assert.True(source.SupportsTranscoding);
    }

    [Fact]
    public void NothingIsDecidedFromAMissingList()
    {
        Assert.Throws<ArgumentNullException>(
            () => BitrateCapReach.Of(Ceiling, null!, false));
    }

    private static PlayableVersion Below() => new PlayableVersion(Ceiling - 1, false);

    private static PlayableVersion At() => new PlayableVersion(Ceiling, false);

    private static PlayableVersion OneOver() => new PlayableVersion(Ceiling + 1, false);

    private static PlayableVersion Above() => new PlayableVersion(Ceiling * 4, true);

    // Above the ceiling and not transcodable, which is the state an account
    // permitted to transcode still cannot get under the cap through.
    private static PlayableVersion AboveAndFixed() => new PlayableVersion(Ceiling * 4, false);

    private static PlayableVersion Unreported() => new PlayableVersion(null, true);
}
