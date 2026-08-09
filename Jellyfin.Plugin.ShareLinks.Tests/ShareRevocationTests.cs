using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Jellyfin.Plugin.ShareLinks;
using MediaBrowser.Model.Plugins;
using Xunit;

namespace Jellyfin.Plugin.ShareLinks.Tests;

/// <summary>
/// What #46 asks of revocation: that it stops the next request, that pressing it
/// twice is not an error, and that it records rather than deletes.
/// </summary>
/// <remarks>
/// <para>
/// The first of those is asserted against the decision routine rather than
/// against the field, because "the share stops working" is a statement about what
/// a request gets and not about what a record holds. A test that read
/// <c>RevokedAt</c> back would pass against a resolution path that never looked
/// at it.
/// </para>
/// <para>
/// Every assertion is against a real store in a real directory. Whether the
/// change survived is a question about the file, and only the file can answer it.
/// </para>
/// </remarks>
public sealed class ShareRevocationTests : IDisposable
{
    private static readonly DateTimeOffset Now = new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid Invited = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Operator = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly byte[] Key = Enumerable.Range(0, ShareTokenHash.MinimumKeyBytes).Select(b => (byte)b).ToArray();

    private readonly string _directory;

    public ShareRevocationTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "share-links-revocation-" + Guid.NewGuid().ToString("N"));
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

    /// <summary>
    /// The condition #46 leads with. The request before the revocation resolves,
    /// the request after it is refused, and nothing swept in between.
    /// </summary>
    /// <returns>A task that completes when the assertions have been made.</returns>
    [Fact]
    public async Task TheNextRequestAfterRevocationIsRefusedWithNoSweepHavingRun()
    {
        using var store = new ShareStore(StorePath);
        var share = ARecord();
        await store.MutateAsync(_ => new[] { share });

        var before = ShareResolution.Resolve(await store.ReadAsync(), Key, "a-token", Invited, PluginStatus.Active, At(Now));
        Assert.True(before.IsResolved);

        await store.RevokeAsync(share.Id, Operator, Now.AddMinutes(1));

        var after = ShareResolution.Resolve(await store.ReadAsync(), Key, "a-token", Invited, PluginStatus.Active, At(Now.AddMinutes(2)));
        Assert.Equal(ShareRefusal.Revoked, after.Refusal);

        // The record is still there. Revocation records; it does not delete, and
        // the sweep that would delete it is a different operation that this call
        // does not reach.
        var records = await store.ReadAsync();
        Assert.Single(records);
        Assert.Equal(share.Id, records[0].Id);
    }

    [Fact]
    public async Task RevokingRecordsWhenWhoAndWhy()
    {
        using var store = new ShareStore(StorePath);
        var share = ARecord();
        await store.MutateAsync(_ => new[] { share });

        var revoked = await store.RevokeAsync(share.Id, Operator, Now.AddMinutes(1), "shared with the wrong person");

        Assert.NotNull(revoked);
        Assert.Equal(Now.AddMinutes(1), revoked!.RevokedAt);
        Assert.Equal(Operator, revoked.RevokedByUserId);
        Assert.Equal("shared with the wrong person", revoked.RevocationReason);

        var reread = (await store.ReadAsync()).Single();
        Assert.Equal(Now.AddMinutes(1), reread.RevokedAt);
        Assert.Equal(Operator, reread.RevokedByUserId);
        Assert.Equal("shared with the wrong person", reread.RevocationReason);
    }

    /// <summary>
    /// The second condition. Pressing revoke again succeeds and leaves the record
    /// exactly as the first press left it, including the instant, so the audit
    /// trail says when the share stopped rather than when somebody last clicked.
    /// </summary>
    /// <returns>A task that completes when the assertions have been made.</returns>
    [Fact]
    public async Task RevokingTwiceSucceedsAndChangesNothing()
    {
        using var store = new ShareStore(StorePath);
        var share = ARecord();
        await store.MutateAsync(_ => new[] { share });

        await store.RevokeAsync(share.Id, Operator, Now.AddMinutes(1), "the first press");
        var afterFirst = await File.ReadAllTextAsync(StorePath);

        var second = await store.RevokeAsync(share.Id, Guid.NewGuid(), Now.AddHours(5), "the second press");

        Assert.NotNull(second);
        Assert.Equal(Now.AddMinutes(1), second!.RevokedAt);
        Assert.Equal(Operator, second.RevokedByUserId);
        Assert.Equal("the first press", second.RevocationReason);
        Assert.Equal(afterFirst, await File.ReadAllTextAsync(StorePath));
    }

    /// <summary>
    /// A share that has already expired is the same case: the call succeeds and
    /// the record is left alone, because the share had already stopped and the
    /// press did not stop it.
    /// </summary>
    /// <returns>A task that completes when the assertions have been made.</returns>
    [Fact]
    public async Task RevokingAnExpiredShareSucceedsAndChangesNothing()
    {
        using var store = new ShareStore(StorePath);
        var share = ARecord();
        await store.MutateAsync(_ => new[] { share });
        var before = await File.ReadAllTextAsync(StorePath);

        var result = await store.RevokeAsync(share.Id, Operator, Now.AddDays(30));

        Assert.NotNull(result);
        Assert.Null(result!.RevokedAt);
        Assert.Equal(before, await File.ReadAllTextAsync(StorePath));
    }

    [Fact]
    public async Task RevokingAShareTheStoreDoesNotHoldAnswersNothingAndLeavesTheRestAlone()
    {
        using var store = new ShareStore(StorePath);
        var share = ARecord();
        await store.MutateAsync(_ => new[] { share });

        var result = await store.RevokeAsync(Guid.NewGuid(), Operator, Now.AddMinutes(1));

        Assert.Null(result);
        Assert.Null((await store.ReadAsync()).Single().RevokedAt);
    }

    [Fact]
    public async Task RevokingOneShareLeavesEveryOtherShareAlone()
    {
        using var store = new ShareStore(StorePath);
        var wanted = ARecord();
        var neighbour = ARecord(token: "another-token");
        await store.MutateAsync(_ => new[] { neighbour, wanted });

        await store.RevokeAsync(wanted.Id, Operator, Now.AddMinutes(1));

        var records = await store.ReadAsync();
        Assert.Equal(2, records.Count);
        Assert.Null(records.Single(record => record.Id == neighbour.Id).RevokedAt);
        Assert.NotNull(records.Single(record => record.Id == wanted.Id).RevokedAt);
    }

    /// <summary>
    /// The record is replaced rather than edited, which is what stops a request
    /// holding the old object from seeing a field move under it mid-decision.
    /// </summary>
    /// <returns>A task that completes when the assertion has been made.</returns>
    [Fact]
    public async Task RevocationWritesANewRecordOverTheOldOne()
    {
        using var store = new ShareStore(StorePath);
        var share = ARecord();
        await store.MutateAsync(_ => new[] { share });

        var revoked = await store.RevokeAsync(share.Id, Operator, Now.AddMinutes(1));

        Assert.NotSame(share, revoked);
        Assert.Null(share.RevokedAt);
    }

    /// <summary>
    /// Revocation carries every other field across untouched. The hazard is a
    /// field added to the record and forgotten in the copy, so this compares the
    /// two sides rather than naming the fields it expects to survive.
    /// </summary>
    /// <returns>A task that completes when the assertion has been made.</returns>
    [Fact]
    public async Task RevocationChangesOnlyWhatRevocationIsAbout()
    {
        using var store = new ShareStore(StorePath);
        var share = ARecord();
        await store.MutateAsync(_ => new[] { share });

        var revoked = await store.RevokeAsync(share.Id, Operator, Now.AddMinutes(1), "why");

        var untouched = typeof(ShareRecord).GetProperties()
            .Where(property => property.Name is not (nameof(ShareRecord.RevokedAt)
                or nameof(ShareRecord.RevocationReason)
                or nameof(ShareRecord.RevokedByUserId)));

        foreach (var property in untouched)
        {
            Assert.Equal(property.GetValue(share), property.GetValue(revoked));
        }
    }

    /// <summary>
    /// A share revoked before the revoker was a field is still revoked. The
    /// silence is read as "not written down" and never as "not revoked", which is
    /// the reading that would bring a revoked share back on an upgrade.
    /// </summary>
    /// <returns>A task that completes when the assertions have been made.</returns>
    [Fact]
    public async Task AShareRevokedBeforeTheRevokerWasAFieldIsStillRevoked()
    {
        var storedValue = ShareTokenHash.Compute(Key, "a-token");
        var atTheOldVersion = $$"""
            {
              "StoreVersion": 1,
              "Shares": [
                {
                  "SchemaVersion": 2,
                  "Id": "44444444-4444-4444-4444-444444444444",
                  "ItemId": "55555555-5555-5555-5555-555555555555",
                  "InvitedUserIds": ["11111111-1111-1111-1111-111111111111"],
                  "CreatedByUserId": "33333333-3333-3333-3333-333333333333",
                  "CreatedAt": "2026-05-31T12:00:00+00:00",
                  "ExpiresAt": "2026-06-08T12:00:00+00:00",
                  "RevokedAt": "2026-06-01T12:01:00+00:00",
                  "RevocationReason": "revoked before the field existed",
                  "TokenHash": "{{storedValue}}"
                }
              ]
            }
            """;

        await File.WriteAllTextAsync(StorePath, atTheOldVersion);
        using var store = new ShareStore(StorePath);

        var migrated = (await store.ReadAsync()).Single();
        Assert.Equal(ShareRecord.CurrentSchemaVersion, migrated.SchemaVersion);
        Assert.NotNull(migrated.RevokedAt);
        Assert.Null(migrated.RevokedByUserId);

        var resolved = ShareResolution.Resolve(await store.ReadAsync(), Key, "a-token", Invited, PluginStatus.Active, At(Now));
        Assert.Equal(ShareRefusal.Revoked, resolved.Refusal);
    }

    private static ShareRecord ARecord(string token = "a-token") => new ShareRecord
    {
        SchemaVersion = ShareRecord.CurrentSchemaVersion,
        Id = Guid.NewGuid(),
        ItemId = Guid.NewGuid(),
        InvitedUserIds = new[] { Invited },
        CreatedByUserId = Guid.NewGuid(),
        CreatedAt = Now.AddDays(-1),
        ExpiresAt = Now.AddDays(7),
        MaxBitrateBitsPerSecond = 4_000_000,
        TokenHash = ShareTokenHash.Compute(Key, token),
    };

    private static TimeProvider At(DateTimeOffset instant) => new FixedClock(instant);

    private sealed class FixedClock : TimeProvider
    {
        private readonly DateTimeOffset _instant;

        public FixedClock(DateTimeOffset instant) => _instant = instant;

        public override DateTimeOffset GetUtcNow() => _instant;
    }
}
