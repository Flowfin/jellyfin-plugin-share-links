using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Jellyfin.Plugin.ShareLinks;
using Xunit;

namespace Jellyfin.Plugin.ShareLinks.Tests;

/// <summary>
/// The list of personal data in <c>docs/personal-data.md</c> is the record's own
/// list, and removing a share really does take the guest out of the file (#31).
/// </summary>
/// <remarks>
/// <para>
/// A hand-written list of what a system holds about a person is complete on the day
/// it is written and never again. The failure is silent in both directions: a field
/// added to the record with no row here reads as a field nobody has to account for,
/// and a row left behind by a rename reads as data being kept that is not. So the
/// table is compared against <see cref="ShareRecord"/> rather than trusted, and both
/// directions are compared.
/// </para>
/// <para>
/// The removal test is the second clause of the issue and it is executed against a
/// real file. Asserting that a list in memory no longer holds a record proves
/// nothing about the bytes on disk, which is where the data actually is, so the file
/// is read back as text and searched.
/// </para>
/// <para>
/// What no test here reaches is the retention length. Ninety days is a decision the
/// page states and nothing in this tree yet reads; the setting is #71 and the sweep
/// is #29. The page says so about itself rather than leaving a green suite to imply
/// otherwise.
/// </para>
/// </remarks>
public sealed class PersonalDataTests : IDisposable
{
    private readonly string _directory;

    public PersonalDataTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "share-links-personal-data-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
    }

    /// <summary>
    /// Gets one case per field the record carries, so a field with no row fails
    /// under its own name rather than inside one assertion about two sets.
    /// </summary>
    public static TheoryData<string> RecordFields
    {
        get
        {
            var data = new TheoryData<string>();
            foreach (var field in Fields())
            {
                data.Add(field);
            }

            return data;
        }
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

    [Theory]
    [MemberData(nameof(RecordFields))]
    public void EveryFieldOfTheRecordHasARow(string field)
    {
        Assert.True(
            Rows().Any(row => string.Equals(row.Field, field, StringComparison.Ordinal)),
            $"ShareRecord.{field} is a field this plugin stores and docs/personal-data.md does not list it. "
            + "A field with no row is a field with no retention answer, which is what the issue is against.");
    }

    [Fact]
    public void EveryRowNamesAFieldTheRecordStillCarries()
    {
        var fields = Fields();
        var strays = Rows().Select(row => row.Field).Where(field => !fields.Contains(field)).ToArray();

        Assert.True(
            strays.Length == 0,
            "docs/personal-data.md lists " + string.Join(", ", strays)
            + " and ShareRecord carries no such field. The record moved under the page: "
            + string.Join(", ", fields));
    }

    [Fact]
    public void EveryRowCarriesARetentionAnswer()
    {
        var rows = Rows();
        Assert.True(rows.Count >= 10, $"the field table in docs/personal-data.md parsed to {rows.Count} rows");

        foreach (var row in rows)
        {
            Assert.True(
                row.Retention.Length > 0,
                $"the row for {row.Field} in docs/personal-data.md has no retention answer");
        }
    }

    [Fact]
    public async Task RemovingAShareLeavesNothingInTheFileNamingItsGuest()
    {
        var guest = Guid.NewGuid();
        var removed = ARecord(guest);
        var kept = ARecord(Guid.NewGuid());

        var path = Path.Combine(_directory, "shares.json");
        using var store = new ShareStore(path);

        await store.MutateAsync(_ => new[] { removed, kept });

        // The share is in the file before it is removed, or the assertion after the
        // removal would pass against a store that never held it.
        var before = await File.ReadAllTextAsync(path);
        Assert.Contains(guest.ToString(), before, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(removed.Id.ToString(), before, StringComparison.OrdinalIgnoreCase);

        await store.MutateAsync(records => records.Where(record => record.Id != removed.Id).ToArray());

        var after = await File.ReadAllTextAsync(path);
        Assert.DoesNotContain(guest.ToString(), after, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(removed.Id.ToString(), after, StringComparison.OrdinalIgnoreCase);

        // The other share is untouched, so the assertions above are about the record
        // that was removed rather than about a store that was emptied.
        Assert.Contains(kept.Id.ToString(), after, StringComparison.OrdinalIgnoreCase);
        Assert.Single(await store.ReadAsync());
    }

    private static ShareRecord ARecord(Guid guest) => new ShareRecord
    {
        SchemaVersion = ShareRecord.CurrentSchemaVersion,
        Id = Guid.NewGuid(),
        ItemId = Guid.NewGuid(),
        InvitedUserIds = new[] { guest },
        PluginCreatedUserIds = new[] { guest },
        CreatedByUserId = Guid.NewGuid(),
        CreatedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
        ExpiresAt = new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero),
        TokenHash = Guid.NewGuid().ToString("N"),
    };

    // Every public instance property, not only the ones declared on the type. A
    // field an operator's data ends up in is worth listing wherever on the hierarchy
    // it was written.
    private static IReadOnlyCollection<string> Fields() =>
        typeof(ShareRecord)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(property => property.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToHashSet(StringComparer.Ordinal);

    private static string Document()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "personal-data.md");
        Assert.True(File.Exists(path), $"personal-data.md was not copied next to the test assembly: {path}");
        return File.ReadAllText(path);
    }

    // A row is a table line whose first cell is a backticked identifier, which the
    // heading and the separator are not, so neither has to be skipped by position.
    private static IReadOnlyList<(string Field, string Identifies, string Retention)> Rows() =>
        Document()
            .Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.StartsWith('|'))
            .Select(line => line.Trim('|').Split('|').Select(cell => cell.Trim()).ToArray())
            .Where(cells => cells.Length >= 3)
            .Select(cells => (Cells: cells, Match: Regex.Match(cells[0], @"^`([A-Za-z][A-Za-z0-9]*)`$")))
            .Where(row => row.Match.Success)
            .Select(row => (row.Match.Groups[1].Value, row.Cells[1], row.Cells[2]))
            .ToList();
}
