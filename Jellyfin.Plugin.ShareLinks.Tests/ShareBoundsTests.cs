using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml.Serialization;
using Jellyfin.Plugin.ShareLinks;
using Jellyfin.Plugin.ShareLinks.Configuration;
using Xunit;
using Microsoft.Extensions.Logging.Abstractions;

namespace Jellyfin.Plugin.ShareLinks.Tests;

/// <summary>
/// What #29 asks: each bound has a configured default, exceeding one is refused
/// with a message naming the bound, and a create past a ceiling produces the
/// refusal rather than the growth.
/// </summary>
/// <remarks>
/// <para>
/// The growth half is asserted against a real store in a real directory rather
/// than against the routine that computes the refusal. A refusal that leaves the
/// record in the file is the failure this issue is about, and only the file can
/// say whether it did.
/// </para>
/// <para>
/// No test here sleeps and none reads a clock. The instant is a parameter, so
/// standing one tick either side of a boundary costs nothing.
/// </para>
/// </remarks>
public sealed class ShareBoundsTests : IDisposable
{
    private static readonly DateTimeOffset Now = new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);

    private readonly string _directory;

    public ShareBoundsTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "share-links-bounds-" + Guid.NewGuid().ToString("N"));
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

    private static ShareBounds Bounds(
        int maxLiveShares = 3,
        int maxLiveSharesPerItem = 2,
        int maxShareLifetimeDays = 30,
        int expiredShareRetentionDays = 90)
        => new ShareBounds(maxLiveShares, maxLiveSharesPerItem, maxShareLifetimeDays, expiredShareRetentionDays);

    // Small numbers on purpose. A test that has to write a hundred records to
    // reach a ceiling is testing the file system, and the ceiling being a
    // configured value is exactly what lets the test pick one it can reach.
    private static ShareRecord ARecord(
        Guid itemId,
        DateTimeOffset? expiresAt = null,
        DateTimeOffset? revokedAt = null,
        DateTimeOffset? createdAt = null)
        => new ShareRecord
        {
            SchemaVersion = ShareRecord.CurrentSchemaVersion,
            Id = Guid.NewGuid(),
            ItemId = itemId,
            InvitedUserIds = new[] { Guid.NewGuid() },
            CreatedByUserId = Guid.NewGuid(),
            CreatedAt = createdAt ?? Now,
            ExpiresAt = expiresAt ?? Now.AddDays(7),
            RevokedAt = revokedAt,
            TokenHash = Guid.NewGuid().ToString("N"),
        };

    [Fact]
    public void EveryBoundHasADefaultOnTheConfiguration()
    {
        var fresh = new PluginConfiguration();

        Assert.Equal(ShareBounds.DefaultMaxLiveShares, fresh.MaxLiveShares);
        Assert.Equal(ShareBounds.DefaultMaxLiveSharesPerItem, fresh.MaxLiveSharesPerItem);
        Assert.Equal(ShareBounds.DefaultMaxShareLifetimeDays, fresh.MaxShareLifetimeDays);
        Assert.Equal(ShareBounds.DefaultExpiredShareRetentionDays, fresh.ExpiredShareRetentionDays);

        var bounds = ShareBounds.From(fresh);

        Assert.Equal(ShareBounds.DefaultMaxLiveShares, bounds.MaxLiveShares);
        Assert.Equal(ShareBounds.DefaultMaxLiveSharesPerItem, bounds.MaxLiveSharesPerItem);
        Assert.Equal(TimeSpan.FromDays(ShareBounds.DefaultMaxShareLifetimeDays), bounds.MaxShareLifetime);
        Assert.Equal(TimeSpan.FromDays(ShareBounds.DefaultExpiredShareRetentionDays), bounds.ExpiredShareRetention);
    }

    [Fact]
    public void TheBoundsSurviveTheSerialiserTheServerUses()
    {
        // The server writes this class out with XmlSerializer and reads it back on
        // the next start. A ceiling that does not round-trip is a ceiling an
        // operator raises once and finds back at its default after a restart,
        // which is a refusal they cannot explain.
        var serialiser = new XmlSerializer(typeof(PluginConfiguration));
        using var written = new StringWriter(CultureInfo.InvariantCulture);
        serialiser.Serialize(
            written,
            new PluginConfiguration
            {
                MaxLiveShares = 7,
                MaxLiveSharesPerItem = 5,
                MaxShareLifetimeDays = 3,
                ExpiredShareRetentionDays = 0,
            });

        using var read = new StringReader(written.ToString());
        var restored = Assert.IsType<PluginConfiguration>(serialiser.Deserialize(read));

        Assert.Equal(7, restored.MaxLiveShares);
        Assert.Equal(5, restored.MaxLiveSharesPerItem);
        Assert.Equal(3, restored.MaxShareLifetimeDays);
        Assert.Equal(0, restored.ExpiredShareRetentionDays);
    }

    [Theory]
    [InlineData(0, 1, 1, 0, "MaxLiveShares")]
    [InlineData(1, 0, 1, 0, "MaxLiveSharesPerItem")]
    [InlineData(1, 1, 0, 0, "MaxShareLifetimeDays")]
    [InlineData(1, 1, 1, -1, "ExpiredShareRetentionDays")]
    public void AValueOutsideWhatTheSettingAdmitsIsRefusedByName(int live, int perItem, int lifetime, int retention, string setting)
    {
        var refusal = Assert.Throws<ArgumentOutOfRangeException>(
            () => new ShareBounds(live, perItem, lifetime, retention));

        Assert.Equal(setting, refusal.ParamName);
    }

    [Fact]
    public async Task ACreatePastTheServerCeilingIsRefusedAndTheStoreDoesNotGrow()
    {
        var store = new ShareStore(StorePath);
        var bounds = Bounds(maxLiveShares: 3, maxLiveSharesPerItem: 99);

        for (var made = 0; made < 3; made++)
        {
            await store.AddAsync(ARecord(Guid.NewGuid()), bounds, Now, NullLogger.Instance);
        }

        var refusal = await Assert.ThrowsAsync<ShareBoundExceededException>(
            () => store.AddAsync(ARecord(Guid.NewGuid()), bounds, Now, NullLogger.Instance));

        Assert.Contains("MaxLiveShares", refusal.Message, StringComparison.Ordinal);
        Assert.Equal(3, (await store.ReadAsync()).Count);
    }

    [Fact]
    public async Task ACreatePastTheItemCeilingIsRefusedAndTheStoreDoesNotGrow()
    {
        var store = new ShareStore(StorePath);
        var bounds = Bounds(maxLiveShares: 99, maxLiveSharesPerItem: 2);
        var item = Guid.NewGuid();

        await store.AddAsync(ARecord(item), bounds, Now, NullLogger.Instance);
        await store.AddAsync(ARecord(item), bounds, Now, NullLogger.Instance);

        var refusal = await Assert.ThrowsAsync<ShareBoundExceededException>(
            () => store.AddAsync(ARecord(item), bounds, Now, NullLogger.Instance));

        Assert.Contains("MaxLiveSharesPerItem", refusal.Message, StringComparison.Ordinal);
        Assert.Equal(2, (await store.ReadAsync()).Count);

        // The ceiling is per item and not a second server ceiling wearing its
        // name, so another item is still admitted at the same instant.
        await store.AddAsync(ARecord(Guid.NewGuid()), bounds, Now, NullLogger.Instance);
        Assert.Equal(3, (await store.ReadAsync()).Count);
    }

    [Fact]
    public async Task ACreatePastTheLifetimeCeilingIsRefusedAndTheStoreDoesNotGrow()
    {
        var store = new ShareStore(StorePath);
        var bounds = Bounds(maxShareLifetimeDays: 30);

        var refusal = await Assert.ThrowsAsync<ShareBoundExceededException>(
            () => store.AddAsync(ARecord(Guid.NewGuid(), expiresAt: Now.AddDays(31)), bounds, Now, NullLogger.Instance));

        Assert.Contains("MaxShareLifetimeDays", refusal.Message, StringComparison.Ordinal);
        Assert.Empty(await store.ReadAsync());

        // Exactly the ceiling is admitted. The refusal is of a lifetime longer
        // than the ceiling, and a bound nobody may reach is a different bound.
        await store.AddAsync(ARecord(Guid.NewGuid(), expiresAt: Now.AddDays(30)), bounds, Now, NullLogger.Instance);
        Assert.Single(await store.ReadAsync());
    }

    [Fact]
    public void ARecordThatNoLongerAnswersDoesNotHoldAPlace()
    {
        var bounds = Bounds(maxLiveShares: 2, maxLiveSharesPerItem: 2);
        var item = Guid.NewGuid();

        var existing = new[]
        {
            ARecord(item, expiresAt: Now),                        // expired at this instant
            ARecord(item, revokedAt: Now.AddDays(-1)),            // revoked
        };

        Assert.Null(bounds.Refuse(existing, ARecord(item), Now));
    }

    [Fact]
    public void TheBoundaryInstantIsNotLive()
    {
        var record = ARecord(Guid.NewGuid(), expiresAt: Now);

        Assert.True(ShareBounds.IsLive(record, Now.AddTicks(-1)));
        Assert.False(ShareBounds.IsLive(record, Now));
        Assert.False(ShareBounds.IsLive(record, Now.AddTicks(1)));
    }

    [Fact]
    public void RetentionKeepsWhatStoppedInsideTheWindowAndDropsWhatDidNot()
    {
        var bounds = Bounds(expiredShareRetentionDays: 90);
        var item = Guid.NewGuid();

        var live = ARecord(item, expiresAt: Now.AddDays(1));
        var justInside = ARecord(item, expiresAt: Now.AddDays(-89));
        var exactlyOut = ARecord(item, expiresAt: Now.AddDays(-90));
        var longGone = ARecord(item, expiresAt: Now.AddDays(-400));

        var kept = bounds.Retained(new[] { live, justInside, exactlyOut, longGone }, Now);

        Assert.Equal(
            new[] { live.Id, justInside.Id },
            kept.Select(record => record.Id).ToArray());
    }

    [Fact]
    public void RetentionDatesFromWhenTheShareStoppedAndNotFromTheLaterRevocation()
    {
        var bounds = Bounds(expiredShareRetentionDays: 90);

        // Expired one hundred days ago and revoked yesterday. It stopped working
        // when it expired, so retention is already past.
        var record = ARecord(
            Guid.NewGuid(),
            expiresAt: Now.AddDays(-100),
            revokedAt: Now.AddDays(-1));

        Assert.Empty(bounds.Retained(new[] { record }, Now));
    }

    [Fact]
    public async Task ARetentionOfZeroEmptiesWhatHasStoppedWorkingAtTheNextWrite()
    {
        var store = new ShareStore(StorePath);
        var keeping = Bounds(expiredShareRetentionDays: 90);
        var emptying = Bounds(expiredShareRetentionDays: 0);

        var dead = ARecord(Guid.NewGuid(), expiresAt: Now.AddDays(1));
        await store.AddAsync(dead, keeping, Now, NullLogger.Instance);
        Assert.Single(await store.ReadAsync());

        // A day after it expired, with retention set to nothing, the next create
        // is what takes it out.
        var later = Now.AddDays(2);
        await store.AddAsync(ARecord(Guid.NewGuid(), createdAt: later, expiresAt: later.AddDays(1)), emptying, later, NullLogger.Instance);

        var left = await store.ReadAsync();
        Assert.Single(left);
        Assert.DoesNotContain(left, record => record.Id == dead.Id);
    }

    [Fact]
    public async Task TheSweepRunsBeforeTheCeilingIsCounted()
    {
        var store = new ShareStore(StorePath);
        var bounds = Bounds(maxLiveShares: 2, maxLiveSharesPerItem: 2, expiredShareRetentionDays: 1);

        var first = ARecord(Guid.NewGuid(), expiresAt: Now.AddDays(1));
        var second = ARecord(Guid.NewGuid(), expiresAt: Now.AddDays(1));
        await store.AddAsync(first, bounds, Now, NullLogger.Instance);
        await store.AddAsync(second, bounds, Now, NullLogger.Instance);

        // Both have expired and both are past retention, so a third create is
        // admitted and what it leaves behind is one record rather than three.
        var later = Now.AddDays(3);
        await store.AddAsync(ARecord(Guid.NewGuid(), createdAt: later, expiresAt: later.AddDays(1)), bounds, later, NullLogger.Instance);

        Assert.Single(await store.ReadAsync());
    }

    [Fact]
    public void TheRefusalNamesTheNumberAsWellAsTheSetting()
    {
        var bounds = Bounds(maxLiveShares: 1, maxLiveSharesPerItem: 99);
        var existing = new[] { ARecord(Guid.NewGuid()) };

        var refusal = bounds.Refuse(existing, ARecord(Guid.NewGuid()), Now);

        Assert.NotNull(refusal);
        Assert.Contains("MaxLiveShares is 1", refusal, StringComparison.Ordinal);
    }

    [Fact]
    public void TheDocumentStatesTheDefaultsTheCodeHolds()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "bounds.md");
        Assert.True(File.Exists(path), $"bounds.md was not copied next to the test assembly: {path}");
        // Whitespace collapsed first. The document is wrapped, so "one hundred"
        // is one word at the end of a line and one at the start of the next, and
        // a match against the raw bytes would depend on where the wrapping fell.
        var document = Regex.Replace(File.ReadAllText(path), @"\s+", " ");

        // The values live in docs/configuration.md, which the reference tests
        // compare against the class. What this refuses is the prose in bounds.md
        // arguing for a number the code no longer holds, which is the way a
        // document that explains a value goes wrong.
        foreach (var spelled in new[]
                 {
                     Spell(ShareBounds.DefaultMaxLiveShares),
                     Spell(ShareBounds.DefaultMaxLiveSharesPerItem),
                     Spell(ShareBounds.DefaultMaxShareLifetimeDays),
                     Spell(ShareBounds.DefaultExpiredShareRetentionDays),
                 })
        {
            Assert.Contains(spelled, document, StringComparison.Ordinal);
        }
    }

    // The numbers are argued in words rather than in digits, because a paragraph
    // reads better that way and because "10" matches "100".
    private static string Spell(int value) => value switch
    {
        10 => "ten",
        30 => "thirty",
        90 => "ninety",
        100 => "one hundred",
        _ => value.ToString(CultureInfo.InvariantCulture),
    };
}
