using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace Jellyfin.Plugin.ShareLinks.Tests;

/// <summary>
/// Every branch a workflow trigger names is a branch this repository has (#211).
/// </summary>
/// <remarks>
/// <para>
/// A trigger filtered on a branch that is not here does not fail. It does nothing,
/// on every push, silently, while the file above it goes on describing the run it
/// was supposed to make. That is how <c>scorecard.yml</c> came to say it re-scored
/// on every push to the default branch and re-score only on its schedule: the
/// branch name arrived from the upstream template, which ships <c>main</c>, and
/// nothing here compares a branch name against the repository the file sits in.
/// </para>
/// <para>
/// What is held is agreement inside the tree, and that bound is the whole of what
/// this can be. No test in this repository may reach the network, which
/// <c>docs/testing.md</c> fixes and <c>.github/workflows/headless.yml</c> proves,
/// so nothing here can ask the server which branch is the default one. The branch
/// is therefore taken from the one other place in the tree that has to name a
/// branch that exists, which is the image address in <c>build.yaml</c>: a
/// catalogue fetches the logo from it, so a branch that is not here is a tile with
/// no picture. Deriving it there rather than repeating it in this file is what
/// stops the expected answer from being edited to match a wrong one.
/// </para>
/// <para>
/// A wildcard is accepted rather than resolved. <c>["**"]</c> is what most of
/// these files carry and it names no branch, so there is nothing for this to
/// compare; the failure this exists against is a specific name that is specifically
/// wrong.
/// </para>
/// </remarks>
public sealed class WorkflowBranchTests
{
    // `branches-ignore:` is a different key with the same prefix, so the colon is
    // matched rather than assumed. The remainder is whatever followed on the line,
    // which is a flow sequence where there is one and empty where the list is
    // written as indented entries underneath.
    private static readonly Regex BranchesKey = new(@"^\s*branches:[ \t]*(.*)$", RegexOptions.Compiled);

    // An entry of a block sequence. The list ends at the first line that is not
    // one, which is what keeps the `paths-ignore:` entries under the same trigger
    // out of the branch names.
    private static readonly Regex BlockEntry = new(@"^\s*-[ \t]+(.+?)\s*$", RegexOptions.Compiled);

    // The branch segment of a raw content address, which is the fourth path
    // element after the host: owner, repository, branch.
    private static readonly Regex RawContentBranch =
        new(@"raw\.githubusercontent\.com/[^/\s]+/[^/\s]+/([^/\s]+)/", RegexOptions.Compiled);

    private static string WorkflowDirectory()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "workflows");
        Assert.True(Directory.Exists(path), $"the workflow files were not copied next to the test assembly: {path}");
        return path;
    }

    private static IReadOnlyList<string> WorkflowFiles()
    {
        var files = Directory.GetFiles(WorkflowDirectory(), "*.yml")
            .Concat(Directory.GetFiles(WorkflowDirectory(), "*.yaml"))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToList();

        // A run that read no file would pass every assertion below it, which is the
        // shape this repository refuses elsewhere.
        Assert.NotEmpty(files);
        return files;
    }

    /// <summary>
    /// The branch names one workflow file's triggers filter on, in the two forms
    /// GitHub accepts for the same key.
    /// </summary>
    private static IReadOnlyList<string> BranchesNamedBy(string path)
    {
        var lines = File.ReadAllLines(path);
        var named = new List<string>();

        for (var i = 0; i < lines.Length; i++)
        {
            var key = BranchesKey.Match(lines[i]);
            if (!key.Success)
            {
                continue;
            }

            var remainder = key.Groups[1].Value.Trim();
            if (remainder.StartsWith('['))
            {
                named.AddRange(
                    remainder.Trim('[', ']')
                        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                        .Select(entry => entry.Trim('"', '\'')));
                continue;
            }

            for (var j = i + 1; j < lines.Length; j++)
            {
                var entry = BlockEntry.Match(lines[j]);
                if (!entry.Success)
                {
                    break;
                }

                named.Add(entry.Groups[1].Value.Trim('"', '\''));
            }
        }

        return named;
    }

    /// <summary>
    /// The branch a catalogue is told to fetch this plugin's image from, which is a
    /// branch this repository has or the tile is empty.
    /// </summary>
    private static string BranchThePackageMetadataPointsAt()
    {
        var buildYaml = Path.Combine(AppContext.BaseDirectory, "build.yaml");
        Assert.True(File.Exists(buildYaml), $"build.yaml was not copied next to the test assembly: {buildYaml}");

        var match = RawContentBranch.Match(File.ReadAllText(buildYaml));
        Assert.True(match.Success, "build.yaml names no raw content address to take a branch from");
        return match.Groups[1].Value;
    }

    [Fact]
    public void EveryBranchAWorkflowTriggerNamesIsTheBranchThePackageMetadataPointsAt()
    {
        var expected = BranchThePackageMetadataPointsAt();

        foreach (var path in WorkflowFiles())
        {
            foreach (var branch in BranchesNamedBy(path))
            {
                if (branch.Contains('*', StringComparison.Ordinal))
                {
                    continue;
                }

                Assert.True(
                    string.Equals(branch, expected, StringComparison.Ordinal),
                    $"{Path.GetFileName(path)} filters a trigger on branch '{branch}', and this repository publishes from '{expected}'");
            }
        }
    }

    [Fact]
    public void AFileDeclaringTheKeyYieldsAtLeastOneBranch()
    {
        // Without this, a reading that quietly matched nothing would report every
        // file clean, which is the same silence the defect above was hiding in.
        foreach (var path in WorkflowFiles())
        {
            if (!File.ReadAllLines(path).Any(line => BranchesKey.IsMatch(line)))
            {
                continue;
            }

            Assert.True(
                BranchesNamedBy(path).Count > 0,
                $"{Path.GetFileName(path)} declares a branch filter and none was read out of it");
        }
    }
}
