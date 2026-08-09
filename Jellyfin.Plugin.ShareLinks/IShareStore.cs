using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.ShareLinks;

/// <summary>
/// Everything a caller may ask of the place share records are kept (#34).
/// </summary>
/// <remarks>
/// <para>
/// <c>docs/share-store.md</c> compares the three places the records could have
/// lived and chooses a file of the plugin's own under the plugin data folder.
/// This interface is the other half of that choice: the comparison it records is
/// only worth writing down if the answer can be revisited, and an answer welded
/// into every caller cannot be.
/// </para>
/// <para>
/// Two members, and the narrowness is the point rather than a style. A store that
/// offered a query language, or a delete, or a way to write one record, would be
/// a store the next implementation has to reproduce feature for feature before
/// anything compiles, which is the shape that makes a stored decision permanent.
/// What a caller genuinely needs is everything in the store, and a way to change
/// what is in the store without another writer landing in the middle of it.
/// </para>
/// <para>
/// What is deliberately not here is the ceiling and the sweep. Those are
/// <see cref="ShareStoreExtensions.AddAsync"/>, written once over this interface
/// rather than once per implementation, because a bound each implementation
/// reimplements is a bound two implementations can disagree about.
/// </para>
/// <para>
/// This says nothing about where the records sit or what they are written as. It
/// is also not an assurance that the choice is reversible in practice: a second
/// implementation still has to make writes that a crash cannot leave half done,
/// which is <see cref="ShareStore"/>'s subject and not this interface's.
/// </para>
/// </remarks>
public interface IShareStore
{
    /// <summary>
    /// Reads every record in the store.
    /// </summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The records, or an empty list when nothing has been written yet.</returns>
    /// <remarks>
    /// An implementation that cannot make sense of what it holds throws
    /// <see cref="ShareStoreUnreadableException"/> rather than returning an empty
    /// list. A store that lost its contents and a server nobody has shared
    /// anything on must not look the same to a caller.
    /// </remarks>
    Task<IReadOnlyList<ShareRecord>> ReadAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads the store, applies a change to what was read, and writes the result back.
    /// </summary>
    /// <param name="change">Takes the records currently in the store and returns the records that should replace them.</param>
    /// <param name="cancellationToken">Cancels the change.</param>
    /// <returns>The records that were written.</returns>
    /// <remarks>
    /// The read is part of this operation rather than something the caller does
    /// first. A caller that reads, decides, and then asks for a plain write has
    /// already lost whatever another writer did in between, and no lock around the
    /// write alone repairs that. An implementation that cannot hold other writers
    /// off for the duration is an implementation this interface's callers are
    /// entitled to assume it is not.
    /// </remarks>
    Task<IReadOnlyList<ShareRecord>> MutateAsync(
        Func<IReadOnlyList<ShareRecord>, IReadOnlyList<ShareRecord>> change,
        CancellationToken cancellationToken = default);
}
