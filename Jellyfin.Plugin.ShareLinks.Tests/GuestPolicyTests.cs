using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Jellyfin.Plugin.ShareLinks;
using MediaBrowser.Model.Users;
using Xunit;

namespace Jellyfin.Plugin.ShareLinks.Tests;

/// <summary>
/// The clause of #57 that asks for the refused capabilities to be refused for a
/// guest, checked against the document that decides them.
/// </summary>
/// <remarks>
/// <para>
/// The values are read out of <c>docs/guest-capabilities.md</c> rather than
/// restated here. A list in a test drifts against the document the same way a
/// list in a document drifts against the code, and the failure has the same
/// shape: an operator reads a table and is told something that is no longer
/// true.
/// </para>
/// <para>
/// What this cannot show is that a switch is honoured once set. The server
/// enforces a policy, and a server that ignored its own would pass everything
/// here. The document says so about itself.
/// </para>
/// </remarks>
public class GuestPolicyTests
{
    // A switch and its value, written either as a table cell, "`Name` false", or
    // as prose, "`Name` is false". Both forms appear in the document and both
    // are a name with its own value next to it, which is the property this
    // pattern is for; a comma-separated list of names sharing one verb is not,
    // and the document was rewritten into rows rather than the pattern widened
    // to guess at one.
    private static readonly Regex Setting = new Regex(
        @"`(?<name>[A-Za-z][A-Za-z0-9_]*)`\s*(?:\|\s*)?(?:is\s+|set\s+to\s+)?`?(?<value>true|false)`?",
        RegexOptions.CultureInvariant);

    private static IReadOnlyDictionary<string, bool> Decided()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "guest-capabilities.md");
        Assert.True(File.Exists(path), $"guest-capabilities.md was not copied next to the test assembly: {path}");

        var decided = new Dictionary<string, bool>(StringComparer.Ordinal);
        foreach (Match match in Setting.Matches(File.ReadAllText(path)))
        {
            var name = match.Groups["name"].Value;
            var value = string.Equals(match.Groups["value"].Value, "true", StringComparison.Ordinal);

            if (decided.TryGetValue(name, out var already))
            {
                Assert.True(
                    already == value,
                    $"docs/guest-capabilities.md gives {name} two different values, so there is no single decision to check.");
                continue;
            }

            decided.Add(name, value);
        }

        // Without this the whole file passes on a document whose table somebody
        // reformatted out of the pattern's reach, and a parser that matches
        // nothing agrees with a policy that sets nothing.
        Assert.True(
            decided.Count >= 15,
            $"docs/guest-capabilities.md parsed to {decided.Count} decided switches, which is fewer than the document holds. The parser and the document have come apart.");

        return decided;
    }

    public static TheoryData<string, bool> DecidedSwitches()
    {
        var data = new TheoryData<string, bool>();
        foreach (var (name, value) in Decided().OrderBy(entry => entry.Key, StringComparer.Ordinal))
        {
            data.Add(name, value);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(DecidedSwitches))]
    public void TheGuestGetsTheValueTheDocumentDecided(string name, bool expected)
    {
        var property = typeof(UserPolicy).GetProperty(name);
        Assert.True(property is not null, $"docs/guest-capabilities.md decides {name} and UserPolicy has no such property.");
        Assert.Equal(typeof(bool), property!.PropertyType);

        Assert.Equal(expected, (bool)property.GetValue(GuestPolicy.Create())!);
    }

    [Fact]
    public void TheGuestCannotJoinASynchronisedPlaybackGroup()
    {
        // Not a switch, so it is not in the theory above. The narrowest value is
        // asserted by name because that is how the document writes it.
        var value = GuestPolicy.Create().SyncPlayAccess;

        Assert.Equal("None", Enum.GetName(value.GetType(), value));
    }

    [Fact]
    public void NothingIsSetThatTheDocumentDoesNotDecide()
    {
        // The other direction. Measured against a fresh policy from the server,
        // so a switch this plugin sets to the value the server already held is
        // invisible here; the document states that bound rather than this test
        // carrying it silently.
        var decided = Decided();
        var server = new UserPolicy();
        var guest = GuestPolicy.Create();

        var moved = typeof(UserPolicy)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.PropertyType == typeof(bool))
            .Where(property => !Equals(property.GetValue(server), property.GetValue(guest)))
            .Select(property => property.Name)
            .Where(name => !decided.ContainsKey(name))
            .ToArray();

        Assert.True(
            moved.Length == 0,
            "GuestPolicy moves a switch docs/guest-capabilities.md does not decide: " + string.Join(", ", moved));
    }

    [Fact]
    public void ApplyingToAnExistingPolicyNarrowsItTheSameWay()
    {
        // The operator-prepared account in docs/guest-accounts.md is an account
        // that already has a policy, so the narrowing has to be the same whether
        // it starts from a fresh policy or from one somebody else set wide.
        var wide = new UserPolicy
        {
            IsAdministrator = true,
            EnableContentDownloading = true,
            EnableRemoteControlOfOtherUsers = true,
            EnableSharedDeviceControl = true,
            EnablePublicSharing = true,
            IsHidden = false,
        };

        GuestPolicy.Apply(wide);

        foreach (var (name, expected) in Decided())
        {
            Assert.Equal(expected, (bool)typeof(UserPolicy).GetProperty(name)!.GetValue(wide)!);
        }
    }
}
