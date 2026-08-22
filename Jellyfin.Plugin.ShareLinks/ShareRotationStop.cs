using System.Collections.Generic;

namespace Jellyfin.Plugin.ShareLinks;

/// <summary>
/// What stopping every live share left behind, both halves of it (#243).
/// </summary>
/// <remarks>
/// <para>
/// Two lists rather than one, because the caller needs both and they answer
/// different questions. <see cref="Stopped"/> is what this call did, which is the
/// number an operator is told and the set whose guests are signed out.
/// <see cref="Store"/> is everything the store holds afterwards, which is what
/// decides whether a guest named by one of those records still has something else
/// to watch.
/// </para>
/// <para>
/// The second list is returned rather than read back, and that is not a saving.
/// A read after the write is a second answer that another writer can have moved
/// underneath, and the store's own mutation already knows what it wrote.
/// </para>
/// </remarks>
/// <param name="Stopped">The records this call stopped, as they stand afterwards.</param>
/// <param name="Store">Every record the store holds after the change.</param>
public sealed record ShareRotationStop(
    IReadOnlyList<ShareRecord> Stopped,
    IReadOnlyList<ShareRecord> Store);
