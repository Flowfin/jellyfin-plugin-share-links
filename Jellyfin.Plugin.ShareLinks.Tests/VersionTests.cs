using System;
using System.Reflection;
using Xunit;

namespace Jellyfin.Plugin.ShareLinks.Tests;

/// <summary>
/// docs/versioning.md reserves 0.0.0.0 for builds the release process did not
/// make. The reservation is only worth anything if a build that reports it also
/// says so, because four zeros on their own read as an old release just as easily
/// as they read as no release at all.
/// </summary>
public class VersionTests
{
    private static readonly Assembly PluginAssembly = typeof(Plugin).Assembly;

    private static readonly Version Unreleased = new Version(0, 0, 0, 0);

    [Fact]
    public void AnUnreleasedBuildSaysSoAndAReleasedOneDoesNot()
    {
        var informational = PluginAssembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;

        Assert.NotNull(informational);

        // The two directions are one test because they are one rule, and a test
        // asserting only the first would pass on a release build that had quietly
        // kept the marker.
        if (PluginAssembly.GetName().Version == Unreleased)
        {
            Assert.StartsWith("0.0.0.0-unreleased", informational, StringComparison.Ordinal);
        }
        else
        {
            Assert.DoesNotContain("unreleased", informational, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void TheBuildNamesTheCommitItCameFrom()
    {
        // Two builds of one commit matching is worth little if the artefact does
        // not say which commit that was. The commit comes from the SDK's source
        // information, not from ContinuousIntegrationBuild: turning that setting
        // off leaves this test green, which is why the reproducibility half is
        // checked by hashing two builds and not by any test in this file.
        var informational = PluginAssembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;

        Assert.NotNull(informational);

        var plus = informational.IndexOf('+', StringComparison.Ordinal);
        Assert.True(plus >= 0, $"the informational version carries no commit: {informational}");

        var commit = informational[(plus + 1)..];
        Assert.Equal(40, commit.Length);
        Assert.All(commit, character => Assert.True(Uri.IsHexDigit(character), $"not a commit: {commit}"));
    }
}
