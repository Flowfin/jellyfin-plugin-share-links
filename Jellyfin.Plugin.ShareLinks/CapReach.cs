namespace Jellyfin.Plugin.ShareLinks;

/// <summary>
/// Whether anything can be served under a share's ceiling, and how (#285).
/// </summary>
/// <remarks>
/// <para>
/// Five answers rather than a boolean, because the three that are not a refusal
/// fail differently and a caller that collapsed them would refuse a share that
/// works. <see cref="NoCeilingIsSet"/> and <see cref="NotKnown"/> are both "do
/// not refuse", for opposite reasons: the first is a share with nothing to meet,
/// the second is a question the server did not answer.
/// </para>
/// <para>
/// Only <see cref="NothingCanBeServed"/> is the condition #63 is about. Every
/// other member is a caller's cue to leave the request alone.
/// </para>
/// </remarks>
public enum CapReach
{
    /// <summary>
    /// The server did not say enough to decide, so nothing is concluded.
    /// </summary>
    /// <remarks>
    /// First, and zero, so that a value nobody set reads as the answer that
    /// refuses nothing. A default that meant <see cref="NothingCanBeServed"/>
    /// would turn a field somebody forgot into a refusal.
    /// </remarks>
    NotKnown = 0,

    /// <summary>
    /// No ceiling applies, so there is nothing for the item to fit under.
    /// </summary>
    NoCeilingIsSet = 1,

    /// <summary>
    /// At least one version is at or below the ceiling and can be played as it is.
    /// </summary>
    AVersionIsWithinIt = 2,

    /// <summary>
    /// Every version is above the ceiling, and one of them can be transcoded down for an account permitted to.
    /// </summary>
    OnlyByTranscoding = 3,

    /// <summary>
    /// Every version is above the ceiling and none of them can be brought under it.
    /// </summary>
    /// <remarks>
    /// The one member that is a refusal. It means the share's cap and the item
    /// cannot both be honoured, which is the outcome #63 exists to stop being a
    /// stream that quietly ignores the cap.
    /// </remarks>
    NothingCanBeServed = 4,
}
