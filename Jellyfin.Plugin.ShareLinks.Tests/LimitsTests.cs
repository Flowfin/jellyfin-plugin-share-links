using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace Jellyfin.Plugin.ShareLinks.Tests;

/// <summary>
/// <c>docs/limits.md</c> collects the limits an operator meets, and every entry on it
/// says what to do about the limit and where the limit came from (#86).
/// </summary>
/// <remarks>
/// <para>
/// A page of limits is complete on the day it is written and never again, and it fails
/// silently in the direction that matters: a limit decided somewhere else and never
/// collected here reads, to somebody consulting one page, as a limit that does not
/// exist. So the set of documents the page names is compared against the set of
/// documents in the tree, in both directions, rather than trusted.
/// </para>
/// <para>
/// The substitution that comparison rests on is stated on the page rather than hidden
/// here. #86 asks that every limit named in an earlier milestone's issue appears, and
/// what is checked is that every document under <c>docs/</c> is accounted for. Those
/// documents are where the earlier milestones' answers landed and each one names its
/// issue, so the two sets coincide for a limit that was written down anywhere. A limit
/// named in an issue and never written into any document is outside what this can see.
/// </para>
/// <para>
/// The entry legs are the other clause of the issue: a limit written without what an
/// operator should do about it is half an entry, and one written without the issue it
/// came from cannot be checked against the issue later. Both are refused per entry, so
/// a half-written entry fails under its own heading rather than inside one assertion
/// about the whole page.
/// </para>
/// </remarks>
public sealed class LimitsTests
{
    private const string OperatorLine = "**What an operator does.**";

    // A reference to an issue on this repository. The lookbehind is what keeps
    // `jellyfin/jellyfin#14926` from counting as one: an upstream number is not
    // provenance for a limit here, and the one entry that names it is about a limit
    // this plugin does not have.
    private static readonly Regex IssueReference = new(@"(?<![\w/])#[0-9]+", RegexOptions.Compiled);

    // A backticked path to a document, which is how the page names one.
    private static readonly Regex DocumentReference = new(@"`docs/([A-Za-z0-9._-]+\.md)`", RegexOptions.Compiled);

    /// <summary>
    /// Gets one case per entry, so an entry missing half of its shape fails under its
    /// own heading instead of inside one assertion about the page.
    /// </summary>
    public static TheoryData<string> Entries
    {
        get
        {
            var data = new TheoryData<string>();
            foreach (var entry in Read())
            {
                data.Add(entry.Heading);
            }

            return data;
        }
    }

    [Fact]
    public void ThePageParsesToEntriesRatherThanToNothing()
    {
        var entries = Read();

        // Without this, a parser that matched nothing would leave every theory below
        // with no cases and the run would read exactly like one that judged the page.
        Assert.True(
            entries.Count >= 20,
            $"docs/limits.md parsed to {entries.Count} entries. The page collects the limits of "
            + "a whole plugin, so a handful means the parser stopped seeing them rather than that "
            + "the limits went away.");
    }

    [Theory]
    [MemberData(nameof(Entries))]
    public void EveryEntrySaysWhatAnOperatorDoes(string heading)
    {
        var body = Entry(heading);
        var marker = body.IndexOf(OperatorLine, StringComparison.Ordinal);

        Assert.True(
            marker >= 0,
            $"the entry \"{heading}\" in docs/limits.md carries no {OperatorLine} line. A limit "
            + "written without it is one the reader is left to work out, which is what #86 asks "
            + "this page to stop.");

        var guidance = body[(marker + OperatorLine.Length)..].Trim();

        Assert.True(
            guidance.Length >= 20,
            $"the entry \"{heading}\" in docs/limits.md opens an {OperatorLine} line and says "
            + $"nothing after it: \"{guidance}\"");
    }

    [Theory]
    [MemberData(nameof(Entries))]
    public void EveryEntryNamesTheIssueTheLimitComesFrom(string heading)
    {
        Assert.True(
            IssueReference.IsMatch(Entry(heading)),
            $"the entry \"{heading}\" in docs/limits.md names no issue. A limit with no provenance "
            + "cannot be checked against the issue that decided it, and this page is a collection "
            + "of other issues' answers rather than a place decisions are taken.");
    }

    [Fact]
    public void EveryDocumentThisPageNamesIsInTheTree()
    {
        var present = Documents();
        var dangling = Named().Where(name => !present.Contains(name)).ToArray();

        Assert.True(
            dangling.Length == 0,
            "docs/limits.md names " + string.Join(", ", dangling)
            + " and no such file is under docs/. The tree holds: " + string.Join(", ", present.OrderBy(name => name, StringComparer.Ordinal)));
    }

    [Fact]
    public void EveryDocumentInTheTreeIsNamedHere()
    {
        var named = Named();
        var uncollected = Documents().Where(name => !named.Contains(name)).OrderBy(name => name, StringComparer.Ordinal).ToArray();

        Assert.True(
            uncollected.Length == 0,
            "docs/limits.md accounts for no part of " + string.Join(", ", uncollected)
            + ". A document that carries a limit and is named nowhere on this page is a limit an "
            + "operator consulting one page never meets. Give it an entry, or list it under what "
            + "this page draws no limit from.");
    }

    private static string Document()
    {
        var path = Path.Combine(DocumentDirectory(), "limits.md");
        Assert.True(File.Exists(path), $"limits.md was not copied next to the test assembly: {path}");
        return File.ReadAllText(path);
    }

    private static string DocumentDirectory()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "docs");
        Assert.True(Directory.Exists(path), $"the documents were not copied next to the test assembly: {path}");
        return path;
    }

    // Every document in the tree, by file name, which is how the page names one.
    private static IReadOnlyCollection<string> Documents() =>
        Directory.GetFiles(DocumentDirectory(), "*.md")
            .Select(Path.GetFileName)
            .Select(name => name!)
            .ToHashSet(StringComparer.Ordinal);

    private static IReadOnlyCollection<string> Named() =>
        DocumentReference.Matches(Document())
            .Select(match => match.Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);

    private static string Entry(string heading) =>
        Read().Single(entry => string.Equals(entry.Heading, heading, StringComparison.Ordinal)).Body;

    // An entry is a third-level heading and everything under it until the next heading
    // of any level, so a section that ends the page and one followed by a new section
    // are read the same way.
    private static IReadOnlyList<(string Heading, string Body)> Read()
    {
        var entries = new List<(string Heading, string Body)>();
        string? heading = null;
        var body = new List<string>();

        foreach (var line in Document().Split('\n').Select(line => line.TrimEnd('\r')))
        {
            if (line.StartsWith("## ", StringComparison.Ordinal) || line.StartsWith("### ", StringComparison.Ordinal))
            {
                if (heading is not null)
                {
                    entries.Add((heading, string.Join('\n', body)));
                }

                body.Clear();
                heading = line.StartsWith("### ", StringComparison.Ordinal) ? line[4..].Trim() : null;
                continue;
            }

            if (heading is not null)
            {
                body.Add(line);
            }
        }

        if (heading is not null)
        {
            entries.Add((heading, string.Join('\n', body)));
        }

        return entries;
    }
}
