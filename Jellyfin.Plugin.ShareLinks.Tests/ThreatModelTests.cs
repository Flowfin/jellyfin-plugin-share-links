using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Xunit;

namespace Jellyfin.Plugin.ShareLinks.Tests;

/// <summary>
/// The threat model is a table of threats, controls and proofs (#23), and a table
/// of proofs is worth exactly as much as the proofs resolving. Two ways it rots
/// without anything going red: a row arrives with a control and no proof, which
/// reads as covered because the row exists at all, or a proof names a test that
/// was renamed or never written, which reads as covered because a name in a
/// backtick looks like a thing that runs. These tests refuse both.
/// </summary>
/// <remarks>
/// <para>
/// What they cannot judge is whether a control answers its threat, or whether an
/// issue named in a proof column will ever land. A row naming an issue is a
/// control that is owed, and nothing here shortens that wait or notices when it
/// ends. The document says the same thing about itself in prose; this file is the
/// half a machine can hold.
/// </para>
/// </remarks>
public class ThreatModelTests
{
    // The issues under the threat model milestone that hold a threat of their
    // own. The issue asks that the table cover at least these, so they are named
    // here rather than counted out of the document, which would make the test
    // agree with whatever the document happens to say.
    private static readonly int[] SiblingThreatIssues = [24, 25, 26, 27, 28, 29, 31];

    private static string Document()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "threat-model.md");
        Assert.True(File.Exists(path), $"threat-model.md was not copied next to the test assembly: {path}");
        return File.ReadAllText(path);
    }

    // A row is a table line whose first cell is a threat identifier. The heading
    // row and the separator row are not, so neither has to be skipped by
    // position, which is what breaks when somebody adds a paragraph above the
    // table.
    private static IReadOnlyList<string[]> Rows()
    {
        var rows = Document()
            .Split('\n')
            .Select(line => line.Trim())
            .Where(line => Regex.IsMatch(line, @"^\|\s*T\d+\s*\|"))
            .Select(line => line.Trim('|').Split('|').Select(cell => cell.Trim()).ToArray())
            .ToList();

        // Without this the three tests below all pass on a document with no table
        // in it, which is the state this file exists to refuse.
        Assert.True(rows.Count >= SiblingThreatIssues.Length, $"the threat table parsed to {rows.Count} rows");
        return rows;
    }

    private static string Identifier(string[] row) => row[0];

    private static string Proof(string[] row) => row[^1];

    [Fact]
    public void EveryRowCarriesAThreatAControlAndAProof()
    {
        foreach (var row in Rows())
        {
            Assert.True(row.Length == 4, $"{Identifier(row)} has {row.Length} cells rather than four");

            foreach (var cell in row)
            {
                Assert.False(string.IsNullOrWhiteSpace(cell), $"{Identifier(row)} has an empty cell");
            }
        }
    }

    [Fact]
    public void EveryProofIsAnIssueNumberOrATestName()
    {
        foreach (var row in Rows())
        {
            var proof = Proof(row);
            var resolves = Regex.IsMatch(proof, @"#\d+") || Regex.IsMatch(proof, "`[A-Za-z][A-Za-z0-9_]*`");
            Assert.True(resolves, $"{Identifier(row)} has a proof column naming neither an issue nor a test: {proof}");
        }
    }

    [Fact]
    public void EveryTestNamedAsAProofExistsInThisAssembly()
    {
        var known = typeof(ThreatModelTests).Assembly
            .GetTypes()
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
            .Select(method => method.Name)
            .ToHashSet(StringComparer.Ordinal);

        var named = Rows()
            .SelectMany(row => Regex.Matches(Proof(row), "`([A-Za-z][A-Za-z0-9_]*)`").Select(match => (Row: Identifier(row), Name: match.Groups[1].Value)))
            .ToList();

        // A document that names no test at all would pass the loop below without
        // asserting anything, and that is the state the token rows are there to
        // keep this file out of.
        Assert.NotEmpty(named);

        foreach (var (row, name) in named)
        {
            Assert.True(known.Contains(name), $"{row} names {name} as its proof, and no test in this assembly is called that");
        }
    }

    [Fact]
    public void EveryThreatHeldByASiblingIssueHasARow()
    {
        var referenced = Rows()
            .SelectMany(row => Regex.Matches(Proof(row), @"#(\d+)").Select(match => int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture)))
            .ToHashSet();

        foreach (var issue in SiblingThreatIssues)
        {
            Assert.True(referenced.Contains(issue), $"no row names #{issue} in its proof column");
        }
    }
}
