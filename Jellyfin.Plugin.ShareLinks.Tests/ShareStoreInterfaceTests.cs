using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.ShareLinks;
using Xunit;

namespace Jellyfin.Plugin.ShareLinks.Tests;

/// <summary>
/// Whether the store choice in <c>docs/share-store.md</c> could be revisited
/// without touching the callers (#34).
/// </summary>
/// <remarks>
/// <para>
/// An interface asserts that claim and does not demonstrate it. What
/// demonstrates it is a second implementation that shares no line of storage code
/// with the first, put through the same calls, and required to answer the same
/// way. The second implementation here keeps its records in a list, which is as
/// far from a file as this tree can get, so anything the callers were relying on
/// a file for shows up as a failure rather than as a comment.
/// </para>
/// <para>
/// The bound on that, stated rather than left to be assumed: this says the
/// callers do not depend on storage, and it says nothing about whether a second
/// implementation would be correct. Durability across a crash, and a write two
/// requests cannot interleave, are properties of an implementation and are
/// <see cref="ShareStoreTests"/>'s subject.
/// </para>
/// </remarks>
public class ShareStoreInterfaceTests
{
    private static readonly DateTimeOffset Now = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// Every store this suite holds, handed to a test as the interface and nothing more.
    /// </summary>
    /// <returns>One row per implementation.</returns>
    public static TheoryData<string> Implementations() => new TheoryData<string>
    {
        nameof(ShareStore),
        nameof(ListShareStore),
    };

    [Theory]
    [MemberData(nameof(Implementations))]
    public async Task AStoreThatHasNeverBeenWrittenReadsEmpty(string implementation)
    {
        using var owner = Open(implementation);

        Assert.Empty(await owner.Store.ReadAsync());
    }

    [Theory]
    [MemberData(nameof(Implementations))]
    public async Task WhatIsWrittenIsWhatIsRead(string implementation)
    {
        using var owner = Open(implementation);

        await owner.Store.MutateAsync(_ => new[] { ARecord(), ARecord() });

        Assert.Equal(2, (await owner.Store.ReadAsync()).Count);
    }

    [Theory]
    [MemberData(nameof(Implementations))]
    public async Task AChangeSeesWhatIsAlreadyThere(string implementation)
    {
        using var owner = Open(implementation);

        var first = ARecord();
        await owner.Store.MutateAsync(_ => new[] { first });
        await owner.Store.MutateAsync(existing => existing.Append(ARecord()).ToList());

        var records = await owner.Store.ReadAsync();
        Assert.Equal(2, records.Count);
        Assert.Contains(records, record => record.Id == first.Id);
    }

    /// <summary>
    /// The ceiling is over the interface, so both implementations are held to it
    /// without either of them holding a line of it.
    /// </summary>
    /// <param name="implementation">The store under test.</param>
    /// <returns>A task that completes when the assertion has been made.</returns>
    [Theory]
    [MemberData(nameof(Implementations))]
    public async Task TheCeilingRefusesTheCreateThatWouldPassIt(string implementation)
    {
        using var owner = Open(implementation);
        var bounds = new ShareBounds(maxLiveShares: 1, maxLiveSharesPerItem: 99, maxShareLifetimeDays: 30, expiredShareRetentionDays: 90);

        await owner.Store.AddAsync(ARecord(), bounds, Now);

        await Assert.ThrowsAsync<ShareBoundExceededException>(
            () => owner.Store.AddAsync(ARecord(), bounds, Now));

        Assert.Single(await owner.Store.ReadAsync());
    }

    /// <summary>
    /// The sweep is over the interface too, and a store that never had a file is
    /// swept by the same code that sweeps one.
    /// </summary>
    /// <param name="implementation">The store under test.</param>
    /// <returns>A task that completes when the assertion has been made.</returns>
    [Theory]
    [MemberData(nameof(Implementations))]
    public async Task TheSweepReachesEveryImplementationOnTheWayToAWrite(string implementation)
    {
        using var owner = Open(implementation);
        var bounds = new ShareBounds(maxLiveShares: 99, maxLiveSharesPerItem: 99, maxShareLifetimeDays: 30, expiredShareRetentionDays: 0);

        var dead = ARecord(expiresAt: Now.AddMinutes(1));
        await owner.Store.AddAsync(dead, bounds, Now);

        var later = Now.AddHours(1);
        await owner.Store.AddAsync(ARecord(createdAt: later, expiresAt: later.AddDays(1)), bounds, later);

        var left = await owner.Store.ReadAsync();
        Assert.Single(left);
        Assert.DoesNotContain(left, record => record.Id == dead.Id);
    }

    private static StoreUnderTest Open(string implementation)
    {
        if (implementation == nameof(ListShareStore))
        {
            return new StoreUnderTest(new ListShareStore(), null);
        }

        var directory = Directory.CreateTempSubdirectory("share-links-interface-");
        return new StoreUnderTest(new ShareStore(Path.Combine(directory.FullName, "shares.json")), directory);
    }

    private static ShareRecord ARecord(DateTimeOffset? createdAt = null, DateTimeOffset? expiresAt = null) => new ShareRecord
    {
        SchemaVersion = ShareRecord.CurrentSchemaVersion,
        Id = Guid.NewGuid(),
        ItemId = Guid.NewGuid(),
        InvitedUserIds = new[] { Guid.NewGuid() },
        CreatedByUserId = Guid.NewGuid(),
        CreatedAt = createdAt ?? Now,
        ExpiresAt = expiresAt ?? Now.AddDays(1),
        TokenHash = new string('a', ShareTokenHash.EncodedLength),
    };

    /// <summary>
    /// A store this suite owns for the length of one test, with whatever it has to
    /// clean up afterwards.
    /// </summary>
    private sealed class StoreUnderTest : IDisposable
    {
        private readonly DirectoryInfo? _directory;

        public StoreUnderTest(IShareStore store, DirectoryInfo? directory)
        {
            Store = store;
            _directory = directory;
        }

        public IShareStore Store { get; }

        public void Dispose()
        {
            (Store as IDisposable)?.Dispose();
            _directory?.Delete(recursive: true);
        }
    }

    /// <summary>
    /// A share store that is a list, sharing no line with the file one.
    /// </summary>
    /// <remarks>
    /// It is not a store anybody should ship. It holds nothing across a restart
    /// and it is one process's, which is exactly why it is useful here: every
    /// property the callers still have when they run against it is a property that
    /// came from the callers rather than from the file.
    /// </remarks>
    private sealed class ListShareStore : IShareStore, IDisposable
    {
        private readonly SemaphoreSlim _writers = new SemaphoreSlim(1, 1);
        private IReadOnlyList<ShareRecord> _records = Array.Empty<ShareRecord>();

        public void Dispose() => _writers.Dispose();

        public Task<IReadOnlyList<ShareRecord>> ReadAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(_records);

        public async Task<IReadOnlyList<ShareRecord>> MutateAsync(
            Func<IReadOnlyList<ShareRecord>, IReadOnlyList<ShareRecord>> change,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(change);

            await _writers.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                _records = change(_records) ?? throw new InvalidOperationException("The change returned no list.");
                return _records;
            }
            finally
            {
                _writers.Release();
            }
        }
    }
}
