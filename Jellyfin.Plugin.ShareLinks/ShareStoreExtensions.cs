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
}
