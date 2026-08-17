using System;
using System.Linq;
using Xunit;

namespace Jellyfin.Plugin.ShareLinks.Tests;

/// <summary>
/// The line <c>docs/negative-capabilities.md</c> collects from #45: no route can
/// move the expiry of an existing record (#47).
/// </summary>
/// <remarks>
/// <para>
/// The last test here is the one that judges this plugin. The ones above it
/// judge the guard, against routines that exist for no other purpose, because
/// what the plugin holds today is a fact about the tree and the guard has to be
/// shown refusing something before a green run over the tree means anything.
/// </para>
/// <para>
/// What this replaces is not a weaker test, it is no test. Until this landed the
/// line was held by the shape of the source: the instant is init-only and every
/// routine that rebuilt a record happened to copy it. That is a description of
/// the tree on the day somebody read it, and nothing refused the next routine
/// that did not copy it.
/// </para>
/// </remarks>
public class ExpiryPolicyTests
{
    [Fact]
    public void TheGuardAcceptsAWriterThatCopiesTheInstantOffTheRecord()
    {
        var judged = Assert.Single(ExpiryPolicy.Judge(typeof(ExpiryPolicyFixtures.AWriterThatCopiesTheInstant)));

        Assert.Equal(ExpiryVerdict.CarriesTheInstantAcross, judged.Verdict);
        Assert.False(judged.IsRefused);
    }

    [Fact]
    public void TheGuardRefusesAWriterThatAssignsTheInstantBesideIt()
    {
        // The near-miss this guard exists for. One line takes its value from the
        // parameter the line under it uses, both are DateTimeOffset, and the
        // mistake compiles.
        var judged = Assert.Single(ExpiryPolicy.Judge(typeof(ExpiryPolicyFixtures.AWriterThatAssignsTheNeighbouringInstant)));

        Assert.Equal(ExpiryVerdict.MovesTheInstant, judged.Verdict);
        Assert.True(judged.IsRefused);
    }

    [Fact]
    public void TheGuardRefusesAWriterThatMovesTheInstantInOneDirectionOnly()
    {
        // Driven with one instant this would pass half the time, which is why
        // the guard drives with one on each side.
        var judged = Assert.Single(ExpiryPolicy.Judge(typeof(ExpiryPolicyFixtures.AWriterThatOnlyEverExtends)));

        Assert.Equal(ExpiryVerdict.MovesTheInstant, judged.Verdict);
        Assert.True(judged.IsRefused);
    }

    [Fact]
    public void TheGuardRefusesAWriterThatMovesTheInstantBehindATask()
    {
        var judged = Assert.Single(ExpiryPolicy.Judge(typeof(ExpiryPolicyFixtures.AWriterThatAnswersThroughATask)));

        Assert.Equal(ExpiryVerdict.MovesTheInstant, judged.Verdict);
        Assert.True(judged.IsRefused);
    }

    [Fact]
    public void TheGuardAcceptsAWriterThatChangesEverythingExceptTheInstant()
    {
        // This rule is about the expiry. A guard that also refused this one
        // would be a general copy check under another name, and revocation,
        // which is a legitimate rewrite, would be refused by it.
        var judged = Assert.Single(ExpiryPolicy.Judge(typeof(ExpiryPolicyFixtures.AWriterThatChangesEverythingElse)));

        Assert.Equal(ExpiryVerdict.CarriesTheInstantAcross, judged.Verdict);
        Assert.False(judged.IsRefused);
    }

    [Fact]
    public void TheGuardRefusesAWriterItCannotDriveRatherThanSkippingIt()
    {
        // A routine nothing was learned about must not read like one that was
        // read and cleared, which is the failure every silently skipping guard
        // has.
        var judged = Assert.Single(ExpiryPolicy.Judge(typeof(ExpiryPolicyFixtures.AWriterTheGuardCannotDrive)));

        Assert.Equal(ExpiryVerdict.CouldNotBeDriven, judged.Verdict);
        Assert.True(judged.IsRefused);
        Assert.Contains("teach it one rather than exempting the routine", judged.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void JudgingAWholeAssemblyReachesANestedTypeAndANonPublicRoutine()
    {
        // The legs above hand the guard a type. This one hands it an assembly,
        // which is what the plugin's own leg does, so the walk is proved rather
        // than assumed: a routine the walk misses is a routine nothing judges.
        // The fixtures are nested types and one of their routines is private,
        // which are the two shapes a walk written for public top-level types
        // would step over.
        var judged = ExpiryPolicy.Judge(typeof(ExpiryPolicyFixtures).Assembly);
        var names = judged.Select(routine => routine.Type + "." + routine.Routine).ToList();

        Assert.Contains("AWriterThatCopiesTheInstant.Revoked", names, StringComparer.Ordinal);
        Assert.Contains("AWriterThatAnswersThroughATask.ExtendedAsync", names, StringComparer.Ordinal);
        Assert.Contains("ExpiryPolicyFixtures.Rebuilt", names, StringComparer.Ordinal);
    }

    [Fact]
    public void TheFailureMessageNamesEveryRoutineItJudgedAndWhatItDecided()
    {
        // A failure that says only "something moved the instant" sends the
        // reader back to reflection by hand.
        var described = ExpiryPolicy.Describe(
            ExpiryPolicy.Judge(typeof(ExpiryPolicyFixtures.AWriterThatAssignsTheNeighbouringInstant)));

        Assert.Contains("REFUSED AWriterThatAssignsTheNeighbouringInstant.Revoked", described, StringComparison.Ordinal);
        Assert.Contains("MovesTheInstant", described, StringComparison.Ordinal);
    }

    [Fact]
    public void AnEmptySetIsDescribedAsAnEmptySetRatherThanACleanRun()
    {
        // A run that judged nothing must not read like a run that judged
        // everything and found nothing wrong.
        Assert.Equal(
            "no routine making a record out of a record was found, so nothing was judged.",
            ExpiryPolicy.Describe(Array.Empty<JudgedProducer>()));
    }

    [Fact]
    public void NoRoutineInThisPluginMovesTheExpiryOfARecordItWasGiven()
    {
        var judged = ExpiryPolicy.Judge(typeof(Plugin).Assembly);
        var refused = judged.Where(routine => routine.IsRefused).ToList();

        // An empty set would pass the count below by finding nothing, which is
        // the reading this line exists to stop.
        Assert.NotEmpty(judged);

        Assert.True(
            refused.Count == 0,
            "The plugin carries a routine that answers with a record expiring at an instant other than the one it was given.\n"
            + ExpiryPolicy.Describe(judged));
    }
}
