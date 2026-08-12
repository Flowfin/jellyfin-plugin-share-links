using System;
using System.Linq;
using Jellyfin.Plugin.ShareLinks.Configuration;
using MediaBrowser.Model.Users;
using Xunit;

namespace Jellyfin.Plugin.ShareLinks.Tests;

/// <summary>
/// How many sessions one invited guest may hold at once, and what happens to an
/// account that already carries a number of its own (#56).
/// </summary>
/// <remarks>
/// <para>
/// What is asserted here is the number this plugin asks the server for. What
/// happens at the ceiling is the server's behaviour: this plugin counts no
/// sessions and turns nobody away, so whether the newest arrival is refused or
/// somebody watching is displaced is not decided here and is not measured
/// anywhere in this repository. <c>docs/testing.md</c> is why, and
/// <c>docs/guest-capabilities.md</c> states it where an operator meets it.
/// </para>
/// </remarks>
public class GuestSessionCeilingTests
{
    [Fact]
    public void TheCeilingAnOperatorConfiguredIsTheOneTheGuestGets()
    {
        var configuration = new PluginConfiguration { GuestMaxActiveSessions = 7 };

        Assert.Equal(7, GuestPolicy.Create(GuestPolicy.MaxActiveSessionsFrom(configuration)).MaxActiveSessions);
    }

    [Fact]
    public void AFreshConfigurationCarriesTheDefaultTheRoutineDecided()
    {
        // Two spellings of one number would be one number until somebody moved
        // half of it.
        Assert.Equal(GuestPolicy.DefaultMaxActiveSessions, new PluginConfiguration().GuestMaxActiveSessions);
    }

    [Fact]
    public void AGuestIsNotLeftWithTheServersOwnAnswerOfNoCeilingAtAll()
    {
        // The whole of what this issue asks the plugin to do. The server's
        // default is zero, which is unlimited, and a policy assembled without
        // this line reads exactly like one with it until somebody counts the
        // sessions on a shared account.
        var server = new UserPolicy();
        var guest = GuestPolicy.Create(GuestPolicy.DefaultMaxActiveSessions);

        Assert.Equal(0, server.MaxActiveSessions);
        Assert.NotEqual(server.MaxActiveSessions, guest.MaxActiveSessions);
        Assert.InRange(guest.MaxActiveSessions, GuestPolicy.MinimumMaxActiveSessions, GuestPolicy.MaximumMaxActiveSessions);
    }

    [Fact]
    public void ZeroIsRefusedRatherThanReadAsNoCeiling()
    {
        // The value most likely to be typed on purpose in the belief that it
        // means something else. Accepting it would produce an account with no
        // ceiling under a setting whose name says it has one.
        var refusal = GuestPolicy.Refuse(0);

        Assert.NotNull(refusal);
        Assert.Contains("no ceiling at all", refusal, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(21)]
    [InlineData(int.MaxValue)]
    public void AValueOutsideTheBoundsIsRefusedAndTheMessageNamesTheSetting(int configured)
    {
        var configuration = new PluginConfiguration { GuestMaxActiveSessions = configured };

        var thrown = Assert.Throws<ArgumentOutOfRangeException>(() => GuestPolicy.MaxActiveSessionsFrom(configuration));

        // An operator meets this after editing a file by hand, so the message has
        // to name the line to fix rather than the parameter a routine happened to
        // take.
        Assert.Contains(nameof(PluginConfiguration.GuestMaxActiveSessions), thrown.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(GuestPolicy.MinimumMaxActiveSessions)]
    [InlineData(GuestPolicy.DefaultMaxActiveSessions)]
    [InlineData(GuestPolicy.MaximumMaxActiveSessions)]
    public void TheBoundsAdmitTheirOwnEnds(int configured)
    {
        var configuration = new PluginConfiguration { GuestMaxActiveSessions = configured };

        Assert.Null(GuestPolicy.Refuse(configured));
        Assert.Equal(configured, GuestPolicy.MaxActiveSessionsFrom(configuration));
    }

    [Fact]
    public void AnAccountThatAlreadyCarriesALowerCeilingKeepsIt()
    {
        // The operator-prepared account in docs/guest-accounts.md is an account
        // somebody else has already decided a number on. Writing the configured
        // value outright would widen it, which is what #58 is about and is the
        // direction this plugin never moves an account in.
        var prepared = new UserPolicy { MaxActiveSessions = 2 };

        GuestPolicy.Apply(prepared, 5);

        Assert.Equal(2, prepared.MaxActiveSessions);
    }

    [Fact]
    public void AnAccountAlreadyAtTheLowestCeilingKeepsIt()
    {
        // The boundary of the test above, and the one value where the comparison
        // and the reading of zero meet. An account limited to a single device is
        // an operator's deliberate answer, and it sits exactly on the lowest
        // ceiling this plugin will let anybody configure, so it is the account
        // most easily read as carrying no ceiling and widened to the configured
        // number. Two is kept by a comparison that is one character wrong; one is
        // not, and nothing else in this suite stands on that character.
        var prepared = new UserPolicy { MaxActiveSessions = GuestPolicy.MinimumMaxActiveSessions };

        GuestPolicy.Apply(prepared, GuestPolicy.DefaultMaxActiveSessions);

        Assert.Equal(GuestPolicy.MinimumMaxActiveSessions, prepared.MaxActiveSessions);
    }

    [Fact]
    public void AnAccountThatAlreadyCarriesAHigherCeilingIsNarrowedToTheConfiguredOne()
    {
        var prepared = new UserPolicy { MaxActiveSessions = 12 };

        GuestPolicy.Apply(prepared, 5);

        Assert.Equal(5, prepared.MaxActiveSessions);
    }

    [Fact]
    public void AnAccountCarryingTheServersUnlimitedValueTakesTheConfiguredCeiling()
    {
        // Zero is no ceiling rather than the lowest one, so "the lower of the
        // two" read literally would leave every fresh account unlimited, which
        // is the reading that undoes the setting entirely.
        var prepared = new UserPolicy { MaxActiveSessions = 0 };

        GuestPolicy.Apply(prepared, 5);

        Assert.Equal(5, prepared.MaxActiveSessions);
    }

    [Fact]
    public void ApplyingTwiceLeavesTheSameNumber()
    {
        // Two shares invite one account, so the policy is written more than once
        // over the life of that account. A rule that narrowed on every pass would
        // walk a ceiling down to nothing across a guest's second and third
        // invitation.
        var policy = GuestPolicy.Create(5);

        GuestPolicy.Apply(policy, 5);

        Assert.Equal(5, policy.MaxActiveSessions);
    }

    [Fact]
    public void ARefusedCeilingReachesNoPolicy()
    {
        // A refusal that threw after the assignments would leave a half-written
        // policy on an account, which is wider than the one that was there.
        var prepared = new UserPolicy { MaxActiveSessions = 3, IsAdministrator = true };

        Assert.Throws<ArgumentOutOfRangeException>(() => GuestPolicy.Apply(prepared, 0));

        Assert.Equal(3, prepared.MaxActiveSessions);
        Assert.True(prepared.IsAdministrator);
    }

    [Fact]
    public void WritingTheCeilingRefusesAPolicyThatIsNotThere()
    {
        // The guard on the way in, proved rather than read. Without it the next
        // line is a null reference from inside the plugin, on the path that sets
        // up a guest, and the account is left in whatever state the caller found
        // it in with nothing saying which switches were written.
        Assert.Throws<ArgumentNullException>(() => GuestPolicy.Apply(null!, GuestPolicy.DefaultMaxActiveSessions));
    }

    [Fact]
    public void ReadingTheCeilingRefusesAConfigurationThatIsNotThere()
    {
        Assert.Throws<ArgumentNullException>(() => GuestPolicy.MaxActiveSessionsFrom(null!));
    }

    [Fact]
    public void TheCeilingIsPerAccountAndNothingHereTakesAShare()
    {
        // docs/guest-capabilities.md says the ceiling is per account rather than
        // per share, and the reason is the shape of this routine: the number is
        // written onto a policy and a policy belongs to an account. A per-share
        // ceiling would have to arrive through a record, so this refuses one
        // being added without the document being revisited.
        var takesARecord = typeof(GuestPolicy)
            .GetMethods()
            .SelectMany(method => method.GetParameters())
            .Where(parameter => parameter.ParameterType == typeof(ShareRecord))
            .Select(parameter => parameter.Member.Name)
            .ToArray();

        Assert.True(
            takesARecord.Length == 0,
            "GuestPolicy takes a share record in " + string.Join(", ", takesARecord)
            + ", so the ceiling is no longer per account and docs/guest-capabilities.md says it is.");
    }
}
