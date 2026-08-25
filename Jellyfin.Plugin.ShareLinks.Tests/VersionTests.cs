using System;
using System.Reflection;
using Xunit;

namespace Jellyfin.Plugin.ShareLinks.Tests;

/// <summary>
/// docs/versioning.md fixes that one number describes a build, read out of
/// build.yaml and stamped into every place the assembly reports a version. The
/// informational version is the place that can drift on its own, because the SDK
/// composes it rather than copying it, and a build reporting two different numbers
/// is a build whose provenance cannot be settled by reading it.
/// </summary>
public class VersionTests
{
    private static readonly Assembly PluginAssembly = typeof(Plugin).Assembly;

    [Fact]
    public void TheInformationalVersionCarriesTheSameNumberAsTheAssembly()
    {
        var informational = PluginAssembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;

        Assert.NotNull(informational);

        // The SDK appends the commit after a plus sign, and the part in front of it
        // is what a person reads as the version of the thing they are holding.
        var plus = informational.IndexOf('+', StringComparison.Ordinal);
        var declared = plus >= 0 ? informational[..plus] : informational;

        Assert.Equal(PluginAssembly.GetName().Version?.ToString(), declared);
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
