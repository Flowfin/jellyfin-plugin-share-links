using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace Jellyfin.Plugin.ShareLinks.Tests;

/// <summary>
/// The readme is the first and often the only thing anybody reads about this
/// plugin, and it began life as twenty two thousand bytes of instructions for
/// building a different plugin from a template (#82). Nothing in the build refuses
/// that: a tree whose readme describes a template compiles, packages and installs.
/// These tests are what refuses it, and they hold the three properties the readme
/// is worth having at all, which are that it is about this plugin, that it says
/// who a share is for, and that it names no server version the package does not
/// carry.
/// </summary>
public class ReadmeTests
{
    // Sentences and identifiers out of the plugin template's own readme, chosen
    // because none of them can appear by accident in a document about this
    // plugin. A whole-file comparison would be the obvious check and is the wrong
    // one: the readme is meant to be edited, so a check that reds on any edit is a
    // check somebody deletes.
    private static readonly string[] TemplateText =
    [
        "So you want to make a Jellyfin plugin",
        "jellyfin-plugin-template",
        "dotnet new Jellyfin-plugin",
        "MyJellyfinPlugin",
        "perfectly functional functionless",
        "Customize Plugin Information",
    ];

    private static string ReadFile(string name)
    {
        var path = Path.Combine(AppContext.BaseDirectory, name);
        Assert.True(File.Exists(path), $"{name} was not copied next to the test assembly: {path}");
        return File.ReadAllText(path);
    }

    private static string TargetAbi()
    {
        var match = Regex.Match(ReadFile("build.yaml"), "^targetAbi:\\s*\"([^\"]*)\"\\s*$", RegexOptions.Multiline);
        Assert.True(match.Success, "build.yaml declares no quoted 'targetAbi' field");
        return match.Groups[1].Value;
    }

    [Fact]
    public void NothingIsLeftOfTheTemplateReadme()
    {
        var readme = ReadFile("README.md");

        foreach (var text in TemplateText)
        {
            Assert.DoesNotContain(text, readme, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void TheOpeningParagraphNamesTheOneFeature()
    {
        // The first block of prose under the heading, which is what somebody reads
        // before deciding whether to read any more. A feature named in the middle
        // of the file is a feature most readers never reach.
        var readme = ReadFile("README.md");
        var blocks = readme.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split("\n\n", StringSplitOptions.RemoveEmptyEntries)
            .Select(b => b.Trim())
            .Where(b => b.Length > 0 && !b.StartsWith('#'))
            .ToList();

        Assert.NotEmpty(blocks);
        var opening = Regex.Replace(blocks[0], "\\s+", " ");

        Assert.Contains("one library item", opening, StringComparison.Ordinal);
        Assert.Contains("link", opening, StringComparison.Ordinal);
        Assert.Contains("expires", opening, StringComparison.Ordinal);
    }

    [Fact]
    public void ItSaysWhoAShareIsFor()
    {
        // The difference between an invited guest and anybody holding the link is
        // the whole posture of this plugin, and it is the one thing a reader must
        // not have to infer. The words are the ones the milestone issues use, so a
        // reader meeting them twice meets the same sentence.
        //
        // Runs of whitespace are collapsed first. The readme is hard wrapped, so a
        // sentence sits across two lines as often as not, and a check that reds
        // when a paragraph is rewrapped is a check about typography rather than
        // about what the readme says.
        var readme = Regex.Replace(ReadFile("README.md"), "\\s+", " ");

        Assert.Contains("Sharing is designed for invited guests of the server operator", readme, StringComparison.Ordinal);
        Assert.Contains("There are no anonymous public links", readme, StringComparison.Ordinal);
    }

    [Fact]
    public void EveryVersionTheReadmeNamesIsTheOneTheManifestCarries()
    {
        // A readme naming a server version the package was not built for sends
        // somebody to install something that will not load, and the two files drift
        // apart quietly because nothing reads them together.
        //
        // The pattern takes numbers of three parts or more. Two-part numbers are
        // left alone deliberately, because the readme legitimately carries them in
        // a licence identifier, and widening the pattern to catch them would mean
        // an exception list that grows until the check stops judging anything.
        // What that costs is stated rather than hidden: a server line written as
        // two parts, "10.10" say, walks past this check, and only a three-part or
        // four-part version is refused.
        var abi = TargetAbi();
        var readme = ReadFile("README.md");

        var named = Regex.Matches(readme, "\\d+(?:\\.\\d+){2,}")
            .Select(m => m.Value)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        Assert.Equal(new[] { abi }, named);
    }

    [Fact]
    public void TheReadmeNamesTheVersionRatherThanLeavingItOut()
    {
        // The check above is satisfied by a readme naming no version at all, which
        // would pass while leaving a reader with no idea what this plugin loads on.
        // This is the other half, and it is a separate test so that a readme losing
        // the number reds for that reason instead of reddening a check about
        // disagreement.
        Assert.Contains(
            string.Format(CultureInfo.InvariantCulture, "targetAbi: \"{0}\"", TargetAbi()),
            ReadFile("README.md"),
            StringComparison.Ordinal);
    }
}
