using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Jellyfin.Plugin.ShareLinks;
using MediaBrowser.Model.Plugins;
using Xunit;
using Microsoft.Extensions.Logging.Abstractions;

namespace Jellyfin.Plugin.ShareLinks.Tests;

/// <summary>
/// What a restored backup does to live shares (#40), as the three cases
/// <c>docs/backup-restore.md</c> names.
/// </summary>
/// <remarks>
/// <para>
/// A restore is two files moving back in time while nothing else does, so it is
/// modelled here by moving those two files. The store and the key are copied
/// aside, the world is allowed to change, and the copies are put back. That is
/// what a restore does to this plugin, and it needs no server and no backup tool
/// to do it.
/// </para>
/// <para>
/// What it is not is a real restore. A server backup tool takes more than these
/// two files and puts them back in an order this test does not model, and no
/// claim about one is made here. <c>docs/testing.md</c> is the rule that keeps
/// the suite off a running server.
/// </para>
/// <para>
/// Every test creates its own directory under the temporary directory and removes
/// it, so no two of them share a path.
/// </para>
/// </remarks>
public sealed class BackupRestoreTests : IDisposable
{
    private static readonly DateTimeOffset Created = new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Expires = new DateTimeOffset(2026, 7, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset WhileLive = new DateTimeOffset(2026, 6, 15, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset AfterExpiry = new DateTimeOffset(2026, 8, 1, 12, 0, 0, TimeSpan.Zero);

    private static readonly Guid Invited = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Operator = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private readonly string _directory;

    public BackupRestoreTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "share-links-restore-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // A leftover directory under the temporary directory is not worth
            // failing a green suite over.
        }
    }

    private string StorePath => Path.Combine(_directory, "shares.json");

    private string KeyPath => Path.Combine(_directory, "share-key");

    private string BackupOf(string path) => path + ".backup";

    /// <summary>
    /// The first case. A store from before a revocation, restored under the key
    /// it was written with, brings the revoked share back alive.
    /// </summary>
    /// <remarks>
    /// This asserts what happens rather than that something is refused, because
    /// nothing here can refuse: the restored store is a valid store and there is
    /// no second place that remembers the revocation. The answer to it is
    /// operator guidance, which is why the page exists.
    /// </remarks>
    [Fact]
    public async Task ARestoredStoreBringsBackAShareThatWasRevokedAfterTheBackup()
    {
        var key = new ShareKeyFile(KeyPath);
        var store = new ShareStore(StorePath);
        var token = ShareTokens.Mint();
        var share = ARecord(ShareTokenHash.Compute(key.Read(), token));

        await store.MutateAsync(_ => new[] { share });
        Take(StorePath);

        Assert.NotNull(await store.RevokeAsync(share.Id, Operator, WhileLive, NullLogger.Instance, "sent to the wrong person"));
        Assert.Equal(ShareRefusal.Revoked, Resolve(await store.ReadAsync(), key, token).Refusal);

        Restore(StorePath);

        var afterRestore = ShareResolution.Resolve(
            await store.ReadAsync(),
            key,
            token,
            Invited,
            PluginStatus.Active,
            At(WhileLive));

        Assert.True(afterRestore.IsResolved);
        Assert.Equal(share.Id, afterRestore.Share?.Id);
    }

    /// <summary>
    /// The same case seen from the store rather than from the decision: the
    /// record that came back carries no revocation, so nothing downstream of it
    /// can tell that one ever happened.
    /// </summary>
    [Fact]
    public async Task TheRestoredRecordCarriesNoTraceOfTheRevocationItPredates()
    {
        var key = new ShareKeyFile(KeyPath);
        var store = new ShareStore(StorePath);
        var share = ARecord(ShareTokenHash.Compute(key.Read(), ShareTokens.Mint()));

        await store.MutateAsync(_ => new[] { share });
        Take(StorePath);
        await store.RevokeAsync(share.Id, Operator, WhileLive, NullLogger.Instance, "sent to the wrong person");
        Restore(StorePath);

        var restored = Assert.Single(await store.ReadAsync());

        Assert.Null(restored.RevokedAt);
        Assert.Null(restored.RevokedByUserId);
        Assert.Null(restored.RevocationReason);
    }

    /// <summary>
    /// The second case, and the one with a guard behind it. The key comes back
    /// from a backup and the store is the one from now, so every hash in it was
    /// computed under a key that is gone.
    /// </summary>
    [Fact]
    public async Task AStoreRestoredWithoutItsKeyResolvesNothing()
    {
        var key = new ShareKeyFile(KeyPath);
        var store = new ShareStore(StorePath);

        // The key of the day the backup was taken, kept aside before anything is
        // issued under the key that replaces it.
        key.Read();
        Take(KeyPath);
        key.Rotate(liveShares: 0);

        var token = ShareTokens.Mint();
        await store.MutateAsync(_ => new[] { ARecord(ShareTokenHash.Compute(key.Read(), token)) });
        Assert.True(Resolve(await store.ReadAsync(), key, token).IsResolved);

        Restore(KeyPath);

        Assert.Equal(ShareRefusal.NoSuchShare, Resolve(await store.ReadAsync(), key, token).Refusal);
    }

    /// <summary>
    /// The refusal in that case says no more than a token naming nothing says,
    /// which is <c>docs/leaked-link.md</c>'s property rather than a nicety.
    /// </summary>
    [Fact]
    public async Task AKeyThatDoesNotMatchTheStoreIsNotDistinguishableFromATokenNamingNothing()
    {
        var key = new ShareKeyFile(KeyPath);
        var store = new ShareStore(StorePath);

        key.Read();
        Take(KeyPath);
        key.Rotate(liveShares: 0);

        var issued = ShareTokens.Mint();
        await store.MutateAsync(_ => new[] { ARecord(ShareTokenHash.Compute(key.Read(), issued)) });

        Restore(KeyPath);
        var records = await store.ReadAsync();

        var real = Resolve(records, key, issued);
        var invented = Resolve(records, key, ShareTokens.Mint());

        Assert.Equal(invented.Refusal, real.Refusal);
        Assert.Null(real.Share);
        Assert.Null(invented.Share);
    }

    /// <summary>
    /// Reading a store under a key that matches none of it changes neither of
    /// them. A plugin that minted a fresh key here would have destroyed the only
    /// thing that could still resolve those records.
    /// </summary>
    [Fact]
    public async Task AKeyThatMatchesNothingIsNotReplacedAndTheStoreIsNotRewritten()
    {
        var key = new ShareKeyFile(KeyPath);
        var store = new ShareStore(StorePath);

        key.Read();
        Take(KeyPath);
        key.Rotate(liveShares: 0);
        await store.MutateAsync(_ => new[] { ARecord(ShareTokenHash.Compute(key.Read(), ShareTokens.Mint())) });

        Restore(KeyPath);
        var keyAsRestored = await File.ReadAllBytesAsync(KeyPath);
        var storeAsWritten = await File.ReadAllTextAsync(StorePath);

        Resolve(await store.ReadAsync(), key, ShareTokens.Mint());

        Assert.Equal(keyAsRestored, await File.ReadAllBytesAsync(KeyPath));
        Assert.Equal(storeAsWritten, await File.ReadAllTextAsync(StorePath));
    }

    /// <summary>
    /// The third case. Expiry is an instant, so a restore does not move it.
    /// </summary>
    [Fact]
    public async Task SharesThatHaveExpiredAreStillExpiredAfterTheStoreIsRestored()
    {
        var key = new ShareKeyFile(KeyPath);
        var store = new ShareStore(StorePath);
        var token = ShareTokens.Mint();

        await store.MutateAsync(_ => new[] { ARecord(ShareTokenHash.Compute(key.Read(), token)) });
        Take(StorePath);
        await store.MutateAsync(_ => Array.Empty<ShareRecord>());
        Restore(StorePath);

        var records = await store.ReadAsync();

        // Live against a clock before the instant, and expired against one after
        // it, out of the same restored bytes.
        Assert.True(ShareResolution.Resolve(records, key, token, Invited, PluginStatus.Active, At(WhileLive)).IsResolved);
        Assert.Equal(
            ShareRefusal.Expired,
            ShareResolution.Resolve(records, key, token, Invited, PluginStatus.Active, At(AfterExpiry)).Refusal);
    }

    private static ShareRecord ARecord(string tokenHash) => new ShareRecord
    {
        SchemaVersion = ShareRecord.CurrentSchemaVersion,
        Id = Guid.NewGuid(),
        ItemId = Guid.NewGuid(),
        InvitedUserIds = new[] { Invited },
        CreatedByUserId = Operator,
        CreatedAt = Created,
        ExpiresAt = Expires,
        TokenHash = tokenHash,
    };

    private static TimeProvider At(DateTimeOffset instant) => new FixedClock(instant);

    private static ShareResolutionResult Resolve(IReadOnlyList<ShareRecord> records, ShareKeyFile key, string token)
        => ShareResolution.Resolve(records, key, token, Invited, PluginStatus.Active, At(WhileLive));

    private void Take(string path) => File.Copy(path, BackupOf(path), overwrite: true);

    private void Restore(string path) => File.Copy(BackupOf(path), path, overwrite: true);

    private sealed class FixedClock : TimeProvider
    {
        private readonly DateTimeOffset _instant;

        public FixedClock(DateTimeOffset instant) => _instant = instant;

        public override DateTimeOffset GetUtcNow() => _instant;
    }
}
