namespace Jellyfin.Plugin.ShareLinks;

/// <summary>
/// What a rotation did, as the operator surface reports it (#28, #243).
/// </summary>
/// <remarks>
/// <para>
/// The number is the fact an operator needs and will not have if the call
/// answers with nothing: every link that was handed out has stopped, and how
/// many of them there were is the size of what they have just done.
/// </para>
/// <para>
/// It carries the state as well as the number, because the two answers a
/// rotation can leave behind are told apart by nothing else.
/// <see cref="ShareKeyRotationOutcome"/> is where that is argued.
/// </para>
/// <para>
/// No key material, no path and no token is here. What an operator is told is
/// how many shares stopped; where the file is and what is in it are the log's
/// and the file's, for the reason <c>docs/share-key.md</c> gives about the key
/// never travelling.
/// </para>
/// </remarks>
public sealed class ShareKeyRotated
{
    /// <summary>
    /// Gets how many live shares this rotation stopped, counted the moment they were stopped.
    /// </summary>
    public required int SharesStopped { get; init; }

    /// <summary>
    /// Gets how far the rotation got.
    /// </summary>
    public required ShareKeyRotationOutcome Outcome { get; init; }
}
