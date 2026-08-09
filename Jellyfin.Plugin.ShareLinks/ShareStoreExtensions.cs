using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.ShareLinks;

/// <summary>
/// The operations every share store gets for free, written over <see cref="IShareStore"/> (#34).
/// </summary>
/// <remarks>
/// This is where an operation lives when it is a rule about records rather than a
/// fact about storage. Nothing here knows what a store is made of, and that is
/// what makes the choice in <c>docs/share-store.md</c> revisable: a second
/// implementation inherits these rules instead of restating them.
/// </remarks>
public static class ShareStoreExtensions
{
    /// <summary>
    /// Adds a record, sweeping what retention no longer keeps and refusing a create that would pass a ceiling (#29).
    /// </summary>
    /// <param name="store">The store to add to.</param>
    /// <param name="record">The record to add.</param>
    /// <param name="bounds">The ceilings and the retention rule.</param>
    /// <param name="now">The instant the create is happening at.</param>
    /// <param name="cancellationToken">Cancels the change.</param>
    /// <returns>The records that were written.</returns>
    /// <exception cref="ShareBoundExceededException">The create would pass a ceiling. The store is left as it was, so the refusal costs nothing.</exception>
    /// <remarks>
    /// <para>
    /// The check is here rather than only in the route because this is where the
    /// store grows. A ceiling enforced at the route alone is a ceiling that holds
    /// for the callers somebody remembered, and the failure being defended against
    /// is a caller nobody thought about.
    /// </para>
    /// <para>
    /// It is over the interface rather than on one implementation for the same
    /// reason one step out. A ceiling that lives on the file store is a ceiling
    /// the day somebody writes a second store has to be found and copied, and a
    /// copy is a thing that can be copied wrong. Here the two implementations
    /// cannot disagree, because there is one of it.
    /// </para>
    /// <para>
    /// The sweep runs before the ceiling is counted and on the way to every write
    /// rather than on a timer. That is deliberate and it is not free: a server
    /// nobody creates a share on never sweeps, so retention bounds what a write
    /// leaves behind rather than what a quiet server holds. A share cannot be
    /// created without a sweep happening first, which is the direction that
    /// matters for the ceiling; the timer that would make deletion prompt belongs
    /// with the scheduled task, and there is none in this tree.
    /// </para>
    /// <para>
    /// <see cref="IShareStore.MutateAsync"/> is the general seam and is not
    /// bounded. Nothing refuses a future caller that appends through it, and the
    /// invariant lint reads text rather than call graphs, so this is a rule the
    /// review holds.
    /// </para>
    /// </remarks>
    public static async Task<IReadOnlyList<ShareRecord>> AddAsync(
        this IShareStore store,
        ShareRecord record,
        ShareBounds bounds,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(bounds);

        return await store.MutateAsync(
            current =>
            {
                var kept = bounds.Retained(current, now);

                var refusal = bounds.Refuse(kept, record, now);
                if (refusal is not null)
                {
                    throw new ShareBoundExceededException(refusal);
                }

                var next = new List<ShareRecord>(kept.Count + 1);
                next.AddRange(kept);
                next.Add(record);
                return next;
            },
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Revokes a share, immediately, repeatably, and without deleting anything (#46).
    /// </summary>
    /// <param name="store">The store holding the share.</param>
    /// <param name="shareId">The share to revoke.</param>
    /// <param name="revokedByUserId">The account pressing revoke.</param>
    /// <param name="revokedAt">The instant the revocation happens at, which is also the instant the share's state is judged against.</param>
    /// <param name="reason">Free text for another operator to read later, or <c>null</c>.</param>
    /// <param name="cancellationToken">Cancels the change.</param>
    /// <returns>The record as it stands after the call, or <c>null</c> where the store holds no share with that identifier.</returns>
    /// <remarks>
    /// <para>
    /// Immediate means the record is changed rather than queued. There is no
    /// sweep here and none is needed: the resolution decision reads
    /// <see cref="ShareRecord.RevokedAt"/> on every request, so the next request
    /// after this call is refused by the record it reads. Deleting the record
    /// would refuse the same request and lose the reason an operator wrote and
    /// the fact that anything was ever shared, which is the audit trail this
    /// keeps.
    /// </para>
    /// <para>
    /// Repeatable means the second call succeeds and changes nothing. It does not
    /// mean the second call rewrites the same values: a revocation already
    /// recorded keeps its instant, its reason and its revoker, because the first
    /// press is the one that stopped the share and the second press did not stop
    /// anything. A share that has already expired is the same case for the same
    /// reason, and it is what this issue asks for in those words.
    /// </para>
    /// <para>
    /// The record is replaced rather than mutated. Every member of
    /// <see cref="ShareRecord"/> is init-only, so a revocation is a new record
    /// written over the old one, which is what stops a request that is holding
    /// the old object from seeing a field change under it mid-decision.
    /// </para>
    /// <para>
    /// The read and the write are one act, because they are done inside
    /// <see cref="IShareStore.MutateAsync"/>. Two administrators revoking two
    /// shares in the same moment cannot lose each other's work, which is the
    /// property a read followed by a plain write does not have.
    /// </para>
    /// </remarks>
    public static async Task<ShareRecord?> RevokeAsync(
        this IShareStore store,
        Guid shareId,
        Guid revokedByUserId,
        DateTimeOffset revokedAt,
        string? reason = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(store);

        ShareRecord? outcome = null;

        await store.MutateAsync(
            current =>
            {
                var next = new List<ShareRecord>(current.Count);
                for (var index = 0; index < current.Count; index++)
                {
                    var record = current[index];
                    if (record.Id != shareId)
                    {
                        next.Add(record);
                        continue;
                    }

                    if (record.RevokedAt is not null || !ShareBounds.IsLive(record, revokedAt))
                    {
                        // Already stopped, by revocation or by its own instant.
                        // Nothing to change, and the call still succeeds.
                        outcome = record;
                        next.Add(record);
                        continue;
                    }

                    outcome = Revoked(record, revokedByUserId, revokedAt, reason);
                    next.Add(outcome);
                }

                return next;
            },
            cancellationToken).ConfigureAwait(false);

        return outcome;
    }

    // The same record with the revocation written on it. Field by field rather
    // than by a copy helper, for the reason ShareRecord.Upgraded gives about
    // itself: a field added to the type and forgotten here would be dropped from
    // every record anybody revokes, and the suite compares the two sides rather
    // than trusting this list.
    private static ShareRecord Revoked(ShareRecord record, Guid revokedByUserId, DateTimeOffset revokedAt, string? reason) => new ShareRecord
    {
        // Stamped current rather than carried over. The revoker is a field the
        // current shape has, so a record written with one and stamped with an
        // older number would claim a shape it does not have. A store that
        // migrates on read hands this method a current record anyway; a store
        // that does not is where the difference would show, and there it is this
        // line that keeps the number and the fields agreeing.
        SchemaVersion = ShareRecord.CurrentSchemaVersion,
        Id = record.Id,
        ItemId = record.ItemId,
        InvitedUserIds = record.InvitedUserIds,
        PluginCreatedUserIds = record.PluginCreatedUserIds,
        CreatedByUserId = record.CreatedByUserId,
        CreatedAt = record.CreatedAt,
        ExpiresAt = record.ExpiresAt,
        RevokedAt = revokedAt,
        RevocationReason = reason,
        RevokedByUserId = revokedByUserId,
        MaxBitrateBitsPerSecond = record.MaxBitrateBitsPerSecond,
        TokenHash = record.TokenHash,
    };
}
