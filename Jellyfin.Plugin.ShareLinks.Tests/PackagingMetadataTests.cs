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

    // Phrases that assert the state of the release history. build.yaml is packaged
    // into every release, so a sentence here about what has or has not been
    // published is written before the release it travels in and refuted by that
    // release the moment it exists. Phrases rather than whole sentences, so a
    // rewording that keeps the claim is still caught, and each one specific enough
    // that a changelog naming where the notes live carries none of them.
    private static readonly string[] ReleaseHistoryClaims =
    [
        "no version",
        "no release",
        "not been published",
        "has been published",
        "have been published",
        "nothing here to read",
        "not yet released",
    ];

    private static string ManifestPath => Path.Combine(AppContext.BaseDirectory, "build.yaml");

    private static string ReadManifest()
    {
        Assert.True(File.Exists(ManifestPath), $"build.yaml was not copied next to the test assembly: {ManifestPath}");
        return File.ReadAllText(ManifestPath);
    }

    private static string ReadmePath => Path.Join(AppContext.BaseDirectory, "README.md");

    // A folded scalar, read as the block of indented lines under the key. The
    // quoted reader below cannot see a field written this way, which is why the
    // changelog was the one manifest field no check here read until it shipped
    // inside a package. A blank line inside the block is kept, so a second
    // paragraph is part of what is judged rather than silently dropped.
    private static string ReadFoldedField(string field)
    {
        var pattern = string.Format(CultureInfo.InvariantCulture, "^{0}:[ \t]*[>|][-+]?[ \t]*\r?\n((?:(?:[ \t]+[^\r\n]*)?\r?\n)*)", Regex.Escape(field));
        var match = Regex.Match(ReadManifest(), pattern, RegexOptions.Multiline);
        Assert.True(match.Success, $"build.yaml declares no folded '{field}' block");
        return match.Groups[1].Value;
    }

    private static string ReadQuotedField(string field)
    {
        var pattern = string.Format(CultureInfo.InvariantCulture, "^{0}:\\s*\"([^\"]*)\"\\s*$", Regex.Escape(field));
        var match = Regex.Match(ReadManifest(), pattern, RegexOptions.Multiline);
        Assert.True(match.Success, $"build.yaml declares no quoted '{field}' field");
        return match.Groups[1].Value;
    }

    // A field read whichever of the three shapes it is written in, for the one test
    // below that is about a value being there at all rather than about what it says.
    // The two readers above each refuse the other's shape, so either of them alone
    // would red a rewrite that is only a rewrite: turning a folded block into a
    // quoted line changes no published byte and must not fail a test about
    // emptiness. The bare scalar is last because the folded reader already matches
    // every `>` and `|` header, so nothing written as a block reaches it.
    private static string ReadFieldInAnyShape(string field)
    {
        var manifest = ReadManifest();

        var quoted = Regex.Match(manifest, string.Format(CultureInfo.InvariantCulture, "^{0}:[ \t]*\"([^\"]*)\"[ \t]*$", Regex.Escape(field)), RegexOptions.Multiline);
        if (quoted.Success)
        {
            return quoted.Groups[1].Value;
        }

        var folded = Regex.Match(manifest, string.Format(CultureInfo.InvariantCulture, "^{0}:[ \t]*[>|][-+]?[ \t]*\r?\n((?:(?:[ \t]+[^\r\n]*)?\r?\n)*)", Regex.Escape(field)), RegexOptions.Multiline);
        if (folded.Success)
        {
            return folded.Groups[1].Value;
        }

        var bare = Regex.Match(manifest, string.Format(CultureInfo.InvariantCulture, "^{0}:[ \t]*([^\r\n]*)$", Regex.Escape(field)), RegexOptions.Multiline);
        Assert.True(bare.Success, $"build.yaml declares no '{field}' field, in any of the three shapes it could be written in");
        return bare.Groups[1].Value;
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
    public void ChangelogClaimsNothingAboutTheReleaseHistory()
    {
        // Found on 2026-08-27, before the first tag, by reading the meta.json the
        // mainline package job produced: the field said no version of this plugin
        // had been published yet, and that string is what a catalogue serves as the
        // changelog of the version it ships in. The served catalogue carries the
        // field per version:
        //   curl -sS https://flowfin.dev/manifest.json | python -c
        //     'import json,sys; [print(v["changelog"]) for e in json.load(sys.stdin) for v in e["versions"]]'
        // A published release is never edited here, so the cost of finding this
        // after the tag is a burnt version number rather than a commit.
        //
        // WHAT THIS CANNOT DO. It holds a vocabulary, not a meaning. A claim about
        // the release history worded outside the list above walks past it, and no
        // reading of this tree could do better: the fact such a sentence would be
        // judged against lives on the forge, and the suite reaches no network.
        var changelog = ReadFoldedField("changelog");

        Assert.False(
            string.IsNullOrWhiteSpace(changelog),
            "build.yaml's changelog block is empty, so every package would ship a catalogue entry with nothing under its changelog");

        foreach (var claim in ReleaseHistoryClaims)
        {
            Assert.DoesNotContain(claim, changelog, StringComparison.OrdinalIgnoreCase);
        }
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
    public void TheAssemblyCarriesTheVersionTheManifestDeclares()
    {
        // build.yaml is the one place the version is written, and
        // Directory.Build.props reads this field for AssemblyVersion. The release
        // gate proves the tag equals this field and then proves the assembly
        // carries the same number, but that second proof is reachable only by
        // pushing a tag, and a tag cannot be taken back. This is the same
        // comparison on every run.
        //
        // It is not a comparison of a value with itself. What it refuses is the
        // derivation breaking: a literal pinned back into Directory.Build.props, or
        // a build.yaml the pattern in it no longer matches, which leaves the
        // property empty and the SDK stamping its own default.
        var declared = ReadQuotedField("version");

        // Four parts, because a Jellyfin plugin version is four parts wherever a
        // server reads it, and because Assembly.GetName().Version is always four:
        // a three part declaration would fail this comparison for a reason that is
        // about the formatting rather than about the number.
        Assert.Matches("^[0-9]+\\.[0-9]+\\.[0-9]+\\.[0-9]+$", declared);
        Assert.Equal(declared, typeof(Plugin).Assembly.GetName().Version?.ToString());
    }

    [Fact]
    public void TheVersionIsNotTheOneThatSaysNoReleaseWasMade()
    {
        // 0.0.0.0 was reserved for a build the release process did not make, and
        // that reservation is retired: the packaging tool builds the plugin itself
        // and is handed no properties, so the only number it can stamp is the one
        // this file holds. What survives the retirement is the smaller half - four
        // zeros is not a version anybody releases, and a manifest that has fallen
        // back to it would hand a catalogue an entry no server can order.
        Assert.NotEqual("0.0.0.0", ReadQuotedField("version"));
    }

    [Fact]
    public void DescriptionIsNotBlank()
    {
        // The catalogue this plugin is published into reads its entry out of the
        // `.meta.json` the packaging step writes beside the archive, and that file
        // is build.yaml's fields copied. Six of them cannot be absent or blank
        // after trimming: guid, name, description, overview, owner and category,
        // read on 2026-08-30 from
        //   gh api repos/Flowfin/hub/contents/internal/identity/identity.go \
        //     --jq .content | base64 -d | grep -n 'var Required'
        //   var Required = []string{"guid", "name", "description", "overview", "owner", "category"}
        //
        // Five of the six are already refused blank here or in
        // PluginIdentityTests, because a test asserting what a field SAYS cannot
        // pass an empty one: the name matches what the running plugin reports, the
        // owner matches the account the readme links to, the overview contains a
        // phrase, the category is one of a list, and the guid parses and equals the
        // plugin's id. The description was refused by nothing. The placeholder test
        // at the top of this file names the template's two description sentences
        // and passes a field with nothing in it at all.
        //
        // WHAT AN EMPTY ONE COSTS IS NOT THIS PLUGIN'S ROW. A blank required field
        // is a refusal the catalogue's generator treats as fatal rather than as a
        // plugin to skip, so its run writes nothing and the address goes on serving
        // the file from the run before it. Every other plugin in that catalogue
        // then stops gaining versions too, and what says so is a generator log
        // nobody on this board reads.
        //
        // The release route reaches this later and does less with it: its gate
        // greps for `^description:` and passes a key whose value is empty, which is
        // the one-character mistake this exists against. It also runs only on a
        // pushed tag, and the tag is the input on that route that cannot be taken
        // back.
        Assert.False(
            string.IsNullOrWhiteSpace(ReadFieldInAnyShape("description")),
            "build.yaml declares a blank description. The catalogue's generator requires that field, refuses the whole run when it is empty rather than skipping this plugin, and publishes nothing at all until it is filled in.");
    }
}
