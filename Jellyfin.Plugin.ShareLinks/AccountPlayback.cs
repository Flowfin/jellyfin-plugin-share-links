using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.ShareLinks;

/// <summary>
/// What the server says about playing one item for one account (#286).
/// </summary>
/// <param name="Versions">What the item can be played at, one entry per version the server reports.</param>
/// <param name="MayTranscode">Whether the account is permitted to transcode video.</param>
/// <remarks>
/// <para>
/// The two inputs <see cref="BitrateCapReach"/> takes beside the ceiling, carried
/// as one value because they are read together and are about one pair of an
/// account and an item. A caller holding them apart is a caller that can pass the
/// versions of one account beside the permission of another, which is a wrong
/// answer nothing would refuse.
/// </para>
/// <para>
/// No server type appears here, which is what lets
/// <see cref="GuestCeilings.OfAsync"/> stay drivable with no server at all -
/// <c>docs/testing.md</c>'s rule. Reading a running server into this is
/// <see cref="PlayableVersions"/>'s, and it is the one adapter.
/// </para>
/// <para>
/// <see cref="Nothing"/> is what a caller that could not ask hands back. It is an
/// empty version list, which <see cref="BitrateCapReach"/> answers
/// <see cref="CapReach.NotKnown"/> to rather than refusing on, so an account or
/// an item the server did not hand back comes out as a question nobody answered
/// instead of as a share that cannot be served.
/// </para>
/// </remarks>
public readonly record struct AccountPlayback(IReadOnlyList<PlayableVersion> Versions, bool MayTranscode)
{
    /// <summary>
    /// Gets the answer for a pair the server said nothing about.
    /// </summary>
    public static AccountPlayback Nothing { get; } = new AccountPlayback(Array.Empty<PlayableVersion>(), false);
}
