using System;
using System.Linq;
using System.Reflection;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Model.Users;
using Microsoft.AspNetCore.Mvc.Filters;
using Xunit;

namespace Jellyfin.Plugin.ShareLinks.Tests;

/// <summary>
/// The server members <c>docs/guest-confinement.md</c> compares still exist on the
/// line this plugin compiles against (#52).
/// </summary>
/// <remarks>
/// <para>
/// A comparison of two mechanisms is a set of claims about another artefact, and
/// the artefact is the server. A page arguing against a mechanism that no longer
/// exists, or for one that has been renamed, is a page that reads exactly like a
/// correct one. These are the names it turns on, held here so the page reds the
/// suite instead of going quietly wrong.
/// </para>
/// <para>
/// What this cannot show is how either mechanism behaves. That needs a running
/// server with a library on it, and <c>docs/testing.md</c> is where the rule
/// putting one outside this suite is written. The page says the same about itself.
/// </para>
/// </remarks>
public class GuestConfinementTests
{
    /// <summary>
    /// Gets the account policy members the comparison names, with the type each one has to have.
    /// </summary>
    public static TheoryData<string, string> PolicyMembers => new()
    {
        { "AllowedTags", "String[]" },
        { "BlockedTags", "String[]" },
        { "EnabledFolders", "Guid[]" },
        { "EnableAllFolders", "Boolean" }
    };

    [Theory]
    [MemberData(nameof(PolicyMembers))]
    public void TheAccountPolicyStillCarriesTheMemberTheComparisonNames(string name, string type)
    {
        var property = typeof(UserPolicy).GetProperty(name);

        Assert.True(
            property is not null,
            $"docs/guest-confinement.md names UserPolicy.{name} and there is no such property. The server line moved under the page.");

        Assert.Equal(type, property!.PropertyType.Name);
    }

    [Fact]
    public void TheRoutineThatReadsTheAllowedTagsStillExists()
    {
        // The tag candidate is refused on what it costs rather than on whether it
        // works, so the routine that makes it work has to be there for the refusal
        // to be about anything. It is not public API, which is itself part of the
        // argument: the mechanism is the server's to change.
        var names = typeof(BaseItem)
            .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
            .Select(method => method.Name)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            names.Contains("IsVisibleViaTags", StringComparer.Ordinal),
            "docs/guest-confinement.md names BaseItem.IsVisibleViaTags as what reads the allowed tags, and there is no such method. "
            + "What the type does carry: "
            + string.Join(", ", names.Where(name => name.Contains("Visible", StringComparison.Ordinal)).OrderBy(name => name, StringComparer.Ordinal)));
    }

    [Fact]
    public void ThisPluginCarriesNoFilterYet()
    {
        // The page decides which mechanism is built and is not the building of
        // it. A page that read as a description of working code would be the
        // worse failure, so the absence is asserted rather than left to be
        // inferred, and the first filter to land reds this and sends whoever
        // wrote it to the section that says what the choice does not cover.
        var filters = typeof(Plugin).Assembly
            .GetTypes()
            .Where(type => typeof(IFilterMetadata).IsAssignableFrom(type))
            .Select(type => type.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            filters.Length == 0,
            "This plugin now carries " + string.Join(", ", filters)
            + ", so docs/guest-confinement.md describes a mechanism that has been built and its last section says it has not.");
    }
}
