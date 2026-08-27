using System;
using System.IO;
using System.Text.RegularExpressions;
using Xunit;

namespace Jellyfin.Plugin.ShareLinks.Tests;

/// <summary>
/// The release process names the interoperability matrix as a condition, and the
/// things it names are in this tree (#96).
/// </summary>
/// <remarks>
/// <para>
/// #96 asks that the family rule be held by a machine rather than by care, and its
/// last clause is that the release checklist names the matrix as a condition. A
/// checklist step is prose, so what a check can hold is the part that goes wrong
/// silently: the step naming a workflow that has been renamed, or a trigger the
/// step tells a releaser to use and the workflow no longer declares. Either leaves
/// a step somebody follows to a run that is not there, and nothing else in this
/// tree compares the two.
/// </para>
/// <para>
/// WHAT THIS DOES NOT DO IS REFUSE A RELEASE. The publish route reads no other
/// workflow's runs, so a tag pushed while the matrix is red publishes exactly like
/// one pushed while it is green. <c>docs/RELEASING.md</c> says so where the
/// condition is written, and this remark is the same sentence where a reader of
/// the check meets it. The condition is held by whoever cuts the release; this
/// holds only that what they are told to read still exists.
/// </para>
/// </remarks>
public sealed class InteroperabilityMatrixTests
{
    // The workflow the release section names, as it names it.
    private const string MatrixWorkflow = ".github/workflows/observations.yml";

    // Where an incompatibility the matrix finds is written down instead of fixed.
    private const string LimitsPage = "docs/limits.md";

    // The step in the cutting-a-release list, and the step it has to come before.
    // Checking after the tag is pushed is checking after the one input on this
    // route that cannot be taken back.
    private const string MatrixStep = "interoperability matrix";
    private const string TagStep = "Push the tag";

    /// <summary>
    /// The release process names the matrix workflow, and that file is in the tree.
    /// </summary>
    [Fact]
    public void TheReleaseProcessNamesAWorkflowThatExists()
    {
        Assert.Contains(MatrixWorkflow, Releasing(), StringComparison.Ordinal);

        var path = Path.Combine(WorkflowDirectory(), Path.GetFileName(MatrixWorkflow));
        Assert.True(File.Exists(path), $"docs/RELEASING.md names {MatrixWorkflow} and it is not in the tree: {path}");
    }

    /// <summary>
    /// That workflow declares the two triggers the release process sends a releaser
    /// to: the schedule the nightly verdict comes from, and the manual start for the
    /// commit being released.
    /// </summary>
    [Fact]
    public void TheMatrixCarriesTheTriggersTheProcessSendsAReleaserTo()
    {
        var workflow = MatrixWorkflowText();

        Assert.Matches(new Regex(@"^\s*schedule:\s*$", RegexOptions.Multiline), workflow);
        Assert.Matches(new Regex(@"^\s*workflow_dispatch:\s*$", RegexOptions.Multiline), workflow);
        Assert.Contains("workflow_dispatch", Releasing(), StringComparison.Ordinal);
    }

    /// <summary>
    /// The release process says what a red matrix costs, and the page it sends an
    /// incompatibility to is in the tree.
    /// </summary>
    [Fact]
    public void ARedMatrixHasSomewhereToGo()
    {
        Assert.Contains(LimitsPage, Releasing(), StringComparison.Ordinal);

        var path = Path.Combine(AppContext.BaseDirectory, "docs", Path.GetFileName(LimitsPage));
        Assert.True(File.Exists(path), $"docs/RELEASING.md names {LimitsPage} and it is not in the tree: {path}");
    }

    /// <summary>
    /// The matrix is read before the tag is pushed rather than after it.
    /// </summary>
    [Fact]
    public void TheMatrixIsCheckedBeforeTheTagIsPushed()
    {
        var releasing = Releasing();

        var matrix = releasing.IndexOf(MatrixStep, StringComparison.Ordinal);
        var tag = releasing.IndexOf(TagStep, StringComparison.Ordinal);

        Assert.True(matrix >= 0, $"docs/RELEASING.md carries no step naming the {MatrixStep}.");
        Assert.True(tag >= 0, $"docs/RELEASING.md carries no step reading '{TagStep}'.");
        Assert.True(
            matrix < tag,
            "docs/RELEASING.md reads the interoperability matrix after the tag is pushed. A tag is the one input on this route that cannot be taken back.");
    }

    private static string Releasing()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "docs", "RELEASING.md");
        Assert.True(File.Exists(path), $"RELEASING.md was not copied next to the test assembly: {path}");
        return File.ReadAllText(path);
    }

    private static string MatrixWorkflowText()
    {
        var path = Path.Combine(WorkflowDirectory(), Path.GetFileName(MatrixWorkflow));
        Assert.True(File.Exists(path), $"the matrix workflow was not copied next to the test assembly: {path}");
        return File.ReadAllText(path);
    }

    private static string WorkflowDirectory()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "workflows");
        Assert.True(Directory.Exists(path), $"the workflow files were not copied next to the test assembly: {path}");
        return path;
    }
}
