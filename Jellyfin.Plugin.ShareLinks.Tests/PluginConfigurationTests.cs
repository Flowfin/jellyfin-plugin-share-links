using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using Jellyfin.Plugin.ShareLinks.Configuration;
using Xunit;

namespace Jellyfin.Plugin.ShareLinks.Tests;

/// <summary>
/// The plugin template ships four demonstration settings and an enum for one of
/// them, and the configuration page renders all four. None of them means anything
/// here. These tests refuse them coming back by accident, and refuse a
/// configuration page that is declared but not shipped.
/// </summary>
public class PluginConfigurationTests
{
    private static readonly Assembly PluginAssembly = typeof(PluginConfiguration).Assembly;

    [Fact]
    public void ConfigurationDeclaresNoProperty()
    {
        // DeclaredOnly, because BasePluginConfiguration brings its own and those are
        // not this repository's to remove.
        var declared = typeof(PluginConfiguration)
            .GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Select(property => property.Name)
            .ToArray();

        Assert.Empty(declared);
    }

    [Fact]
    public void TheTemplateOptionsEnumIsGone()
    {
        var leftovers = PluginAssembly.GetTypes()
            .Where(type => type.Name.Equals("SomeOptions", StringComparison.Ordinal))
            .Select(type => type.FullName)
            .ToArray();

        Assert.Empty(leftovers);
    }

    [Fact]
    public void TheConfigurationPageIsEmbeddedUnderTheNameThePluginAsksFor()
    {
        // Plugin.GetPages builds this name from its own namespace at run time. A page
        // that is renamed or dropped from the project leaves the dashboard asking the
        // server for a resource that is not there.
        var expected = string.Format(
            CultureInfo.InvariantCulture,
            "{0}.Configuration.configPage.html",
            typeof(Plugin).Namespace);

        Assert.Contains(expected, PluginAssembly.GetManifestResourceNames(), StringComparer.Ordinal);
    }

    [Fact]
    public void TheConfigurationPageNamesNoPluginIdentifier()
    {
        // The template page carried the template's guid in a script literal, a third
        // copy of the identifier that nothing kept in step with the other two. The
        // page has no script and needs no identifier; if one ever returns it has to
        // come from a source that cannot drift.
        using var stream = PluginAssembly.GetManifestResourceStream(
            string.Format(CultureInfo.InvariantCulture, "{0}.Configuration.configPage.html", typeof(Plugin).Namespace));
        Assert.NotNull(stream);

        using var reader = new StreamReader(stream);
        var page = reader.ReadToEnd();

        Assert.DoesNotContain("eb5d7894-8eef-4b36-aa6f-5d124e828ce1", page, StringComparison.OrdinalIgnoreCase);
    }
}
