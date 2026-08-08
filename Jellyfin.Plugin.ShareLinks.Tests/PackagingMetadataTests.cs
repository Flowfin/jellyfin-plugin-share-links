using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Model.Serialization;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.ShareLinks.Tests;

/// <summary>
/// build.yaml is what a catalogue shows somebody before they install anything, and
/// it starts life as the plugin template's placeholder text. Nothing in the build
/// refuses that text: a package made from the template manifest builds, installs
/// and lists, it just describes a different plugin. These tests are what refuses
/// it, field by field, so a placeholder that comes back comes back red.
/// </summary>
public class PackagingMetadataTests
{
    // The template's own values, written down to be refused rather than left as
    // something a reader has to recognise. Each one is distinctive enough that a
    // substring search over the manifest cannot match anything else in it, which
    // is why the owner and the name carry their field prefix: "jellyfin" alone
    // appears in the artifact name, and "Template" alone would match a sentence.
    private static readonly string[] TemplatePlaceholders =
    [
        "name: \"Template\"",
        "owner: \"jellyfin\"",
        "version: \"1.0.0.0\"",
        "Short description about your plugin",
        "This is a longer description that can span more than one",
        "line and include details about your plugin.",
    ];

    private static string ManifestPath => Path.Combine(AppContext.BaseDirectory, "build.yaml");

    private static string ReadManifest()
    {
        Assert.True(File.Exists(ManifestPath), $"build.yaml was not copied next to the test assembly: {ManifestPath}");
        return File.ReadAllText(ManifestPath);
    }

    private static string ReadmePath => Path.Combine(AppContext.BaseDirectory, "README.md");

    private static string ReadQuotedField(string field)
    {
        var pattern = string.Format(CultureInfo.InvariantCulture, "^{0}:\\s*\"([^\"]*)\"\\s*$", Regex.Escape(field));
        var match = Regex.Match(ReadManifest(), pattern, RegexOptions.Multiline);
        Assert.True(match.Success, $"build.yaml declares no quoted '{field}' field");
        return match.Groups[1].Value;
    }

    // The GitHub account the readme links to, taken from every link in it that
    // addresses an account page rather than a repository. A link carrying a second
    // path segment is a repository and is left alone, so a readme that comes to
    // point at the server project's own sources does not red the test below.
    private static string AccountTheReadmeLinksTo()
    {
        Assert.True(File.Exists(ReadmePath), $"README.md was not copied next to the test assembly: {ReadmePath}");

        // A GitHub account name is alphanumeric with interior hyphens, and the
        // trailing guard is what makes the segment the whole path: a URL followed by
        // a slash addresses something inside the account and is skipped.
        var named = Regex.Matches(File.ReadAllText(ReadmePath), "https://github\\.com/([A-Za-z0-9](?:[A-Za-z0-9-]*[A-Za-z0-9])?)(?![A-Za-z0-9-/])")
            .Select(m => m.Groups[1].Value)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        Assert.True(named.Count > 0, "README.md links to no GitHub account, so there is nothing to hold the manifest's owner against");
        Assert.Single(named);

        return named[0];
    }

    [Fact]
    public void ManifestCarriesNoTemplatePlaceholder()
    {
        var manifest = ReadManifest();

        foreach (var placeholder in TemplatePlaceholders)
        {
            Assert.DoesNotContain(placeholder, manifest, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void ChangelogSaysSomethingOtherThanItsOwnName()
    {
        // The template's changelog is the word "changelog" in a folded block, which
        // no whole-file search can refuse without also matching the field name.
        var repeated = Regex.IsMatch(ReadManifest(), "^changelog:\\s*[>|][-+]?\\s*\\r?\\n\\s*changelog\\s*\\r?\\n?$", RegexOptions.Multiline);
        Assert.False(repeated, "build.yaml's changelog is still the template's, which is the word changelog and nothing else");
    }

    [Fact]
    public void NameIsTheOneTheRunningPluginReports()
    {
        // A catalogue entry and a dashboard entry under two different names are two
        // plugins as far as anybody reading them is concerned.
        var paths = new Mock<IApplicationPaths>();
        paths.SetReturnsDefault(Path.GetTempPath());
        var plugin = new Plugin(paths.Object, Mock.Of<IXmlSerializer>());

        Assert.Equal(plugin.Name, ReadQuotedField("name"));
    }

    [Fact]
    public void OwnerIsTheAccountTheReadmeNames()
    {
        // The owner is rendered next to the plugin in the catalogue, and the readme
        // is what a reader meets in the repository. Two documents naming two parties
        // leave nobody a way to tell which one the plugin belongs to.
        //
        // The account is read out of the readme rather than written down here. A
        // literal in this file is a third place that has to move when the account
        // does, and it is the one nobody remembers: this test held the previous
        // account name and refused the manifest for carrying the current one.
        Assert.Equal(AccountTheReadmeLinksTo(), ReadQuotedField("owner"));
    }

    [Fact]
    public void OverviewSaysWhoAShareIsFor()
    {
        // The line in the catalogue list is the only thing many people will read,
        // and the one thing it cannot leave out is that a share is for a guest the
        // operator invited rather than for anybody holding the link.
        Assert.Contains("invited guests", ReadQuotedField("overview"), StringComparison.Ordinal);
    }

    [Fact]
    public void CategoryIsOneThePublishedCatalogueCarries()
    {
        // Derived on 2026-08-06 from the published manifest, so it is the set a
        // catalogue actually groups by rather than a set invented here:
        //   curl -sSL https://repo.jellyfin.org/files/plugin/manifest.json \
        //     | grep -o '"category": *"[^"]*"' | sort -u
        string[] published =
        [
            "Administration",
            "Anime",
            "Books",
            "General",
            "LiveTV",
            "MoviesAndShows",
            "Music",
            "Subtitles",
        ];

        Assert.Contains(ReadQuotedField("category"), published);
    }

    [Fact]
    public void VersionIsTheReservedOne()
    {
        // docs/versioning.md reserves 0.0.0.0 for a build the release process did
        // not make, and a release supplies its version on the command line. A
        // manifest holding any other number gives a hand-built package a version
        // that looks like a release.
        Assert.Equal("0.0.0.0", ReadQuotedField("version"));
    }
}
