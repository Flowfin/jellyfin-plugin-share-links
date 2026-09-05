using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace Jellyfin.Plugin.ShareLinks.Tests;

/// <summary>
/// No workflow in this repository hands its label set to a synchroniser that
/// deletes the names this board declares for itself (#338).
/// </summary>
/// <remarks>
/// <para>
/// The plugin template shipped <c>.github/workflows/sync-labels.yaml</c>, which
/// called a reusable workflow in another organisation on the first of every month
/// and replaced this repository's whole label set with a list held over there. It
/// fired once, on 2026-09-01 at 02:47 UTC, and took six names off twelve open
/// issues, <c>blocked-on-decision</c> among them - the one name that separates an
/// issue waiting on a person from work somebody may pick up.
/// </para>
/// <para>
/// The deletion is not something the caller can switch off. The list is fetched
/// over plain https from another project's default branch at the moment the run
/// starts, so pinning the call by commit fixes which steps run and fixes nothing
/// about which labels this board is allowed to keep; and the underlying action
/// deletes even a name that list declares, where the entry names itself among its
/// own aliases. The file was removed rather than repaired, and this is what stops
/// it, or another file with the same effect, from arriving again unnoticed.
/// </para>
/// <para>
/// What is held is a reading of the workflow files in this tree, and that bound is
/// the whole of what this can be. No test here may reach the network, which
/// <c>docs/testing.md</c> fixes, so nothing below asks the server what the label
/// set currently is or what a scheduled run would do to it. A route that deletes
/// labels from outside these files - a person, a token, a workflow in another
/// repository - is outside what this sees.
/// </para>
/// </remarks>
public sealed class LabelVocabularyTests
{
    // The reusable workflow the removed file called, and the action underneath it.
    // Both are matched because either one, called directly, has the same effect on
    // this repository's labels.
    private static readonly Regex LabelSyncCall =
        new(@"jellyfin-meta-plugins/\.github/workflows/sync-labels\.ya?ml|EndBug/label-sync", RegexOptions.Compiled);

    // The input that turns a synchroniser into a replacement. `false` is not
    // refused: what this exists against is a run that removes what it did not
    // configure.
    private static readonly Regex DeletesOtherLabels =
        new(@"^\s*delete-other-labels:\s*(?:true|yes|on)\s*$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static IReadOnlyList<string> WorkflowFiles()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "workflows");
        Assert.True(Directory.Exists(directory), $"the workflow files were not copied next to the test assembly: {directory}");

        var files = Directory.GetFiles(directory, "*.yml")
            .Concat(Directory.GetFiles(directory, "*.yaml"))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToList();

        // A run that read no file would pass every assertion below it, which is the
        // silence this repository refuses elsewhere.
        Assert.NotEmpty(files);
        return files;
    }

    [Fact]
    public void NoWorkflowCallsTheLabelSynchroniserThatDeletedThisBoardsNames()
    {
        foreach (var path in WorkflowFiles())
        {
            var match = LabelSyncCall.Match(File.ReadAllText(path));
            Assert.False(
                match.Success,
                $"{Path.GetFileName(path)} calls '{match.Value}', which replaces this repository's label set from a list it does not own (#338)");
        }
    }

    [Fact]
    public void NoWorkflowGrantsALabelWriteThatDeletesWhatItDidNotConfigure()
    {
        // The call above is one spelling. This is the property underneath it, so a
        // second synchroniser under a name nobody has written yet is refused too.
        foreach (var path in WorkflowFiles())
        {
            foreach (var line in File.ReadAllLines(path))
            {
                Assert.False(
                    DeletesOtherLabels.IsMatch(line),
                    $"{Path.GetFileName(path)} asks a label synchroniser to delete every label it does not declare (#338)");
            }
        }
    }
}
