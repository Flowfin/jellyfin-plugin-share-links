using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace Jellyfin.Plugin.ShareLinks.Tests;

/// <summary>
/// The manual trigger on <c>publish.yaml</c> fires the alert and cannot publish (#91).
/// </summary>
/// <remarks>
/// <para>
/// The <c>alert</c> job is the only thing in this repository that says a publish did
/// not happen, and until the trigger this file judges arrived there was no way to
/// watch it fire that did not push a tag of this repository and spend it on a drill.
/// The trigger buys that observation back, and it buys it by adding a second way into
/// a workflow whose whole design is that a tag is the only way in.
/// </para>
/// <para>
/// So the thing worth refusing is not the drill, it is the drill quietly becoming a
/// publish. Three properties keep it from being one and they are independent on
/// purpose: the flag that makes the run fail is what admits it past the gate, the
/// failure is placed before the step that produces the archive, and the release job
/// declines a manual run whatever the other two do. Any one of them removed still
/// leaves a route that publishes nothing; all three are here so that removing one is
/// a red suite rather than a silent narrowing.
/// </para>
/// <para>
/// What is held is the shape of the file and nothing about a run. Whether the forge
/// evaluates these conditions the way they read is not something a test in this
/// repository can ask, because no test here reaches the network, which
/// <c>docs/testing.md</c> fixes. The run itself is the evidence for that half and it
/// is recorded on #91 rather than here.
/// </para>
/// </remarks>
public sealed class PublishAlertDrillTests
{
    // A job header: two spaces under `jobs:`, a name, a colon and nothing else.
    private static readonly Regex JobHeader = new(@"^  (?<name>[A-Za-z0-9_-]+):[ \t]*$", RegexOptions.Compiled);

    // A step header inside a job. Six spaces, a dash, then the first key of the step.
    private static readonly Regex StepHeader = new(@"^      - ", RegexOptions.Compiled);

    private static string Workflow() =>
        File.ReadAllText(Path.Join(AppContext.BaseDirectory, "workflows", "publish.yaml"))
            .Replace("\r\n", "\n", StringComparison.Ordinal);

    /// <summary>
    /// The manual trigger declares the flag whose whole purpose is to fail the run.
    /// </summary>
    /// <remarks>
    /// A <c>workflow_dispatch</c> with no input is a second, unguarded way to start
    /// the publish route. The input is what everything below keys on, so its absence
    /// is the failure that makes the other three tests read a file that no longer
    /// says what they think it says.
    /// </remarks>
    [Fact]
    public void TheManualTriggerCarriesTheFlagThatMakesTheRunFail()
    {
        var lines = Workflow().Split('\n');

        var dispatch = Array.FindIndex(lines, line => line == "  workflow_dispatch:");
        Assert.True(
            dispatch >= 0,
            "publish.yaml declares no workflow_dispatch trigger, so the alert job cannot be watched firing "
            + "without a tag of this repository being spent on it (#91).");

        // The trigger's own block: everything indented under it, up to the next key
        // at the same level or the end of the mapping.
        var block = lines
            .Skip(dispatch + 1)
            .TakeWhile(line => line.Length == 0 || line.StartsWith("    ", StringComparison.Ordinal))
            .ToArray();

        Assert.True(
            block.Any(line => line.Trim() == "deliberately-fail:"),
            "the workflow_dispatch trigger declares no `deliberately-fail` input. Without it a manual run "
            + "carries no statement that it exists to fail, and the gate has nothing to admit it on.");

        Assert.True(
            block.Any(line => line.Trim() == "type: boolean"),
            "the `deliberately-fail` input is not typed boolean, so what reaches the gate is whatever "
            + "string the person starting the run typed.");

        Assert.True(
            block.Any(line => line.Trim() == "default: false"),
            "the `deliberately-fail` input does not default to false. A drill is a deliberate act and the "
            + "default answer to `do you want this run to fail` is no.");
    }

    /// <summary>
    /// The gate refuses a manual run that is not the drill.
    /// </summary>
    /// <remarks>
    /// This is the lock that keeps the trigger from being a second way to publish.
    /// The gate refused every non-tag ref outright before the trigger existed; what
    /// replaced that refusal has to refuse the same set minus the drill, and a
    /// dispatch without the flag is in that set.
    /// </remarks>
    [Fact]
    public void TheGateRefusesAManualRunThatIsNotTheDrill()
    {
        var gate = Job("gate");

        Assert.Contains("DELIBERATELY_FAIL: ${{ inputs['deliberately-fail'] }}", gate, StringComparison.Ordinal);

        // The refusal itself: the flag not being `true` on a dispatch ends the run.
        // Matched as the pair of conditions rather than as one line, because either
        // one alone admits something this must not admit.
        Assert.Contains("\"${GITHUB_EVENT_NAME}\" = \"workflow_dispatch\"", gate, StringComparison.Ordinal);

        var body = gate.Split('\n');
        var test = Array.FindIndex(body, line => line.Contains("\"${DELIBERATELY_FAIL}\" != \"true\"", StringComparison.Ordinal));
        Assert.True(
            test >= 0,
            "the gate does not compare `deliberately-fail` against true, so a manual run reaches the publish "
            + "route without ever saying it is the drill.");

        // Inside that branch and no further. `exit 1` is written several times in
        // this job, so a search over the whole script would be satisfied by the
        // tag-only refusal below and would pass for a branch that only warns.
        var ends = body
            .Skip(test + 1)
            .TakeWhile(line => line.Trim() != "fi")
            .Any(line => line.Trim() == "exit 1");
        Assert.True(
            ends,
            "the gate tests `deliberately-fail` and does not end the run when it is unset, so a manual run "
            + "with the flag off walks into the publish route the tag-only rule exists to keep it out of.");

        // The tag-only refusal is still there for a push. The drill is an exception
        // to it, and an exception that swallowed the rule would leave this file
        // reading as though the rule held.
        Assert.Contains(
            "This workflow publishes from a tag only, but it was started from ${GITHUB_REF_TYPE}",
            gate,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// The drill fails before the step that produces the archive.
    /// </summary>
    /// <remarks>
    /// A run that reaches the packaging step has an archive on disk, and the two jobs
    /// after it are the ones that can attest and write. The done-when of #91 asks for
    /// a failure staged before the release job creates anything, and step order is
    /// where that is decided rather than in the prose beside it.
    /// </remarks>
    [Fact]
    public void TheDrillFailsBeforeTheStepThatProducesTheArchive()
    {
        var steps = Steps("build");

        var shortCircuit = steps.FindIndex(step =>
            step.Contains("${{ inputs['deliberately-fail'] }}", StringComparison.Ordinal)
            && step.Contains("exit 1", StringComparison.Ordinal));
        Assert.True(
            shortCircuit >= 0,
            "no step in the build job is guarded by `deliberately-fail` and ends the run, so a manual run "
            + "with the flag set would build and package exactly as a release does.");

        var packaging = steps.FindIndex(step =>
            step.Contains("jellyfin-plugin-repository-manager", StringComparison.Ordinal));
        Assert.True(packaging >= 0, "the build job no longer names the packaging action this ordering is about.");

        Assert.True(
            shortCircuit < packaging,
            "the drill's failure is staged after the packaging step, so a drill run produces the archive "
            + "the release job publishes. #91 asks for the failure before anything is created.");
    }

    /// <summary>
    /// The release job declines a manual run whatever the rest of the file does.
    /// </summary>
    /// <remarks>
    /// The two properties above already make the release job unreachable on a
    /// dispatch. This one holds when they do not, which is the case worth covering:
    /// a later change that moves the short-circuit or widens the gate is a change
    /// nobody makes while thinking about publishing.
    /// </remarks>
    [Fact]
    public void TheReleaseJobDeclinesAManualRun()
    {
        var release = Job("release");

        var condition = release
            .Split('\n')
            .Select(line => line.Trim())
            .FirstOrDefault(line => line.StartsWith("if:", StringComparison.Ordinal));

        Assert.True(
            condition is not null,
            "the release job carries no condition, so it runs on a manual dispatch as readily as on a tag "
            + "push, and it reads GITHUB_REF_NAME as the tag to release.");

        Assert.Equal("if: github.event_name == 'push'", condition);
    }

    private static string Job(string name)
    {
        var lines = Workflow().Split('\n');

        var start = Array.FindIndex(lines, line =>
        {
            var match = JobHeader.Match(line);
            return match.Success && match.Groups["name"].Value == name;
        });
        Assert.True(start >= 0, $"publish.yaml declares no `{name}` job.");

        var body = lines
            .Skip(start + 1)
            .TakeWhile(line => !JobHeader.IsMatch(line))
            .ToArray();

        return string.Join('\n', body);
    }

    private static List<string> Steps(string job)
    {
        var steps = new List<string>();
        var current = new List<string>();

        foreach (var line in Job(job).Split('\n'))
        {
            if (StepHeader.IsMatch(line))
            {
                if (current.Count > 0)
                {
                    steps.Add(string.Join('\n', current));
                }

                current = [];
            }

            if (current.Count > 0 || StepHeader.IsMatch(line))
            {
                current.Add(line);
            }
        }

        if (current.Count > 0)
        {
            steps.Add(string.Join('\n', current));
        }

        return steps;
    }
}
