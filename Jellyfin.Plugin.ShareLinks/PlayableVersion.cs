namespace Jellyfin.Plugin.ShareLinks;

/// <summary>
/// One version of an item, as far as deciding whether a cap can be met needs it (#285).
/// </summary>
/// <param name="BitsPerSecond">What the server says this version is served at, or <c>null</c> where it says nothing.</param>
/// <param name="SupportsTranscoding">Whether this version can be transcoded down.</param>
/// <remarks>
/// <para>
/// Two fields out of a much larger thing, and the narrowness is the point.
/// <see cref="BitrateCapReach"/> takes this rather than the server's own source
/// type so that every case can be driven with no server, which is what
/// <c>docs/testing.md</c> requires of everything in this repository. Reading the
/// server's sources into these two fields is #284's and is one adapter.
/// </para>
/// <para>
/// <see cref="BitsPerSecond"/> is nullable because "the server reports no
/// bitrate for this version" is a real answer and is not the same as a version
/// above a ceiling. Turning the first into the second is the mistake
/// <see cref="BitrateCapReach"/> exists to not make, so the absence has to
/// survive as far as the decision.
/// </para>
/// </remarks>
public readonly record struct PlayableVersion(long? BitsPerSecond, bool SupportsTranscoding)
{
    /// <summary>
    /// Reads one version off the two fields the server carries.
    /// </summary>
    /// <param name="bitsPerSecond">The bitrate the server reports, in bits per second.</param>
    /// <param name="supportsTranscoding">Whether the server says this version can be transcoded.</param>
    /// <returns>The version, with a bitrate the server did not report left absent.</returns>
    /// <remarks>
    /// The conversion goes through <see cref="EffectiveBitrate.FromServerValue"/>
    /// rather than comparing against zero here, because what a non-positive value
    /// out of one of the server's own integer fields means is already decided
    /// there and argued there. A second reading of it in this file is how the two
    /// come to disagree about a field neither of them owns.
    /// </remarks>
    public static PlayableVersion From(int? bitsPerSecond, bool supportsTranscoding)
        => new PlayableVersion(
            bitsPerSecond is { } reported ? EffectiveBitrate.FromServerValue(reported) : null,
            supportsTranscoding);
}
