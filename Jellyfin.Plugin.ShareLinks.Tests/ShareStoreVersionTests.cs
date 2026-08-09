using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using Jellyfin.Plugin.ShareLinks;
using Xunit;

namespace Jellyfin.Plugin.ShareLinks.Tests;

/// <summary>
/// What #37 asks of the store: a version of its own, a forward migration for what
/// an older version wrote, and a refusal rather than a guess for what a newer one
/// wrote.
/// </summary>
/// <remarks>
/// <para>
/// The failure this is written against is an operator upgrading the plugin over a
/// directory of records the previous version wrote. There are only three outcomes
/// and two of them are bad: the records are read, or they are lost, or they are read
/// wrongly. The last is the worst, because a record misread is a share resolving
/// under rules nobody wrote, and it is the one that leaves nothing in a log.
/// </para>
/// <para>
/// Every fixture here is written as text rather than produced by the store, which is
/// the point. A fixture the current code serialised is a fixture that agrees with
/// the current code by construction, and it would keep agreeing after somebody
/// changed both. These are the bytes an older version left on disk.
/// </para>
/// </remarks>
public sealed class ShareStoreVersionTests : IDisposable
{
    private readonly string _directory;

    public ShareStoreVersionTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "share-links-store-version-" + Guid.NewGuid().ToString("N"));
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

    [Fact]
    public async Task WhatThisPluginWritesCarriesTheStoreVersion()
    {
        using var store = new ShareStore(StorePath);
        await store.MutateAsync(_ => new[] { ARecord() });

        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(StorePath));

        Assert.Equal(JsonValueKind.Object, document.RootElement.ValueKind);
        Assert.Equal(ShareStore.CurrentStoreVersion, document.RootElement.GetProperty("StoreVersion").GetInt32());
        Assert.Single(document.RootElement.GetProperty("Shares").EnumerateArray());
    }

    [Fact]
    public async Task AStoreFromBeforeTheStampIsReadAndMigratedRatherThanRefused()
    {
        // The layout that shipped without a stamp: a bare array, and a record at
        // schema 1, which is what a store written before PluginCreatedUserIds
        // existed holds.
        await File.WriteAllTextAsync(StorePath, UnstampedStoreWithASchemaOneRecord);

        using var store = new ShareStore(StorePath);
        var records = await store.ReadAsync();

        var record = Assert.Single(records);

        // Read correctly: every field the old record carried is still there.
        Assert.Equal(Guid.Parse("11111111-1111-1111-1111-111111111111"), record.Id);
        Assert.Equal(Guid.Parse("22222222-2222-2222-2222-222222222222"), record.ItemId);
        Assert.Equal(new[] { Guid.Parse("33333333-3333-3333-3333-333333333333") }, record.InvitedUserIds);
        Assert.Equal(Guid.Parse("44444444-4444-4444-4444-444444444444"), record.CreatedByUserId);
        Assert.Equal(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), record.CreatedAt);
        Assert.Equal(new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero), record.ExpiresAt);
        Assert.Equal("an-old-hash", record.TokenHash);

        // Migrated: stamped at the current schema, with the field the old shape had
        // no room for reading as the absence #144 decided it means.
        Assert.Equal(ShareRecord.CurrentSchemaVersion, record.SchemaVersion);
        Assert.Empty(record.PluginCreatedUserIds);
    }

    [Fact]
    public async Task ReadingAnOlderStoreDoesNotRewriteIt()
    {
        await File.WriteAllTextAsync(StorePath, UnstampedStoreWithASchemaOneRecord);

        using var store = new ShareStore(StorePath);
        await store.ReadAsync();

        // A read that rewrote the file would migrate an operator's store on a
        // listing, which is a write nobody asked for on a path that is supposed to
        // take no lock at all.
        Assert.Equal(UnstampedStoreWithASchemaOneRecord, await File.ReadAllTextAsync(StorePath));
    }

    [Fact]
    public async Task TheNextWriteLeavesTheMigratedStoreInTheCurrentLayout()
    {
        await File.WriteAllTextAsync(StorePath, UnstampedStoreWithASchemaOneRecord);

        using var store = new ShareStore(StorePath);
        await store.MutateAsync(records => records);

        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(StorePath));

        Assert.Equal(ShareStore.CurrentStoreVersion, document.RootElement.GetProperty("StoreVersion").GetInt32());
        Assert.Equal(
            ShareRecord.CurrentSchemaVersion,
            document.RootElement.GetProperty("Shares").EnumerateArray().Single().GetProperty("SchemaVersion").GetInt32());
    }

    [Fact]
    public async Task AStoreFromANewerPluginIsRefusedAndSaysBothNumbers()
    {
        var newer = ShareStore.CurrentStoreVersion + 1;
        await File.WriteAllTextAsync(
            StorePath,
            "{\"StoreVersion\": " + newer.ToString(System.Globalization.CultureInfo.InvariantCulture) + ", \"Shares\": []}");

        using var store = new ShareStore(StorePath);

        var refused = await Assert.ThrowsAsync<ShareStoreUnreadableException>(() => store.ReadAsync());

        Assert.Contains(newer.ToString(System.Globalization.CultureInfo.InvariantCulture), refused.Message, StringComparison.Ordinal);
        Assert.Contains(ShareStore.CurrentStoreVersion.ToString(System.Globalization.CultureInfo.InvariantCulture), refused.Message, StringComparison.Ordinal);
        Assert.Contains(StorePath, refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ARecordFromANewerPluginIsRefusedEvenInAStoreThisVersionUnderstands()
    {
        var newer = ShareRecord.CurrentSchemaVersion + 1;
        await File.WriteAllTextAsync(StorePath, StampedStoreWithARecordAtSchema(newer));

        using var store = new ShareStore(StorePath);

        var refused = await Assert.ThrowsAsync<ShareStoreUnreadableException>(() => store.ReadAsync());

        Assert.Contains(newer.ToString(System.Globalization.CultureInfo.InvariantCulture), refused.Message, StringComparison.Ordinal);
        Assert.Contains(ShareRecord.CurrentSchemaVersion.ToString(System.Globalization.CultureInfo.InvariantCulture), refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnObjectWithNoStoreVersionIsRefusedRatherThanTakenForTheOldLayout()
    {
        // The old layout is an array. An object without a stamp is a file this code
        // cannot place, and reading it as version 0 would be the guess the refusal
        // exists against.
        await File.WriteAllTextAsync(StorePath, "{\"Shares\": []}");

        using var store = new ShareStore(StorePath);

        var refused = await Assert.ThrowsAsync<ShareStoreUnreadableException>(() => store.ReadAsync());

        Assert.Contains("no store version", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AFileThatIsNeitherAnArrayNorAnObjectIsRefused()
    {
        await File.WriteAllTextAsync(StorePath, "\"shares\"");

        using var store = new ShareStore(StorePath);

        await Assert.ThrowsAsync<ShareStoreUnreadableException>(() => store.ReadAsync());
    }

    [Fact]
    public void AnUpgradedRecordDiffersFromItsSourceInTheVersionAndNothingElse()
    {
        // The hazard the upgrade carries is a field added to ShareRecord and
        // forgotten in the copy, which drops that field from every migrated record
        // and leaves no trace. Comparing property by property is what refuses it,
        // rather than a list in this file that would be forgotten in the same edit.
        var source = ARecord();

        var upgraded = ShareRecord.Upgraded(source);

        Assert.Equal(ShareRecord.CurrentSchemaVersion, upgraded.SchemaVersion);

        var carried = typeof(ShareRecord)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.Name != nameof(ShareRecord.SchemaVersion))
            .ToList();

        Assert.NotEmpty(carried);

        foreach (var property in carried)
        {
            Assert.Equal(property.GetValue(source), property.GetValue(upgraded));
        }
    }

    private const string UnstampedStoreWithASchemaOneRecord = """
        [
          {
            "SchemaVersion": 1,
            "Id": "11111111-1111-1111-1111-111111111111",
            "ItemId": "22222222-2222-2222-2222-222222222222",
            "InvitedUserIds": ["33333333-3333-3333-3333-333333333333"],
            "CreatedByUserId": "44444444-4444-4444-4444-444444444444",
            "CreatedAt": "2026-01-01T00:00:00+00:00",
            "ExpiresAt": "2026-02-01T00:00:00+00:00",
            "TokenHash": "an-old-hash"
          }
        ]
        """;

    private static string StampedStoreWithARecordAtSchema(int schema) => """
        {
          "StoreVersion": 1,
          "Shares": [
            {
              "SchemaVersion": SCHEMA,
              "Id": "11111111-1111-1111-1111-111111111111",
              "ItemId": "22222222-2222-2222-2222-222222222222",
              "InvitedUserIds": ["33333333-3333-3333-3333-333333333333"],
              "CreatedByUserId": "44444444-4444-4444-4444-444444444444",
              "CreatedAt": "2026-01-01T00:00:00+00:00",
              "ExpiresAt": "2026-02-01T00:00:00+00:00",
              "TokenHash": "a-hash"
            }
          ]
        }
        """.Replace("SCHEMA", schema.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);

    private static readonly Guid[] Invited = [Guid.Parse("55555555-5555-5555-5555-555555555555")];

    private static ShareRecord ARecord() => new ShareRecord
    {
        SchemaVersion = ShareRecord.CurrentSchemaVersion,
        Id = Guid.NewGuid(),
        ItemId = Guid.NewGuid(),
        InvitedUserIds = Invited,
        PluginCreatedUserIds = Invited,
        CreatedByUserId = Guid.NewGuid(),
        CreatedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
        ExpiresAt = new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero),
        RevokedAt = new DateTimeOffset(2026, 1, 15, 0, 0, 0, TimeSpan.Zero),
        RevocationReason = "the operator changed their mind",
        MaxBitrateBitsPerSecond = 4_000_000,
        TokenHash = "a-hash",
    };
}
