using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.ShareLinks;

/// <summary>
/// One share as the administrator surface sees it (#67).
/// </summary>
/// <remarks>
/// <para>
/// A separate type rather than the record itself, and the separation is the
/// whole point of it. <see cref="ShareRecord"/> carries
/// <see cref="ShareRecord.TokenHash"/>, and a route answering with the record
/// would put that hash into every listing, into whatever an operator's browser
/// caches, and into any script that ever reads the route. The hash is not the
/// token and it does not open a share, but it is the value the resolution
/// compares against, and handing it out is handing out the one thing an offline
/// search needs.
/// </para>
/// <para>
/// So the fields here are chosen rather than inherited, and the choice is
/// checked. <c>AdministratorRouteTests</c> compares the members of this type
/// against the list it allows, so a field added to
/// <see cref="ShareRecord"/> and copied here without being argued for reds the
/// run rather than shipping.
/// </para>
/// <para>
/// The link is not here either, and cannot be. Only the keyed hash of a token is
/// written down, so the plugin cannot produce a link a second time even when
/// asked. That is what makes "shown once" a property of the store rather than a
/// promise the surface keeps, and it is why #67 asks for the link in the create
/// response and never in this one.
/// </para>
/// </remarks>
public sealed class ShareSummary
{
    /// <summary>
    /// Gets the identifier of the share. This is what a revocation names.
    /// </summary>
    public required Guid Id { get; init; }

    /// <summary>
    /// Gets the item the share is for.
    /// </summary>
    public required Guid ItemId { get; init; }

    /// <summary>
    /// Gets the accounts the share resolves for.
    /// </summary>
    public required IReadOnlyList<Guid> InvitedUserIds { get; init; }

    /// <summary>
    /// Gets the account that made the share.
    /// </summary>
    public required Guid CreatedByUserId { get; init; }

    /// <summary>
    /// Gets when the share was made.
    /// </summary>
    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>
    /// Gets when the share stops resolving on its own.
    /// </summary>
    public required DateTimeOffset ExpiresAt { get; init; }

    /// <summary>
    /// Gets what the share is doing at the instant the listing was read.
    /// </summary>
    /// <remarks>
    /// Read at the instant of the request rather than stored, because expiry is a
    /// comparison against a clock and a state written into a file would be a
    /// second answer that goes stale on its own.
    /// </remarks>
    public required ShareState State { get; init; }

    /// <summary>
    /// Gets when the share was revoked, or <c>null</c> where nothing revoked it.
    /// </summary>
    public DateTimeOffset? RevokedAt { get; init; }

    /// <summary>
    /// Gets the account that revoked the share, or <c>null</c> where no revoker was written down.
    /// </summary>
    public Guid? RevokedByUserId { get; init; }

    /// <summary>
    /// Gets what the operator who revoked the share wrote, or <c>null</c>.
    /// </summary>
    /// <remarks>
    /// It reaches this surface and never a guest. A refusal that explained itself
    /// would tell the wrong reader why their link stopped working, which is #26.
    /// </remarks>
    public string? RevocationReason { get; init; }

    /// <summary>
    /// Gets the ceiling this share puts on the bitrate, in bits per second, or <c>null</c> where it names none.
    /// </summary>
    /// <remarks>
    /// Bits per second, as the record holds it. The setting an operator types is
    /// megabits per second and <see cref="BitrateCap"/> is the one place the two
    /// meet; a conversion here would be a second one.
    /// </remarks>
    public long? MaxBitrateBitsPerSecond { get; init; }

    /// <summary>
    /// Reads one record into what the administrator surface may see.
    /// </summary>
    /// <param name="record">The record.</param>
    /// <param name="now">The instant the state is read at.</param>
    /// <returns>The summary.</returns>
    public static ShareSummary Of(ShareRecord record, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(record);

        return new ShareSummary
        {
            Id = record.Id,
            ItemId = record.ItemId,
            InvitedUserIds = record.InvitedUserIds,
            CreatedByUserId = record.CreatedByUserId,
            CreatedAt = record.CreatedAt,
            ExpiresAt = record.ExpiresAt,
            State = ShareBounds.StateOf(record, now),
            RevokedAt = record.RevokedAt,
            RevokedByUserId = record.RevokedByUserId,
            RevocationReason = record.RevocationReason,
            MaxBitrateBitsPerSecond = record.MaxBitrateBitsPerSecond,
        };
    }
}
