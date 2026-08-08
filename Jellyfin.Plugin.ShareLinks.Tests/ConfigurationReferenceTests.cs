using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Jellyfin.Plugin.ShareLinks.Configuration;
using Xunit;

namespace Jellyfin.Plugin.ShareLinks.Tests;

/// <summary>
/// A configuration reference written by hand drifts the first time somebody adds a
/// setting and stops before the documentation (#85). Nothing about that is visible:
/// the build is green, the plugin runs, and an operator reading the reference sees a
/// complete list because a list looks complete whatever it leaves out. These tests
/// are what refuses it, in both directions, because a row for a setting that was
/// renamed away is the same defect wearing the other face and is the one that
/// survives a rename.
/// </summary>
/// <remarks>
/// <para>
/// What they judge is the setting names and the stated defaults. The meaning, the
/// unit, the bounds and the empty-value answer are prose in the same rows and
/// nothing here reads them, which the document says about itself so a reader does
/// not take a green suite for a judgement on the whole table.
/// </para>
/// </remarks>
public class ConfigurationReferenceTests
{
    private static string Document()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "configuration.md");
        Assert.True(File.Exists(path), $"configuration.md was not copied next to the test assembly: {path}");
        return File.ReadAllText(path);
    }

    // A row is a table line whose first cell is a backticked identifier. The
    // heading and the separator are not, so neither has to be skipped by position,
    // which is what breaks when somebody puts a paragraph above the table.
    private static IReadOnlyList<string[]> Rows()
    {
        var rows = Document()
            .Split('\n')
            .Select(line => line.Trim())
            .Where(line => Regex.IsMatch(line, @"^\|\s*`[A-Za-z][A-Za-z0-9_]*`\s*\|"))
            .Select(line => line.Trim('|').Split('|').Select(cell => cell.Trim()).ToArray())
            .ToList();

        // Without this every test below passes on a document with no table in it,
        // and the class having no properties would not save it either, since two
        // empty sets agree.
        Assert.True(rows.Count >= 1, "the settings table parsed to no rows at all");
        return rows;
    }

    private static string Setting(string[] row) => row[0].Trim('`');

    private static string StatedDefault(string[] row)
    {
        Assert.True(row.Length >= 4, $"the row for {Setting(row)} has {row.Length} cells and no default column");
        return row[3].Trim('`');
    }

    // Every public instance property, not only the ones declared here. A setting an
    // operator can change is a setting worth documenting wherever on the hierarchy
    // it was written, and reading only the declared ones would let a base class
    // supply an undocumented one.
    private static IReadOnlyList<PropertyInfo> Settings() =>
        typeof(PluginConfiguration)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .OrderBy(property => property.Name, StringComparer.Ordinal)
            .ToList();

    // The default as C# spells it, so the document holds a literal a reader can
    // compare against the source rather than a description of one. An empty string
    // is the case that pushed this: "empty", "none" and "" are three spellings of
    // one value and only the last is checkable.
    private static string SpellDefault(object? value) => value switch
    {
        null => "null",
        string text => $"\"{text}\"",
        bool flag => flag ? "true" : "false",
        _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty,
    };

    [Fact]
    public void EverySettingOnTheClassHasARowInTheReference()
    {
        var documented = Rows().Select(Setting).ToHashSet(StringComparer.Ordinal);

        foreach (var property in Settings())
        {
            Assert.True(
                documented.Contains(property.Name),
                $"{property.Name} is a setting on PluginConfiguration with no row in docs/configuration.md");
        }
    }

    [Fact]
    public void EveryRowInTheReferenceNamesASettingOnTheClass()
    {
        var declared = Settings().Select(property => property.Name).ToHashSet(StringComparer.Ordinal);

        foreach (var row in Rows())
        {
            Assert.True(
                declared.Contains(Setting(row)),
                $"docs/configuration.md has a row for {Setting(row)}, which is not a setting on PluginConfiguration");
        }
    }

    [Fact]
    public void EveryRowStatesTheDefaultTheClassActuallyHolds()
    {
        var fresh = new PluginConfiguration();
        var rows = Rows().ToDictionary(Setting, row => StatedDefault(row), StringComparer.Ordinal);

        foreach (var property in Settings())
        {
            if (!rows.TryGetValue(property.Name, out var stated))
            {
                // The missing row is the first test's finding. Saying it twice
                // would make one defect look like two.
                continue;
            }

            var actual = SpellDefault(property.GetValue(fresh));
            Assert.True(
                string.Equals(stated, actual, StringComparison.Ordinal),
                $"docs/configuration.md states the default of {property.Name} as {stated}, and the class holds {actual}");
        }
    }
}
