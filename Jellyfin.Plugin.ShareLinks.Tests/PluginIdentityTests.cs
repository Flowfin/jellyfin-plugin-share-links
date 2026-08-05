using System;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Model.Serialization;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.ShareLinks.Tests;

/// <summary>
/// The identifier a server uses to tell this plugin from any other is written down
/// twice, once in build.yaml for the packaging step and once in Plugin.cs for the
/// running assembly. Nothing in the build makes the two agree, so these tests are
/// what notices when one moves without the other.
/// </summary>
public class PluginIdentityTests
{
    // The plugin template ships this identifier and every copy of the template
    // starts out claiming it. Two plugins claiming one identifier is a collision a
    // server cannot resolve, so the value is written down here to be refused rather
    // than left as something a reader has to recognise.
    private static readonly Guid TemplateId = Guid.Parse("eb5d7894-8eef-4b36-aa6f-5d124e828ce1");

    private static Plugin CreatePlugin()
    {
        var paths = new Mock<IApplicationPaths>();

        // BasePlugin's constructor joins several of these into paths, and a null
        // one throws before the identifier can be read. Nothing here writes a
        // file, so one scratch directory for all of them is enough.
        paths.SetReturnsDefault(Path.GetTempPath());

        return new Plugin(paths.Object, Mock.Of<IXmlSerializer>());
    }

    private static string ReadBuildManifestField(string field)
    {
        var manifest = Path.Combine(AppContext.BaseDirectory, "build.yaml");
        Assert.True(File.Exists(manifest), $"build.yaml was not copied next to the test assembly: {manifest}");

        var pattern = string.Format(CultureInfo.InvariantCulture, "^{0}:\\s*\"([^\"]*)\"\\s*$", Regex.Escape(field));
        var match = Regex.Match(File.ReadAllText(manifest), pattern, RegexOptions.Multiline);
        Assert.True(match.Success, $"build.yaml declares no quoted '{field}' field");
        return match.Groups[1].Value;
    }

    [Fact]
    public void GuidInBuildManifestParses()
    {
        Assert.True(Guid.TryParse(ReadBuildManifestField("guid"), out var parsed));
        Assert.NotEqual(Guid.Empty, parsed);
    }

    [Fact]
    public void GuidInBuildManifestEqualsPluginId()
    {
        var declared = Guid.Parse(ReadBuildManifestField("guid"));
        Assert.Equal(CreatePlugin().Id, declared);
    }

    [Fact]
    public void ArtifactInBuildManifestNamesTheBuiltAssembly()
    {
        var manifest = Path.Combine(AppContext.BaseDirectory, "build.yaml");
        var artifacts = Regex.Match(File.ReadAllText(manifest), "^artifacts:\\s*\\n-\\s*\"([^\"]*)\"", RegexOptions.Multiline);
        Assert.True(artifacts.Success, "build.yaml declares no quoted artifact");

        var assembly = typeof(Plugin).Assembly.GetName().Name + ".dll";
        Assert.Equal(assembly, artifacts.Groups[1].Value);
    }

    [Fact]
    public void IdIsNotTheTemplateIdentifier()
    {
        Assert.NotEqual(TemplateId, CreatePlugin().Id);
        Assert.NotEqual(TemplateId, Guid.Parse(ReadBuildManifestField("guid")));
    }

    [Fact]
    public void NameIsTheOneAServerLists()
    {
        Assert.Equal("Share Links", CreatePlugin().Name);
    }
}
