using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using Xunit;

namespace Jellyfin.Plugin.ShareLinks.Tests;

/// <summary>
/// <c>docs/security.md</c> states the posture and its limits (#84), and it is the
/// one page in this tree whose failure mode is that it reads well. A page of
/// controls rots in three directions, none of which reddens anything on its own:
/// a claim arrives with no proof and is read as covered because it is written
/// down, a claim names a test that was renamed or never existed, and a residual
/// quietly grows a proof and stops being a residual. These tests refuse all three.
/// </summary>
/// <remarks>
/// <para>
/// The two lists on that page belong to other documents. What a share token can
/// never reach is <c>docs/negative-capabilities.md</c>'s list, whose wording is
/// #47's to set, and the never-log list is <c>docs/logging.md</c>'s, which is
/// #27's. Both are compared in both directions rather than copied, because a
/// second copy of a list is the thing that goes stale in the direction nobody
/// looks: the page that was written once and never read again.
/// </para>
/// <para>
/// What none of this judges is whether a claim is true, whether the test named
/// beside it is the test that would catch the failure, or whether a control is
/// missing from the page altogether. Those are what the review is for, and the
/// page says the same about itself in prose.
/// </para>
/// </remarks>
public class SecurityPageTests
{
    private const string SecurityPage = "security.md";
    private const string NegativeCapabilities = "negative-capabilities.md";
    private const string LoggingPolicy = "logging.md";

    private const string Held = "**Held.**";
    private const string HeldInPart = "**Held in part.**";
    private const string NotHeld = "**Not held by a test here.**";

    private const string NeverList = "The never list";
    private const string Residuals = "What is not defended";
    private const string TokenReach = "What a share token can never reach";
    private const string TheList = "The list";

    // The sections of the page whose third-level headings are claims about a
    // control. Named rather than taken as "every section except the residuals",
    // so a section renamed out from under this file fails here instead of
    // silently leaving its claims unjudged.
    private static readonly string[] ClaimSections =
    [
        "What a leaked link is worth",
        TokenReach,
        "What is logged, and what is never logged",
        "What revoking a share stops",
    ];

    // A backticked bare identifier, which is how both this page and the threat
    // model name a test. A backticked path or a member access carries a dot or a
    // slash and is not one, which is what keeps `docs/logging.md` out of the set.
    private static readonly Regex BacktickedIdentifier = new("`([A-Za-z][A-Za-z0-9_]*)`", RegexOptions.Compiled);

    // A fenced block holds commands rather than prose, and the backticks that
    // fence it would otherwise pair with each other across the lines between.
    private static readonly Regex FencedBlock = new("```.*?```", RegexOptions.Compiled | RegexOptions.Singleline);

    /// <summary>
    /// Gets one case per claim, so a claim missing its status word fails under its
    /// own heading rather than inside one assertion about the whole page.
    /// </summary>
    public static TheoryData<string> ClaimHeadings
    {
        get
        {
            var data = new TheoryData<string>();
            foreach (var claim in AllClaims())
            {
                data.Add(claim.Heading);
            }

            return data;
        }
    }

    /// <summary>
    /// Gets one case per residual, for the same reason.
    /// </summary>
    public static TheoryData<string> ResidualHeadings
    {
        get
        {
            var data = new TheoryData<string>();
            foreach (var residual in Sections(Read(SecurityPage), Residuals))
            {
                data.Add(residual.Heading);
            }

            return data;
        }
    }

    [Fact]
    public void ThePageParsesToClaimsAndResidualsRatherThanToNothing()
    {
        // Every theory below has no cases at all if the parser stops seeing the
        // page, and a run with no cases reads exactly like a run that judged it.
        foreach (var section in ClaimSections)
        {
            Assert.True(
                Sections(Read(SecurityPage), section).Count > 0,
                $"docs/security.md has no claims under \"{section}\". Either the section was renamed, "
                + "in which case this file names the old name, or the claims are gone.");
        }

        Assert.True(
            AllClaims().Count >= 15,
            $"docs/security.md parsed to {AllClaims().Count} claims. The page states the posture of a "
            + "whole plugin, so a handful means the parser stopped seeing them.");

        Assert.True(
            Sections(Read(SecurityPage), Residuals).Count >= 5,
            $"docs/security.md parsed to {Sections(Read(SecurityPage), Residuals).Count} residuals under "
            + $"\"{Residuals}\". That section is the part of the page #84 says earns the rest of it.");
    }

    [Theory]
    [MemberData(nameof(ClaimHeadings))]
    public void EveryClaimCarriesExactlyOneStatusWord(string heading)
    {
        var body = Claim(heading);
        var carried = new[] { Held, HeldInPart, NotHeld }.Where(word => body.Contains(word, StringComparison.Ordinal)).ToArray();

        Assert.True(
            carried.Length == 1,
            $"the claim \"{heading}\" in docs/security.md carries {carried.Length} status words rather than one: "
            + $"[{string.Join(", ", carried)}]. A claim with none is read as covered because it is written down, "
            + "and a claim with two says both things at once.");
    }

    [Theory]
    [MemberData(nameof(ClaimHeadings))]
    public void EveryClaimThatSaysItIsHeldNamesATest(string heading)
    {
        var body = Claim(heading);

        if (!body.Contains(Held, StringComparison.Ordinal) && !body.Contains(HeldInPart, StringComparison.Ordinal))
        {
            return;
        }

        Assert.True(
            Named(body).Count > 0,
            $"the claim \"{heading}\" in docs/security.md says it is held and names no test. That is the "
            + "clause of #84 this file exists for: a control claimed without the test that proves it is a "
            + "sentence, and a reader cannot tell one from a control.");
    }

    [Theory]
    [MemberData(nameof(ClaimHeadings))]
    public void EveryTestAClaimNamesExistsInThisAssembly(string heading)
    {
        foreach (var name in Named(Claim(heading)))
        {
            Assert.True(
                TestNames().Contains(name),
                $"the claim \"{heading}\" in docs/security.md names {name}, and no test in this assembly is "
                + "called that. A name in a backtick looks like a thing that runs.");
        }
    }

    [Fact]
    public void ThePageNamesAtLeastOneTestAtAll()
    {
        // Without this, a page that stripped every backtick would satisfy the
        // theory above by having nothing to check, which is the shape the whole
        // fourth clause of #84 is written against.
        Assert.NotEmpty(AllClaims().SelectMany(claim => Named(claim.Body)).ToArray());
    }

    [Theory]
    [MemberData(nameof(ResidualHeadings))]
    public void NoResidualIsWrittenAsAControl(string heading)
    {
        var body = Sections(Read(SecurityPage), Residuals)
            .Single(section => string.Equals(section.Heading, heading, StringComparison.Ordinal))
            .Body;

        var carried = new[] { Held, HeldInPart, NotHeld }.Where(word => body.Contains(word, StringComparison.Ordinal)).ToArray();

        Assert.True(
            carried.Length == 0,
            $"the residual \"{heading}\" in docs/security.md carries the status word {string.Join(", ", carried)}. "
            + "A residual is a thing this plugin does not defend against, and a status word turns an admission "
            + "into a finding about a control.");

        Assert.True(
            Named(body).Count == 0,
            $"the residual \"{heading}\" in docs/security.md names {string.Join(", ", Named(body))}. A residual "
            + "with a proof is a control that has moved section, and leaving it here is a negative disclosure "
            + "read as a positive one.");
    }

    [Fact]
    public void TheListOfWhatATokenCannotReachIsTheListInTheDocumentThatOwnsIt()
    {
        var here = Sections(Read(SecurityPage), TokenReach).Select(section => section.Heading).ToHashSet(StringComparer.Ordinal);
        var there = Sections(Read(NegativeCapabilities), TheList).Select(section => section.Heading).ToHashSet(StringComparer.Ordinal);

        Assert.True(
            there.Count >= 8,
            $"docs/negative-capabilities.md parsed to {there.Count} lines under \"{TheList}\", which is fewer "
            + "than #47's own starting list, so the comparison below would be against almost nothing.");

        var missing = there.Except(here, StringComparer.Ordinal).OrderBy(line => line, StringComparer.Ordinal).ToArray();
        var extra = here.Except(there, StringComparer.Ordinal).OrderBy(line => line, StringComparer.Ordinal).ToArray();

        Assert.True(
            missing.Length == 0 && extra.Length == 0,
            "docs/security.md and docs/negative-capabilities.md disagree about what a share token can never "
            + $"reach. On the list and not on the security page: [{string.Join("; ", missing)}]. On the security "
            + $"page and not on the list: [{string.Join("; ", extra)}]. #47 owns the wording; the security page "
            + "carries it in the same words or the two say different things to two readers.");
    }

    [Fact]
    public void TheNeverListIsTheNeverListOfTheLoggingPolicy()
    {
        var here = Leads(Claim(NeverList));
        var there = Leads(SectionBody(Read(LoggingPolicy), NeverList));

        Assert.True(
            there.Count >= 4,
            $"docs/logging.md parsed to {there.Count} entries under \"{NeverList}\", so the comparison below "
            + "would be against almost nothing.");

        var missing = there.Except(here, StringComparer.Ordinal).ToArray();
        var extra = here.Except(there, StringComparer.Ordinal).ToArray();

        Assert.True(
            missing.Length == 0 && extra.Length == 0,
            "docs/security.md and docs/logging.md disagree about what is never logged. In the policy and not "
            + $"on the security page: [{string.Join("; ", missing)}]. On the security page and not in the "
            + $"policy: [{string.Join("; ", extra)}]. #27 owns the policy.");
    }

    private static string Claim(string heading) =>
        AllClaims().Single(claim => string.Equals(claim.Heading, heading, StringComparison.Ordinal)).Body;

    private static IReadOnlyList<(string Heading, string Body)> AllClaims() =>
        ClaimSections.SelectMany(section => Sections(Read(SecurityPage), section)).ToArray();

    private static IReadOnlyCollection<string> Named(string body) =>
        BacktickedIdentifier.Matches(FencedBlock.Replace(body, string.Empty))
            .Select(match => match.Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);

    private static IReadOnlyCollection<string> TestNames() =>
        typeof(SecurityPageTests).Assembly
            .GetTypes()
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
            .Select(method => method.Name)
            .ToHashSet(StringComparer.Ordinal);

    // The lead of a bullet, which is everything up to its first full stop. The
    // rest of the bullet is where each page says the same thing in its own words,
    // and comparing the whole of it would make the two pages one document.
    private static IReadOnlyCollection<string> Leads(string body)
    {
        var leads = new List<string>();
        StringBuilder? bullet = null;

        foreach (var line in body.Split('\n').Select(line => line.TrimEnd('\r')))
        {
            if (line.StartsWith("- ", StringComparison.Ordinal))
            {
                Close(bullet, leads);
                bullet = new StringBuilder(line[2..].Trim());
                continue;
            }

            if (bullet is not null && line.StartsWith("  ", StringComparison.Ordinal) && line.Trim().Length > 0)
            {
                bullet.Append(' ').Append(line.Trim());
                continue;
            }

            Close(bullet, leads);
            bullet = null;
        }

        Close(bullet, leads);
        return leads.ToHashSet(StringComparer.Ordinal);
    }

    private static void Close(StringBuilder? bullet, List<string> leads)
    {
        if (bullet is null)
        {
            return;
        }

        var text = bullet.ToString();
        var stop = text.IndexOf('.', StringComparison.Ordinal);
        leads.Add(stop < 0 ? text : text[..stop]);
    }

    // The third-level headings under a second-level one, with the body of each.
    // Read by heading text rather than by position, so a paragraph added above a
    // section does not shift what this file thinks it is reading.
    private static IReadOnlyList<(string Heading, string Body)> Sections(string document, string parent)
    {
        var sections = new List<(string Heading, string Body)>();
        var inside = false;
        string? heading = null;
        var body = new List<string>();

        foreach (var line in document.Split('\n').Select(line => line.TrimEnd('\r')))
        {
            if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                if (heading is not null)
                {
                    sections.Add((heading, string.Join('\n', body)));
                }

                heading = null;
                body.Clear();
                inside = string.Equals(line[3..].Trim(), parent, StringComparison.Ordinal);
                continue;
            }

            if (line.StartsWith("### ", StringComparison.Ordinal))
            {
                if (heading is not null)
                {
                    sections.Add((heading, string.Join('\n', body)));
                }

                body.Clear();
                heading = inside ? line[4..].Trim() : null;
                continue;
            }

            if (heading is not null)
            {
                body.Add(line);
            }
        }

        if (heading is not null)
        {
            sections.Add((heading, string.Join('\n', body)));
        }

        return sections;
    }

    // The body of a second-level section, up to the next heading of any level.
    private static string SectionBody(string document, string parent)
    {
        var body = new List<string>();
        var inside = false;

        foreach (var line in document.Split('\n').Select(line => line.TrimEnd('\r')))
        {
            if (line.StartsWith('#'))
            {
                if (inside)
                {
                    break;
                }

                inside = line.StartsWith("## ", StringComparison.Ordinal)
                    && string.Equals(line[3..].Trim(), parent, StringComparison.Ordinal);
                continue;
            }

            if (inside)
            {
                body.Add(line);
            }
        }

        Assert.True(body.Count > 0, $"no section called \"{parent}\" was found, or it is empty");
        return string.Join('\n', body);
    }

    private static string Read(string name)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "docs", name);
        Assert.True(File.Exists(path), $"{name} was not copied next to the test assembly: {path}");
        return File.ReadAllText(path);
    }
}
