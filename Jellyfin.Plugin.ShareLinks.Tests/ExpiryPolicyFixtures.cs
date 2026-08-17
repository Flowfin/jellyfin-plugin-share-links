using System;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.ShareLinks.Tests;

/// <summary>
/// Routines that exist so the guard in <see cref="ExpiryPolicy"/> has something
/// to be right and wrong about (#45, #47).
/// </summary>
/// <remarks>
/// <para>
/// None of these ships. A guard whose only subject is the plugin passes for two
/// reasons and only one of them is good: either it read the routines and cleared
/// them, or it found none and said nothing. These make the difference visible,
/// and each refused one is written as the mistake somebody would actually make
/// rather than as an obvious rewrite of the rule.
/// </para>
/// </remarks>
public static class ExpiryPolicyFixtures
{
    /// <summary>
    /// The shape every writer in the plugin has: the record is rebuilt and the
    /// instant is copied off the record that was handed in.
    /// </summary>
    public static class AWriterThatCopiesTheInstant
    {
        /// <summary>
        /// Writes a revocation onto the record and carries everything else.
        /// </summary>
        /// <param name="record">The record as it was read.</param>
        /// <param name="revokedAt">The instant the revocation happened at.</param>
        /// <returns>The same share, revoked.</returns>
        public static ShareRecord Revoked(ShareRecord record, DateTimeOffset revokedAt)
        {
            ArgumentNullException.ThrowIfNull(record);

            return Rebuilt(record, record.ExpiresAt, revokedAt);
        }
    }

    /// <summary>
    /// The near-miss the guard is written for. The instant assigned is the one
    /// the neighbouring line uses, which compiles because both are
    /// <see cref="DateTimeOffset"/> and which turns a share's expiry into the
    /// moment somebody revoked it.
    /// </summary>
    public static class AWriterThatAssignsTheNeighbouringInstant
    {
        /// <summary>
        /// Writes a revocation onto the record and takes the expiry from the
        /// wrong parameter.
        /// </summary>
        /// <param name="record">The record as it was read.</param>
        /// <param name="revokedAt">The instant the revocation happened at.</param>
        /// <returns>A share whose expiry has moved.</returns>
        public static ShareRecord Revoked(ShareRecord record, DateTimeOffset revokedAt)
        {
            ArgumentNullException.ThrowIfNull(record);

            return Rebuilt(record, revokedAt, revokedAt);
        }
    }

    /// <summary>
    /// A routine that moves the instant in one direction only, which is what an
    /// extend-but-never-shorten rule written by hand looks like. It is here
    /// because a guard driving one instant would clear it.
    /// </summary>
    public static class AWriterThatOnlyEverExtends
    {
        /// <summary>
        /// Rewrites the record, taking the later of the two instants.
        /// </summary>
        /// <param name="record">The record as it was read.</param>
        /// <param name="asked">The instant somebody asked for.</param>
        /// <returns>A share expiring at whichever instant is later.</returns>
        public static ShareRecord Extended(ShareRecord record, DateTimeOffset asked)
        {
            ArgumentNullException.ThrowIfNull(record);

            return Rebuilt(record, asked > record.ExpiresAt ? asked : record.ExpiresAt, record.RevokedAt);
        }
    }

    /// <summary>
    /// A writer that changes a great deal and leaves the instant alone. The
    /// guard is about the expiry and nothing else, so this one is accepted, and
    /// it is here to prove the guard is not a general copy check wearing this
    /// rule's name.
    /// </summary>
    public static class AWriterThatChangesEverythingElse
    {
        /// <summary>
        /// Rewrites every field the record has except the expiry.
        /// </summary>
        /// <param name="record">The record as it was read.</param>
        /// <param name="movedTo">The instant everything else is moved to.</param>
        /// <returns>A very different share, expiring when the old one did.</returns>
        public static ShareRecord Rewritten(ShareRecord record, DateTimeOffset movedTo)
        {
            ArgumentNullException.ThrowIfNull(record);

            return new ShareRecord
            {
                SchemaVersion = ShareRecord.CurrentSchemaVersion,
                Id = Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff"),
                ItemId = Guid.Parse("ffffffff-ffff-ffff-ffff-fffffffffffe"),
                InvitedUserIds = [],
                CreatedByUserId = Guid.Parse("ffffffff-ffff-ffff-ffff-fffffffffffd"),
                CreatedAt = movedTo,
                ExpiresAt = record.ExpiresAt,
                RevokedAt = movedTo,
                RevocationReason = "everything but the instant",
                RevokedByUserId = Guid.Parse("ffffffff-ffff-ffff-ffff-fffffffffffc"),
                MaxBitrateBitsPerSecond = 1,
                TokenHash = record.TokenHash,
            };
        }
    }

    /// <summary>
    /// A writer that answers through a task, because a routine that reaches the
    /// store will, and a guard that read only the synchronous shape would clear
    /// it by not seeing it.
    /// </summary>
    public static class AWriterThatAnswersThroughATask
    {
        /// <summary>
        /// Rewrites the record and moves the instant, through a task.
        /// </summary>
        /// <param name="record">The record as it was read.</param>
        /// <param name="asked">The instant somebody asked for.</param>
        /// <returns>A task answering with a share whose expiry has moved.</returns>
        public static Task<ShareRecord> ExtendedAsync(ShareRecord record, DateTimeOffset asked)
        {
            ArgumentNullException.ThrowIfNull(record);

            return Task.FromResult(Rebuilt(record, asked, record.RevokedAt));
        }
    }

    /// <summary>
    /// A writer the guard has no value to hand, so it is refused rather than
    /// skipped. A routine the guard cannot drive and a routine it drove and
    /// cleared must not read the same.
    /// </summary>
    public static class AWriterTheGuardCannotDrive
    {
        /// <summary>
        /// Rewrites the record under something the guard's table does not carry.
        /// </summary>
        /// <param name="record">The record as it was read.</param>
        /// <param name="how">A parameter of a type the guard has no value for.</param>
        /// <returns>The same share.</returns>
        public static ShareRecord Rewritten(ShareRecord record, Uri how)
        {
            ArgumentNullException.ThrowIfNull(record);
            ArgumentNullException.ThrowIfNull(how);

            return Rebuilt(record, record.ExpiresAt, record.RevokedAt);
        }
    }

    private static ShareRecord Rebuilt(ShareRecord record, DateTimeOffset expiresAt, DateTimeOffset? revokedAt) => new ShareRecord
    {
        SchemaVersion = record.SchemaVersion,
        Id = record.Id,
        ItemId = record.ItemId,
        InvitedUserIds = record.InvitedUserIds,
        PluginCreatedUserIds = record.PluginCreatedUserIds,
        CreatedByUserId = record.CreatedByUserId,
        CreatedAt = record.CreatedAt,
        ExpiresAt = expiresAt,
        RevokedAt = revokedAt,
        RevocationReason = record.RevocationReason,
        RevokedByUserId = record.RevokedByUserId,
        MaxBitrateBitsPerSecond = record.MaxBitrateBitsPerSecond,
        TokenHash = record.TokenHash,
    };
}
