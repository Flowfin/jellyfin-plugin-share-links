using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.ShareLinks;

/// <summary>
/// What a removal of released guest accounts did, including the part of it that
/// did not happen (#238).
/// </summary>
/// <remarks>
/// <para>
/// A removal reaches several accounts, and the interesting case is the one where
/// it reaches some of them. The server refusing the second of three deletions is
/// ordinary, and a call that stopped there would leave the third with nothing
/// naming it and nothing that will ever look at it again, because the record that
/// claimed it has already been swept. So the call continues past a failure and
/// answers with both halves rather than throwing.
/// </para>
/// <para>
/// This is the named state <c>docs/guest-accounts.md</c> asks a partial failure
/// to leave. It is not a promise that anything is retried: what is left behind is
/// left behind, an operator finds those accounts in the server's own user list,
/// and no later run of this plugin knows they were ever guests.
/// </para>
/// </remarks>
public sealed class GuestAccountRemoval
{
    /// <summary>
    /// Gets the accounts the server no longer holds after this call.
    /// </summary>
    /// <remarks>
    /// An account the server said it did not have is here rather than beside it.
    /// The call asked for the account to be gone and the account is gone, which is
    /// the state it was after, and a second call over the same identifier is the
    /// ordinary way that happens.
    /// </remarks>
    public required IReadOnlyList<Guid> Removed { get; init; }

    /// <summary>
    /// Gets the accounts that were asked about and are still there.
    /// </summary>
    /// <remarks>
    /// Each of these is an account this plugin made, whose last record has been
    /// swept, that the server refused to delete. It may still be enabled: what
    /// disables a guest account is the end of its last live share (#58), and a
    /// share that reached its expiry instant with no revocation after it was never
    /// seen by that routine.
    /// </remarks>
    public required IReadOnlyList<Guid> LeftBehind { get; init; }
}
