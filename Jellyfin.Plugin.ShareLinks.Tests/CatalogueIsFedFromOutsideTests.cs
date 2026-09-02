using System;
using System.IO;
using System.Linq;
using Xunit;

namespace Jellyfin.Plugin.ShareLinks.Tests;

/// <summary>
/// The release process tells whoever cuts a release that the catalogue a reader
/// installs from is fed by the tag, from outside, and that no manifest generator
/// is going to be added to this repository. That claim is not this document's to
/// make on its own: it rests on two rows of <c>docs/parity-ledger.md</c> reading
/// <c>Declined</c>, which is where the scope call of #90 was recorded. These
/// tests hold the two together.
/// </summary>
/// <remarks>
/// <para>
/// The sentence they replace said the opposite - that no catalog is fed until a
/// manifest generator is added - and it went on saying it for the eleven days
/// between the scope call and its repair, because nothing read it. A releaser
/// following it would have gone looking for a generator to write.
/// </para>
/// <para>
/// WHAT THIS CANNOT JUDGE. Everything the claim says about the catalogue itself -
/// that a generator in another repository reads finished releases, that this
/// plugin is declared there and enabled, that the served address answers - is a
/// fact of a tree this suite may not reach, which <c>docs/testing.md</c> fixes.
/// Those are read by hand and written into #90. What is held here is the half
/// that is decidable offline: the two documents in this tree agreeing about a
/// verdict one of them owns.
/// </para>
/// </remarks>
public sealed class CatalogueIsFedFromOutsideTests
{
    // The two rows of the parity ledger the release process's claim rests on.
    // Both were `Deferred to M11` until the scope call, and a row moving back is
    // exactly the drift that would make that claim false again.
    public static TheoryData<string> ManifestRows() => new() { "manifest-freshness.yml", "regenerate-manifest.yml" };

    /// <summary>
    /// Each row still declines the workflow rather than deferring or adopting it.
    /// </summary>
    /// <param name="workflow">The workflow the row is about.</param>
    [Theory]
    [MemberData(nameof(ManifestRows))]
    public void TheLedgerStillDeclinesTheWorkflowTheReleaseProcessSaysWillNotBeAdded(string workflow)
    {
        var verdict = LedgerVerdict(workflow);

        Assert.Equal("Declined", verdict);
    }

    /// <summary>
    /// The release process names both rows, so a reader who doubts its claim is
    /// sent to the file that carries the verdict rather than left to find it.
    /// </summary>
    /// <param name="workflow">The workflow the row is about.</param>
    [Theory]
    [MemberData(nameof(ManifestRows))]
    public void TheReleaseProcessNamesTheRowsItsClaimRestsOn(string workflow)
    {
        Assert.Contains(workflow, Releasing(), StringComparison.Ordinal);
    }

    /// <summary>
    /// It names the ledger itself and where the catalogue is generated instead, so
    /// the claim is a pointer rather than an assertion with nowhere to go.
    /// </summary>
    [Fact]
    public void TheReleaseProcessNamesWhereTheCatalogueIsGeneratedInstead()
    {
        var releasing = Releasing();

        Assert.Contains("docs/parity-ledger.md", releasing, StringComparison.Ordinal);
        Assert.Contains("Flowfin/hub", releasing, StringComparison.Ordinal);
    }

    // The verdict column of the ledger row naming this workflow. The table is
    // pipe-separated and the workflow name is backticked in the first cell, so the
    // row is found by that cell rather than by a substring of the whole line: the
    // prose above the table names both workflows as well, and a line-contains
    // search would read one of those sentences as a row.
    private static string LedgerVerdict(string workflow)
    {
        var path = Path.Join(AppContext.BaseDirectory, "docs", "parity-ledger.md");
        Assert.True(File.Exists(path), $"docs/parity-ledger.md was not copied next to the test assembly: {path}");

        var rows = File.ReadAllLines(path)
            .Where(line => line.StartsWith("|", StringComparison.Ordinal))
            .Select(line => line.Split('|'))
            .Where(cells => cells.Length > 2 && cells[1].Trim() == "`" + workflow + "`")
            .ToList();

        Assert.True(rows.Count == 1, $"docs/parity-ledger.md carries {rows.Count} rows for {workflow}, and this check reads one.");

        return rows[0][2].Trim();
    }

    private static string Releasing()
    {
        var path = Path.Join(AppContext.BaseDirectory, "docs", "RELEASING.md");
        Assert.True(File.Exists(path), $"docs/RELEASING.md was not copied next to the test assembly: {path}");
        return File.ReadAllText(path);
    }
}
