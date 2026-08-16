namespace Jellyfin.Plugin.ShareLinks;

/// <summary>
/// What a share is doing at an instant, as the operator surface reports it (#39, #67).
/// </summary>
/// <remarks>
/// <para>
/// A listing that shows every record and says nothing about which of them still
/// work is a listing an operator reads as a list of live shares. That is the
/// failure #39 names in its own words: a share that can no longer resolve must
/// not look live.
/// </para>
/// <para>
/// Three values and no fourth. Whether a record still resolves is
/// <see cref="ShareBounds.IsLive"/>'s question and is answered there rather than
/// here; what this adds is which of the two ways of stopping happened first.
/// <see cref="ShareBounds.StateOf"/> is where a record is read into one of these.
/// </para>
/// </remarks>
public enum ShareState
{
    /// <summary>
    /// The share resolves. It is neither revoked nor past its expiry instant.
    /// </summary>
    Live = 0,

    /// <summary>
    /// The share reached its expiry instant while nothing had revoked it.
    /// </summary>
    Expired = 1,

    /// <summary>
    /// Somebody revoked the share while it was still live.
    /// </summary>
    Revoked = 2,
}
