using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace Jellyfin.Plugin.ShareLinks.Tests;

/// <summary>
/// The release notes a published version carries are assembled from the fragments
/// in <c>changelog.d</c> and from nothing else (#89).
/// </summary>
/// <remarks>
/// <para>
/// The assembler makes these checks too, and that is on purpose rather than
/// duplication: the run that publishes must not depend on a check somebody could
/// have skipped, and a fragment nobody may file is worth refusing on the pull
/// request that adds it rather than at the tag, because a tag is the one input on
/// this route that cannot be taken back.
/// </para>
/// <para>
/// What is NOT judged here is whether a fragment says anything useful to an
/// operator. That is what the review is for, and a fragment reading "fixed a bug"
/// passes every assertion below.
/// </para>
/// </remarks>
public sealed class ReleaseNotesTests
{
    // A fragment names the issue it belongs to and the heading it goes under.
    private static readonly Regex FragmentName = new(
        @"^(?<issue>[0-9]+)\.(?<kind>[a-z]+)\.md$",
        RegexOptions.Compiled);

    // The kinds the assembler accepts, read out of the assembler rather than
    // written here, because a list in a test is a third place for the set to drift.
    private static readonly Regex AssemblerKinds = new(
        @"^KINDS=\((?<kinds>[a-z ]+)\)$",
        RegexOptions.Compiled | RegexOptions.Multiline);

    // The kinds changelog.d/README.md offers the person writing a fragment. The
    // sentence is found first and the kinds are read out of it as the backticked
    // words, so a rewording that keeps the list keeps the comparison.
    private static readonly Regex DocumentedKinds = new(
        @"`<kind>` is one of (?<kinds>[^.]+)\.",
        RegexOptions.Compiled | RegexOptions.Singleline);

    private static readonly Regex BacktickedWord = new(
        @"`(?<word>[a-z]+)`",
        RegexOptions.Compiled);

    public static TheoryData<string> Fragments
    {
        get
        {
            var data = new TheoryData<string>();
            foreach (var name in FragmentNames())
            {
                data.Add(name);
            }

            // A theory with no cases passes by judging nothing, and changelog.d is
            // empty between releases by design, so one case that is not a fragment
            // keeps the legs below reaching an assertion.
            if (data.Count == 0)
            {
                data.Add("README.md");
            }

            return data;
        }
    }

    /// <summary>
    /// Every fragment is named for the issue it belongs to and a kind the assembler
    /// accepts.
    /// </summary>
    /// <param name="name">The file in <c>changelog.d</c>.</param>
    [Theory]
    [MemberData(nameof(Fragments))]
    public void EveryFragmentIsNamedForItsIssueAndAKindTheAssemblerAccepts(string name)
    {
        if (string.Equals(name, "README.md", StringComparison.Ordinal))
        {
            return;
        }

        var match = FragmentName.Match(name);

        Assert.True(
            match.Success,
            $"changelog.d/{name} is not named <issue>.<kind>.md. The name is where a fragment says "
            + "which issue it belongs to and which heading it goes under, and a file that says "
            + "neither is one the release notes cannot place.");

        var kind = match.Groups["kind"].Value;

        Assert.True(
            KindsTheAssemblerAccepts().Contains(kind, StringComparer.Ordinal),
            $"changelog.d/{name} names the kind \"{kind}\" and the assembler accepts "
            + string.Join(", ", KindsTheAssemblerAccepts())
            + ". A kind nothing accepts is an entry that would not appear under any heading.");
    }

    /// <summary>
    /// No fragment is empty.
    /// </summary>
    /// <param name="name">The file in <c>changelog.d</c>.</param>
    [Theory]
    [MemberData(nameof(Fragments))]
    public void NoFragmentIsEmpty(string name)
    {
        if (string.Equals(name, "README.md", StringComparison.Ordinal))
        {
            return;
        }

        Assert.True(
            File.ReadAllText(Path.Join(Directory(), name)).Trim().Length > 0,
            $"changelog.d/{name} is empty. An empty fragment is a bullet in the release notes with "
            + "nothing in it, and the fragment exists because somebody had something to say.");
    }

    /// <summary>
    /// The kinds the convention documents are the kinds the assembler accepts.
    /// </summary>
    /// <remarks>
    /// The two are written in different languages in different files, and a kind
    /// added to one and not the other fails in the least visible way there is: the
    /// person writing the fragment reads the page, files it correctly by that page,
    /// and the release simply does not carry it.
    /// </remarks>
    [Fact]
    public void TheKindsTheConventionDocumentsAreTheKindsTheAssemblerAccepts()
    {
        var documented = DocumentedKinds.Match(File.ReadAllText(Path.Join(Directory(), "README.md")));

        Assert.True(
            documented.Success,
            "changelog.d/README.md no longer says which kinds a fragment may carry in the sentence "
            + "this comparison reads, so the two lists cannot be compared at all.");

        var onThePage = BacktickedWord.Matches(documented.Groups["kinds"].Value)
            .Select(match => match.Groups["word"].Value)
            .ToArray();

        Assert.Equal(KindsTheAssemblerAccepts().OrderBy(kind => kind, StringComparer.Ordinal), onThePage.OrderBy(kind => kind, StringComparer.Ordinal));
    }

    /// <summary>
    /// The publish route reads the assembled notes, and builds no second set from
    /// the commits.
    /// </summary>
    /// <remarks>
    /// Two sources for one release body is the failure #89 decided against, and it
    /// is invisible: both produce a release that looks finished, and which one the
    /// forge uses is not this repository's to decide.
    /// </remarks>
    [Fact]
    public void ThePublishRouteReadsTheAssembledNotesAndGeneratesNoOthers()
    {
        var workflow = File.ReadAllText(Path.Join(AppContext.BaseDirectory, "workflows", "publish.yaml"))
            .Replace("\r\n", "\n", StringComparison.Ordinal);

        // The setting rather than the word. The workflow says in a comment why it
        // does not generate notes from the commits, and a comparison that could not
        // tell the explanation from the setting would forbid the explanation.
        var generating = workflow
            .Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.StartsWith("generate_release_notes:", StringComparison.Ordinal))
            .ToArray();

        Assert.True(
            generating.Length == 0,
            "publish.yaml sets " + string.Join(", ", generating)
            + ". The release body is assembled from the changelog fragments, and a second source "
            + "for it means whichever one the forge prefers wins without anybody choosing.");

        Assert.Contains("body_path:", workflow, StringComparison.Ordinal);
        Assert.Contains("assemble-release-notes.sh", workflow, StringComparison.Ordinal);
    }

    private static string Directory() => Path.Join(AppContext.BaseDirectory, "changelog.d");

    private static IReadOnlyList<string> FragmentNames() =>
        System.IO.Directory.Exists(Directory())
            ? System.IO.Directory.GetFiles(Directory(), "*.md")
                .Select(Path.GetFileName)
                .Where(name => name is not null)
                .Select(name => name!)
                .Where(name => !string.Equals(name, "README.md", StringComparison.Ordinal))
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray()
            : [];

    private static IReadOnlyList<string> KindsTheAssemblerAccepts()
    {
        var script = File.ReadAllText(Path.Join(AppContext.BaseDirectory, "scripts", "assemble-release-notes.sh"))
            .Replace("\r\n", "\n", StringComparison.Ordinal);
        var match = AssemblerKinds.Match(script);

        Assert.True(
            match.Success,
            ".github/scripts/assemble-release-notes.sh no longer declares its kinds as a KINDS array on "
            + "one line, so nothing here can read which kinds it accepts.");

        return match.Groups["kinds"].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
    }
}
