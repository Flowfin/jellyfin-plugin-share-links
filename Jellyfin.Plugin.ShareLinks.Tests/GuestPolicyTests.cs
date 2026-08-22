using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Jellyfin.Database.Implementations.Entities;
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

        Assert.Equal(expected, (bool)property.GetValue(GuestPolicy.Create(GuestPolicy.DefaultMaxActiveSessions))!);
    }

    [Fact]
    public void TheGuestCannotJoinASynchronisedPlaybackGroup()
    {
        // Not a switch, so it is not in the theory above. The narrowest value is
        // asserted by name because that is how the document writes it.
        var value = GuestPolicy.Create(GuestPolicy.DefaultMaxActiveSessions).SyncPlayAccess;

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
        var guest = GuestPolicy.Create(GuestPolicy.DefaultMaxActiveSessions);

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

        GuestPolicy.Apply(wide, GuestPolicy.DefaultMaxActiveSessions);

        foreach (var (name, expected) in Decided())
        {
            Assert.Equal(expected, (bool)typeof(UserPolicy).GetProperty(name)!.GetValue(wide)!);
        }
    }

    /// <summary>
    /// A policy written for an account carries that account's authentication and
    /// password reset providers.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Not a switch this plugin decides, which is exactly why it is asserted. The
    /// server writes a policy onto an account field by field, and these two are
    /// among the fields it writes; both refuse null in its database, so a policy
    /// that arrives without them does not narrow the account, it fails the write.
    /// </para>
    /// <para>
    /// Measured on a running server rather than argued: the create route answered
    /// 500 with `NOT NULL constraint failed: Users.AuthenticationProviderId` in
    /// the log, and no share could be made at all. A doubled user manager takes a
    /// policy and writes it nowhere, so nothing here saw it until the job in #237
    /// did.
    /// </para>
    /// </remarks>
    [Fact]
    public void APolicyForAnAccountCarriesThatAccountsProviders()
    {
        var account = new User("a guest", "an authentication provider", "a reset provider")
        {
            Id = Guid.NewGuid(),
        };

        var policy = GuestPolicy.For(account, GuestPolicy.DefaultMaxActiveSessions);

        Assert.Equal("an authentication provider", policy.AuthenticationProviderId);
        Assert.Equal("a reset provider", policy.PasswordResetProviderId);
    }

    /// <summary>
    /// The ceiling already on the account is what the narrowing rule reads, so a
    /// policy written for an account carrying a lower one keeps the lower one.
    /// </summary>
    /// <remarks>
    /// The same rule <see cref="GuestPolicy.Apply"/> holds, asserted through the
    /// routine the callers use, because that is where the account's own number is
    /// picked up and a caller that looked it up by hand is what this replaced.
    /// </remarks>
    [Fact]
    public void APolicyForAnAccountNarrowsToTheCeilingAlreadyOnIt()
    {
        var account = new User("a guest", "provider", "reset")
        {
            Id = Guid.NewGuid(),
            MaxActiveSessions = 2,
        };

        Assert.Equal(2, GuestPolicy.For(account, 7).MaxActiveSessions);
        Assert.Equal(2, GuestPolicy.For(account, 2).MaxActiveSessions);
    }

    /// <summary>
    /// An account carrying the server's own no-ceiling takes the one asked for.
    /// </summary>
    [Fact]
    public void APolicyForAnAccountCarryingNoCeilingTakesTheOneAskedFor()
    {
        var account = new User("a guest", "provider", "reset") { Id = Guid.NewGuid() };

        Assert.Equal(7, GuestPolicy.For(account, 7).MaxActiveSessions);
    }

    /// <summary>
    /// There is no policy for no account.
    /// </summary>
    [Fact]
    public void APolicyForNoAccountIsRefused()
    {
        Assert.Throws<ArgumentNullException>(() => GuestPolicy.For(null!, GuestPolicy.DefaultMaxActiveSessions));
    }
}
