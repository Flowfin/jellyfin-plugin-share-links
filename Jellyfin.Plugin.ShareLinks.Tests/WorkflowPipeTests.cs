using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace Jellyfin.Plugin.ShareLinks.Tests;

/// <summary>
/// A workflow step that pipes says what the pipeline said, not what tee said (#234).
/// </summary>
/// <remarks>
/// <para>
/// A single-line <c>run:</c> is executed as <c>bash -e {0}</c>. Without
/// <c>pipefail</c> the exit status of a pipeline is the status of its LAST command,
/// so <c>dotnet test ... | tee test-output.txt</c> leaves the step green whenever
/// tee can write the file. The suite is then red and the check says success.
/// </para>
/// <para>
/// That is not hypothetical here. Job 95866157978 carried <c>Test Run Failed.</c>
/// and <c>Total tests: 1007 / Passed: 1006 / Failed: 1</c> in its own log while the
/// context named <c>test</c> reported success.
/// </para>
/// <para>
/// The step directly below the offending one was written against the quieter half
/// of this same problem: it reds the job when the suite ran nothing, because "a
/// suite that runs nothing reports success, which looks exactly like a suite that
/// ran and passed". It cannot catch a suite that ran and failed, because a run with
/// one failure still prints a total. So the repository already refuses one half of
/// this and this test refuses the other.
/// </para>
/// <para>
/// What is held is narrow on purpose. This does not judge what a pipeline does, only
/// that its exit status is not thrown away. A step with no pipe is not reached, and
/// a block that opens with <c>set -euo pipefail</c> is accepted whatever follows.
/// </para>
/// </remarks>
public sealed class WorkflowPipeTests
{
    // `run:` with something after it on the same line. A block is written `run: |`
    // or `run: >`, so the indicator is what separates the two forms.
    private static readonly Regex InlineRun = new(@"^(\s*)run:[ \t]*([^|>\s].*)$", RegexOptions.Compiled);

    // `run: |` and its variants, capturing the indentation the body must exceed.
    private static readonly Regex BlockRun = new(@"^(\s*)run:[ \t]*[|>][-+]?[ \t]*$", RegexOptions.Compiled);

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
    /// Every step in one file whose command carries a pipe and whose exit status is
    /// therefore decided by the last command alone.
    /// </summary>
    /// <remarks>
    /// Returned as descriptions rather than as a count, so a failure names the line
    /// somebody has to open rather than telling them how many there are.
    /// </remarks>
    internal static IReadOnlyList<string> StepsThatDropTheStatus(string name, IReadOnlyList<string> lines)
    {
        var found = new List<string>();

        for (var i = 0; i < lines.Count; i++)
        {
            var inline = InlineRun.Match(lines[i]);
            if (inline.Success)
            {
                if (CarriesAPipe(inline.Groups[2].Value))
                {
                    found.Add($"{name}:{i + 1} is a single-line run: carrying a pipe, so bash -e reports the last command");
                }

                continue;
            }

            var block = BlockRun.Match(lines[i]);
            if (!block.Success)
            {
                continue;
            }

            var indent = block.Groups[1].Value.Length;
            var body = new List<string>();
            for (var j = i + 1; j < lines.Count; j++)
            {
                if (lines[j].Trim().Length == 0)
                {
                    body.Add(lines[j]);
                    continue;
                }

                if (lines[j].Length - lines[j].TrimStart().Length <= indent)
                {
                    break;
                }

                body.Add(lines[j]);
            }

            if (!body.Any(line => CarriesAPipe(line)))
            {
                continue;
            }

            // The setting has to be in force before the pipeline runs, so a line
            // anywhere in the body is not enough on its own; what matters is that it
            // precedes the first pipe.
            var firstPipe = body.FindIndex(line => CarriesAPipe(line));
            var guarded = body.Take(firstPipe).Any(line => line.Contains("pipefail", StringComparison.Ordinal));
            if (!guarded)
            {
                found.Add($"{name}:{i + 1} is a run: block whose pipeline is not preceded by pipefail");
            }
        }

        return found;
    }

    /// <summary>
    /// Whether a command line carries a shell pipe, as opposed to a character that
    /// merely looks like one.
    /// </summary>
    /// <remarks>
    /// A YAML comment is not a command, and <c>||</c> is a shell operator rather than
    /// a pipeline, so neither is reached. This is a text rule and says so: a pipe
    /// inside a quoted string would be counted, and the remedy there is to open the
    /// block with pipefail anyway, which costs one line and is never wrong.
    /// </remarks>
    private static bool CarriesAPipe(string line)
    {
        var text = line.TrimStart();
        if (text.StartsWith('#'))
        {
            return false;
        }

        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] != '|')
            {
                continue;
            }

            var doubled = (i + 1 < text.Length && text[i + 1] == '|') || (i > 0 && text[i - 1] == '|');
            if (!doubled)
            {
                return true;
            }
        }

        return false;
    }

    [Fact]
    public void NoWorkflowStepThrowsAwayThePipelinesExitStatus()
    {
        var offences = WorkflowFiles()
            .SelectMany(path => StepsThatDropTheStatus(Path.GetFileName(path), File.ReadAllLines(path)))
            .ToList();

        Assert.True(
            offences.Count == 0,
            "a pipeline's exit status is decided by its last command unless pipefail is set first:"
                + Environment.NewLine
                + string.Join(Environment.NewLine, offences));
    }

    [Fact]
    public void ASingleLinePipeIsRefused()
    {
        var offences = StepsThatDropTheStatus(
            "sample.yaml",
            new[] { "      - name: Test", "        run: dotnet test | tee out.txt" });

        Assert.Single(offences);
        Assert.Contains("single-line run:", offences[0], StringComparison.Ordinal);
    }

    [Fact]
    public void ABlockWhosePipeComesBeforePipefailIsRefused()
    {
        var offences = StepsThatDropTheStatus(
            "sample.yaml",
            new[]
            {
                "      - name: Test",
                "        run: |",
                "          dotnet test | tee out.txt",
                "          set -euo pipefail",
            });

        Assert.Single(offences);
        Assert.Contains("not preceded by pipefail", offences[0], StringComparison.Ordinal);
    }

    [Fact]
    public void ABlockThatSetsPipefailFirstIsAccepted()
    {
        var offences = StepsThatDropTheStatus(
            "sample.yaml",
            new[]
            {
                "      - name: Test",
                "        run: |",
                "          set -euo pipefail",
                "          dotnet test | tee out.txt",
            });

        Assert.Empty(offences);
    }

    [Fact]
    public void AStepWithNoPipeIsNotReached()
    {
        var offences = StepsThatDropTheStatus(
            "sample.yaml",
            new[] { "      - name: Build", "        run: dotnet build -warnaserror" });

        Assert.Empty(offences);
    }

    [Fact]
    public void AnOrOperatorIsNotAPipeline()
    {
        var offences = StepsThatDropTheStatus(
            "sample.yaml",
            new[] { "      - name: Probe", "        run: command -v docker || exit 1" });

        Assert.Empty(offences);
    }
}
