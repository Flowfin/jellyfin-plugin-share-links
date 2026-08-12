namespace Jellyfin.Plugin.ShareLinks;

/// <summary>
/// What a revocation did to the store (#27, #46).
/// </summary>
/// <remarks>
/// Three outcomes rather than a pair of flags, because "found and changed",
/// "found and already stopped" and "not there at all" are the three things an
/// operator reading a log line wants told apart, and a caller assembling that
/// sentence out of two booleans is a caller who can assemble it wrongly.
/// </remarks>
public enum ShareRevocation
{
    /// <summary>
    /// No record in the store carries that identifier.
    /// </summary>
    NoSuchShare = 0,

    /// <summary>
    /// The share was live and is now revoked.
    /// </summary>
    Revoked = 1,

    /// <summary>
    /// The share had already stopped, by an earlier revocation or by its own
    /// expiry instant, so nothing changed. This is not an error.
    /// </summary>
    AlreadyStopped = 2,
}
