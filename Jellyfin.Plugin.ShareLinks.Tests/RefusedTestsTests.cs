using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Xunit;

namespace Jellyfin.Plugin.ShareLinks.Tests;

/// <summary>
/// <c>docs/refused-tests.md</c> names the tests this repository declines to write and
/// what stands in for each one (#75). These tests are what stop the second half of
/// that pairing from decaying into a promise.
/// </summary>
/// <remarks>
/// <para>
/// A refusal is only a decision while its replacement is real. The failure this
/// guards is quiet in exactly the way the page exists against: a replacement is
/// renamed or deleted for a good reason somewhere else in the suite, every run stays
/// green, and the page goes on naming it. A reader then counts a refusal as covered
/// because a name was written next to it once.
/// </para>
/// <para>
/// So every name a landed replacement gives is resolved against the compiled test
/// assembly rather than read. What is not judged is whether the replacement is an
/// adequate stand-in for what was refused, which is an argument and belongs to the
/// review; and an owed replacement is checked no further than the issue it names,
/// because there is nothing yet for a resolution to reach.
/// </para>
/// </remarks>
public sealed class RefusedTestsTests
{
    private const string ReasonLine = "**Why it is refused.**";
    private const string LandedLine = "**Replacement, landed.**";
    private const string OwedLine = "**Replacement, owed.**";

    // A reference to an issue on this repository. The lookbehind keeps an upstream
    // number written `jellyfin/jellyfin#14926` from passing as provenance here.
    private static readonly Regex IssueReference = new(@"(?<![\w/])#[0-9]+", RegexOptions.Compiled);

    // A backticked identifier, which is how the page names a test. A type on its own,
    // a bare method name, or a type and a method. Anything else in backticks, a path
    // or a setting, does not match and is not looked up.
    private static readonly Regex TestName = new(@"`([A-Za-z][A-Za-z0-9_]*(?:\.[A-Za-z][A-Za-z0-9_]*)?)`", RegexOptions.Compiled);

    /// <summary>
    /// Gets one case per refusal, so a half-written entry fails under its own heading
    /// rather than inside one assertion about the whole page.
    /// </summary>
    public static TheoryData<string> Refusals
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
    public void ThePageParsesToRefusalsRatherThanToNothing()
    {
        var entries = Read();

        // Without this leg every theory below runs with no cases at all, and a run
        // that judged nothing reads exactly like a run that judged the page.
        Assert.True(
            entries.Count >= 4,
            $"docs/refused-tests.md parsed to {entries.Count} refusals. #75 names four before "
            + "anything else is added to them, so fewer means the parser stopped seeing the "
            + "entries rather than that the refusals went away.");
    }

    [Theory]
    [MemberData(nameof(Refusals))]
    public void EveryRefusalSaysWhyItIsRefused(string heading)
    {
        var reason = Paragraph(Entry(heading), ReasonLine);

        Assert.False(
            reason is null,
            $"the refusal \"{heading}\" in docs/refused-tests.md carries no {ReasonLine} line. A "
            + "test declined without the clause it would have broken is a preference rather than "
            + "a decision.");

        // A sentence rather than a length. One of the reasons on the page is "It needs
        // a phone.", which is the whole truth about that line, and a floor tuned above
        // it would be refusing brevity rather than emptiness.
        Assert.True(
            reason!.Length > 0 && reason.EndsWith('.'),
            $"the refusal \"{heading}\" in docs/refused-tests.md opens a {ReasonLine} line and "
            + $"does not finish a sentence after it: \"{reason}\"");
    }

    [Theory]
    [MemberData(nameof(Refusals))]
    public void EveryRefusalCarriesAReplacement(string heading)
    {
        var body = Entry(heading);

        Assert.True(
            Paragraph(body, LandedLine) is not null || Paragraph(body, OwedLine) is not null,
            $"the refusal \"{heading}\" in docs/refused-tests.md names neither a landed nor an "
            + "owed replacement. A refusal on its own is the gap this page exists to stop being "
            + "written as a decision.");
    }

    [Theory]
    [MemberData(nameof(Refusals))]
    public void EveryNameALandedReplacementGivesIsATestThatExists(string heading)
    {
        var landed = Paragraph(Entry(heading), LandedLine);
        if (landed is null)
        {
            return;
        }

        var names = TestName.Matches(landed).Select(match => match.Groups[1].Value).Distinct(StringComparer.Ordinal).ToArray();

        Assert.True(
            names.Length >= 1,
            $"the refusal \"{heading}\" in docs/refused-tests.md declares a landed replacement and "
            + "backticks no name. Landed means the test can be pointed at.");

        var missing = names.Where(name => !Resolves(name)).ToArray();

        Assert.True(
            missing.Length == 0,
            $"the refusal \"{heading}\" in docs/refused-tests.md names " + string.Join(", ", missing)
            + " as a landed replacement and no such type or method is in the test assembly. Either "
            + "the replacement was renamed away and the page did not follow, or it never existed "
            + "and the line is owed rather than landed.");
    }

    [Theory]
    [MemberData(nameof(Refusals))]
    public void EveryOwedReplacementNamesTheIssueThatOwesIt(string heading)
    {
        var owed = Paragraph(Entry(heading), OwedLine);
        if (owed is null)
        {
            return;
        }

        Assert.True(
            IssueReference.IsMatch(owed),
            $"the refusal \"{heading}\" in docs/refused-tests.md declares an owed replacement and "
            + "names no issue. An absence nobody holds is the state this page is meant to make "
            + "readable rather than the state it records.");
    }

    // A type on its own, a method on its own, or a type and a method. All three
    // spellings appear on the page, because a replacement is sometimes a whole class
    // and sometimes one named case inside one.
    private static bool Resolves(string name)
    {
        var types = typeof(RefusedTestsTests).Assembly.GetTypes();
        var parts = name.Split('.');

        if (parts.Length == 2)
        {
            var owner = types.FirstOrDefault(type => string.Equals(type.Name, parts[0], StringComparison.Ordinal));
            return owner is not null && Methods(owner).Any(method => string.Equals(method.Name, parts[1], StringComparison.Ordinal));
        }

        return types.Any(type => string.Equals(type.Name, name, StringComparison.Ordinal))
            || types.Any(type => Methods(type).Any(method => string.Equals(method.Name, name, StringComparison.Ordinal)));
    }

    private static IEnumerable<MethodInfo> Methods(Type type) =>
        type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly);

    private static string Document()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "docs", "refused-tests.md");
        Assert.True(File.Exists(path), $"refused-tests.md was not copied next to the test assembly: {path}");
        return File.ReadAllText(path);
    }

    // The text under a marker, up to the blank line that ends its paragraph, so a line
    // prettier left where it was written and one it did not are read the same way.
    private static string? Paragraph(string body, string marker)
    {
        var start = body.IndexOf(marker, StringComparison.Ordinal);
        if (start < 0)
        {
            return null;
        }

        var rest = body[(start + marker.Length)..];
        var end = rest.IndexOf("\n\n", StringComparison.Ordinal);
        return (end < 0 ? rest : rest[..end]).Trim();
    }

    private static string Entry(string heading) =>
        Read().Single(entry => string.Equals(entry.Heading, heading, StringComparison.Ordinal)).Body;

    // A refusal is a third-level heading and everything under it until the next
    // heading of any level, so the entry that ends the page and one followed by a
    // further section are read the same way.
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
