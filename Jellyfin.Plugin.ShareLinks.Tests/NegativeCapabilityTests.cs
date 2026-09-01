using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Xunit;

namespace Jellyfin.Plugin.ShareLinks.Tests;

/// <summary>
/// <c>docs/negative-capabilities.md</c> is the list of what a share token can never
/// reach, one line at a time, with the verdict on each line written into it (#47).
/// These tests are what stop a verdict from outliving the thing it rests on.
/// </summary>
/// <remarks>
/// <para>
/// The failure this guards has already happened on that page, which is why it is
/// here rather than argued for. Two lines were rewritten from owed to held when the
/// work behind them landed, and the section counting held and unheld lines was not,
/// so the page's own summary contradicted its own list. A count written into the
/// document it is a count of goes stale in the flattering direction, silently.
/// </para>
/// <para>
/// So the count is derived by a command handed to the reader and never written, and
/// what runs here judges the shape a derivation needs: every line carries one of the
/// three verdicts, a held line names a test, and every name a held line gives
/// resolves against the compiled test assembly. A replacement renamed away then reds
/// the suite instead of leaving the page pointing at nothing. This is the guard
/// <c>docs/refused-tests.md</c> already carries, on the neighbouring page and for the
/// same reason.
/// </para>
/// <para>
/// What is NOT judged is the larger half. Whether the test a line names actually
/// holds the sentence above it is an argument about meaning, and the review is where
/// a wrong pairing is caught; a held line naming a test about something else passes
/// every assertion here. And whether the list is complete cannot be read from the
/// tree at all: a capability nobody wrote down is invisible to the page and to this.
/// </para>
/// </remarks>
public sealed class NegativeCapabilityTests
{
    // The three verdicts a line may open with, longest first so that "Held in part"
    // is not read as "Held" with a tail.
    private static readonly string[] Verdicts = ["Held in part", "Not held", "Held"];

    // A test as the page names one, which is inside the command that finds it rather
    // than in backticks: the page's evidence is a runnable grep and the declaration
    // it matches carries the name.
    private static readonly Regex TestName = new(
        @"public\s+(?:async\s+Task|void)\s+([A-Za-z][A-Za-z0-9_]*)",
        RegexOptions.Compiled);

    // Where an operator meets what a not-held line leaves over. One line of its
    // own rather than a sentence anywhere in the paragraph, so that the pointer is
    // as easy to find by eye as it is to resolve here.
    private static readonly Regex Pointer = new(
        @"^An operator meets it in `docs/limits\.md`, under ""(.+?)""\.\s*$",
        RegexOptions.Compiled | RegexOptions.Multiline);

    /// <summary>
    /// Gets one case per line, so a line that is wrong fails under its own heading
    /// rather than inside one assertion about the whole page.
    /// </summary>
    public static TheoryData<string> Lines
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

    /// <summary>
    /// The page parses to lines rather than to nothing. Without this leg every
    /// theory below runs with no cases, and a run that judged nothing reads exactly
    /// like a run that judged the page.
    /// </summary>
    [Fact]
    public void ThePageParsesToLinesRatherThanToNothing()
    {
        var entries = Read();

        Assert.True(
            entries.Count >= 8,
            $"docs/negative-capabilities.md parsed to {entries.Count} lines. #47 names eight "
            + "before anything is collected onto the list, so fewer means the parser stopped "
            + "seeing the lines rather than that the capabilities went away.");
    }

    /// <summary>
    /// Every line opens with one of the three verdicts.
    /// </summary>
    /// <param name="heading">The line.</param>
    [Theory]
    [MemberData(nameof(Lines))]
    public void EveryLineOpensWithItsVerdict(string heading)
    {
        var opening = Opening(Entry(heading));

        Assert.True(
            Verdict(heading) is not null,
            $"the line \"{heading}\" in docs/negative-capabilities.md opens with \"{opening}\" "
            + "and none of the three verdicts the page fixes. A line without one is read as "
            + "coverage by whoever is counting, which is what that page exists against.");
    }

    /// <summary>
    /// A line that claims to be held names at least one test, and every name it
    /// gives is a test that exists.
    /// </summary>
    /// <param name="heading">The line.</param>
    [Theory]
    [MemberData(nameof(Lines))]
    public void EveryHeldLineNamesATestThatExists(string heading)
    {
        if (Verdict(heading) is not ("Held" or "Held in part"))
        {
            return;
        }

        var body = Entry(heading);
        var names = TestName.Matches(body)
            .Select(match => match.Groups[1].Value)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            names.Length >= 1,
            $"the line \"{heading}\" in docs/negative-capabilities.md says it is held and names "
            + "no test. Held means a test can be pointed at, and a line pointing at another "
            + "line's test is one a rename leaves behind.");

        var missing = names.Where(name => !Resolves(name)).ToArray();

        Assert.True(
            missing.Length == 0,
            $"the line \"{heading}\" in docs/negative-capabilities.md names "
            + string.Join(", ", missing)
            + " and no such method is in the test assembly. Either the test was renamed away "
            + "and the page did not follow, or the line was never held.");
    }

    /// <summary>
    /// A line that is not held says whose refusal it is instead.
    /// </summary>
    /// <remarks>
    /// The three lines in that state are each somebody else's refusal, over the
    /// server's own routes or inside an assembly this repository does not compile
    /// against. Saying so is the difference between a decision and a gap, which is
    /// the whole pairing this page is built on.
    /// </remarks>
    /// <param name="heading">The line.</param>
    [Theory]
    [MemberData(nameof(Lines))]
    public void EveryLineThatIsNotHeldSaysWhyNot(string heading)
    {
        if (Verdict(heading) != "Not held")
        {
            return;
        }

        var opening = Opening(Entry(heading));

        Assert.True(
            opening.Length > "Not held".Length + 1 && opening.EndsWith('.'),
            $"the line \"{heading}\" in docs/negative-capabilities.md says it is not held and "
            + $"finishes no sentence saying whose refusal it is instead: \"{opening}\"");
    }

    /// <summary>
    /// A line that is not held names what this plugin contributes towards the
    /// refusal somebody else makes, and the test that asserts that contribution.
    /// </summary>
    /// <remarks>
    /// <para>
    /// #47's done-when separates the two groups on this page by whose check would
    /// have to be removed for the test to fail. For the three lines whose check is
    /// in another assembly there is nothing here to remove, so what the line owes
    /// instead is the contribution this plugin does make, with the assertion on it.
    /// Without this leg a not-held line is a sentence, and a sentence is what the
    /// page can carry while the switch behind it is quietly deleted.
    /// </para>
    /// <para>
    /// What this does NOT judge is whether the contribution is the right one, or
    /// whether it reaches the refusal the heading names. That is the same argument
    /// about meaning the held lines leave to the review, and it is left there.
    /// </para>
    /// </remarks>
    /// <param name="heading">The line.</param>
    [Theory]
    [MemberData(nameof(Lines))]
    public void EveryLineThatIsNotHeldNamesTheContributionItAsserts(string heading)
    {
        if (Verdict(heading) != "Not held")
        {
            return;
        }

        var names = TestName.Matches(Entry(heading))
            .Select(match => match.Groups[1].Value)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            names.Length >= 1,
            $"the line \"{heading}\" in docs/negative-capabilities.md says it is not held and names "
            + "no test at all. A not-held line owes the contribution this plugin makes towards "
            + "somebody else's refusal and the assertion on it, so that dropping the contribution "
            + "reds the suite rather than leaving the line reading as it did.");

        var missing = names.Where(name => !Resolves(name)).ToArray();

        Assert.True(
            missing.Length == 0,
            $"the line \"{heading}\" in docs/negative-capabilities.md names "
            + string.Join(", ", missing)
            + " and no such method is in the test assembly. Either the test was renamed away "
            + "and the page did not follow, or the contribution was never asserted.");
    }

    /// <summary>
    /// A line that is not held names the section of <c>docs/limits.md</c> where an
    /// operator meets what is left, and that section is on that page.
    /// </summary>
    /// <remarks>
    /// A bound that lives only on this page is one an operator does not read: this
    /// page is the list a reviewer checks and <c>docs/limits.md</c> is the page
    /// somebody deciding whether to install the plugin is pointed at. #47's
    /// done-when asks the residual to be carried where they meet it, and a pointer
    /// nothing resolves goes stale the first time that page is reorganised.
    /// </remarks>
    /// <param name="heading">The line.</param>
    [Theory]
    [MemberData(nameof(Lines))]
    public void EveryLineThatIsNotHeldNamesWhereAnOperatorMeetsTheResidual(string heading)
    {
        if (Verdict(heading) != "Not held")
        {
            return;
        }

        var pointer = Pointer.Match(Entry(heading));

        Assert.True(
            pointer.Success,
            $"the line \"{heading}\" in docs/negative-capabilities.md says it is not held and points "
            + "at no section of docs/limits.md. The line is expected to carry, on one line of its "
            + "own: An operator meets it in `docs/limits.md`, under \"<the heading there>\".");

        var section = pointer.Groups[1].Value;
        var headings = Document("limits.md")
            .Split('\n')
            .Select(line => line.TrimEnd('\r'))
            .Where(line => line.StartsWith("### ", StringComparison.Ordinal))
            .Select(line => line[4..].Trim())
            .ToArray();

        Assert.True(
            Array.Exists(headings, other => string.Equals(other, section, StringComparison.Ordinal)),
            $"the line \"{heading}\" in docs/negative-capabilities.md points at the section "
            + $"\"{section}\" of docs/limits.md and that page carries no such heading. A residual "
            + "an operator is sent to and cannot find is one this page has stopped carrying.");
    }

    /// <summary>
    /// A line's verdict is the same on both pages that carry it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>docs/security.md</c> repeats this list with a status word of its own, and
    /// <c>SecurityPageTests</c> already compares the two sets of headings in both
    /// directions. What nothing compared is the verdicts, and the two pages had
    /// drifted apart on two lines: the item line and the listing line read as held
    /// here and as held in part there, over the same filter and the same bound.
    /// </para>
    /// <para>
    /// A reader consulting one page and quoting it at somebody reading the other is
    /// the failure, and it is worse than either page being wrong on its own, because
    /// both look internally consistent.
    /// </para>
    /// </remarks>
    /// <param name="heading">The line.</param>
    [Theory]
    [MemberData(nameof(Lines))]
    public void TheVerdictOnALineIsTheSameOnBothPagesThatCarryIt(string heading)
    {
        var here = Verdict(heading);
        var there = SecurityPageVerdicts();

        Assert.True(
            there.TryGetValue(heading, out var other),
            $"docs/security.md carries no line \"{heading}\" under what a share token can never "
            + "reach, and docs/negative-capabilities.md does.");

        Assert.True(
            string.Equals(here, other, StringComparison.Ordinal),
            $"the line \"{heading}\" reads \"{here}\" on docs/negative-capabilities.md and "
            + $"\"{other}\" on docs/security.md. One of the two pages is telling a reader "
            + "something the other denies.");
    }

    // The status word docs/security.md gives each line of the same list, read into
    // this page's three verdicts. The markers there are bold and end in a full stop,
    // and the not-held one says where the refusal lives instead, so they are mapped
    // rather than compared as text.
    private static IReadOnlyDictionary<string, string> SecurityPageVerdicts()
    {
        var verdicts = new Dictionary<string, string>(StringComparer.Ordinal);
        var page = Document("security.md");
        var start = page.IndexOf("\n## What a share token can never reach", StringComparison.Ordinal);
        Assert.True(start >= 0, "docs/security.md carries no section for what a share token can never reach.");

        var rest = page[start..];
        var end = rest.IndexOf("\n## ", 1, StringComparison.Ordinal);
        var section = end < 0 ? rest : rest[..end];

        string? heading = null;
        foreach (var line in section.Split('\n').Select(line => line.TrimEnd('\r')))
        {
            if (line.StartsWith("### ", StringComparison.Ordinal))
            {
                heading = line[4..].Trim();
                continue;
            }

            if (heading is null)
            {
                continue;
            }

            var verdict = line.StartsWith("**Held in part.**", StringComparison.Ordinal) ? "Held in part"
                : line.StartsWith("**Not held by a test here.**", StringComparison.Ordinal) ? "Not held"
                : line.StartsWith("**Held.**", StringComparison.Ordinal) ? "Held"
                : null;

            if (verdict is not null)
            {
                verdicts[heading] = verdict;
                heading = null;
            }
        }

        return verdicts;
    }

    // The verdict a line opens with, or null where it opens with none.
    private static string? Verdict(string heading)
    {
        var opening = Opening(Entry(heading));
        return Array.Find(Verdicts, verdict => opening.StartsWith(verdict, StringComparison.Ordinal));
    }

    // The first sentence under a heading. Read to the first full stop rather than to
    // the end of the paragraph, because what the verdict legs judge is the opening
    // statement and not everything written under it.
    private static string Opening(string body)
    {
        var text = string.Join(' ', body.Split('\n').Select(line => line.Trim()).Where(line => line.Length > 0));
        var stop = text.IndexOf('.', StringComparison.Ordinal);
        return stop < 0 ? text : text[..(stop + 1)];
    }

    private static bool Resolves(string name) =>
        typeof(NegativeCapabilityTests).Assembly.GetTypes()
            .Any(type => type
                .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .Any(method => string.Equals(method.Name, name, StringComparison.Ordinal)));

    private static string Document() => Document("negative-capabilities.md");

    private static string Document(string name)
    {
        var path = Path.Join(AppContext.BaseDirectory, "docs", name);
        Assert.True(File.Exists(path), $"{name} was not copied next to the test assembly: {path}");
        return File.ReadAllText(path);
    }

    private static string Entry(string heading) =>
        Read().Single(entry => string.Equals(entry.Heading, heading, StringComparison.Ordinal)).Body;

    // A line is a third-level heading and everything under it until the next heading
    // of any level, so the line that ends the list and one followed by a further
    // section are read the same way.
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
