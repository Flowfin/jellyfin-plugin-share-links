namespace Jellyfin.Plugin.ShareLinks;

/// <summary>
/// How far a key rotation got (#243).
/// </summary>
/// <remarks>
/// <para>
/// A rotation is two writes to two different things: the records the shares are
/// kept in, and the key file. Between them is a state that is neither the
/// rotation an operator asked for nor the server they had before it, and a
/// rotation that reported success or failure alone would leave them unable to
/// tell which of the two they are looking at.
/// </para>
/// <para>
/// Two values and no third, because the store side cannot land half done. Every
/// live record is stopped in one <see cref="IShareStore.MutateAsync"/>, which
/// reads, changes and writes as one act, so either all of them stopped or none
/// of them did. What can land half done is the pair, and that is what these two
/// names are for.
/// </para>
/// </remarks>
public enum ShareKeyRotationOutcome
{
    /// <summary>
    /// The shares were stopped and the key was replaced. Nothing that was handed
    /// out resolves, and nothing that is handed out from now on was computed
    /// under the old key.
    /// </summary>
    Rotated = 0,

    /// <summary>
    /// The shares were stopped and the key was not replaced. The links that were
    /// handed out no longer resolve, because the records refuse them, and the key
    /// on disk is still the one that may have leaked. Pressing rotate again is
    /// safe: there is nothing left to stop, and the key write is what is retried.
    /// </summary>
    SharesStoppedKeyKept = 1,
}
