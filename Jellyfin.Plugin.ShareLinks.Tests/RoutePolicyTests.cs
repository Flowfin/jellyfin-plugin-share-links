using System;
using System.Collections.Generic;
using System.Linq;
using MediaBrowser.Common.Api;
using Xunit;

namespace Jellyfin.Plugin.ShareLinks.Tests;

/// <summary>
/// The plugin answers, from a test, which routes it exposes and what each one
/// requires (#69).
/// </summary>
/// <remarks>
/// <para>
/// The last test here is the one that judges this plugin. The ones above it
/// judge the guard, against controllers that exist for no other purpose, because
/// a guard whose subject set is empty passes for two reasons and only one of
/// them is good. What the plugin's own route surface holds today is a fact about
/// the tree rather than about the guard, and it is stated in the pull request
/// that landed this rather than asserted here, where it would go stale the day a
/// route arrives.
/// </para>
/// </remarks>
public class RoutePolicyTests
{
    [Fact]
    public void TheGuardAcceptsAnAdministratorRouteUnderTheServersOwnPolicyName()
    {
        var judged = Assert.Single(RoutePolicy.Judge(typeof(RoutePolicyFixtures.AnAdministratorRoute)));

        Assert.Equal(RouteVerdict.RequiresElevation, judged.Verdict);
        Assert.False(judged.IsRefused);
        Assert.Equal(Policies.RequiresElevation, judged.Detail);
    }

    [Fact]
    public void TheGuardAcceptsAGuestRouteUnderTheDefaultPolicy()
    {
        var judged = Assert.Single(RoutePolicy.Judge(typeof(RoutePolicyFixtures.AGuestRoute)));

        Assert.Equal(RouteVerdict.RequiresAuthentication, judged.Verdict);
        Assert.False(judged.IsRefused);
    }

    [Fact]
    public void TheGuardRefusesAnActionWhoseAuthorizationAttributeIsMissing()
    {
        var judged = Assert.Single(RoutePolicy.Judge(typeof(RoutePolicyFixtures.ARouteWhoseAuthorizationAttributeIsMissing)));

        Assert.Equal(RouteVerdict.CarriesNoAuthorization, judged.Verdict);
        Assert.True(judged.IsRefused);
    }

    [Fact]
    public void TheGuardRefusesAPolicyNameThatIsOneCharacterWrong()
    {
        // The near-miss the guard exists for. A mistyped policy name compiles,
        // deploys, and is refused by the server at request time rather than by
        // anything here, which is a route nobody can use and nobody was told
        // about.
        var judged = Assert.Single(RoutePolicy.Judge(typeof(RoutePolicyFixtures.ARouteWhosePolicyNameIsMistyped)));

        Assert.Equal(RouteVerdict.CarriesSomethingThisPluginHasNotDeclared, judged.Verdict);
        Assert.Contains("RequiresElevatio", judged.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void TheGuardRefusesAnActionMadeAnonymousByASubclassedAttribute()
    {
        // The shape the text lint cannot see: the metadata says anonymous and
        // the source does not spell it.
        var judged = Assert.Single(RoutePolicy.Judge(typeof(RoutePolicyFixtures.ARouteMadeAnonymousByASubclassedAttribute)));

        Assert.Equal(RouteVerdict.ReachableWithoutAuthentication, judged.Verdict);
        Assert.Contains("OpenToAnybodyUnderAnotherNameAttribute", judged.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void TheGuardReachesAnActionInheritedFromABaseController()
    {
        // Reading only the methods a controller declares itself would find no
        // action here and report the controller clean.
        var judged = Assert.Single(RoutePolicy.Judge(typeof(RoutePolicyFixtures.ARouteInheritedFromABaseController)));

        Assert.Equal("Resolve", judged.Action);
        Assert.Equal(RouteVerdict.CarriesNoAuthorization, judged.Verdict);
    }

    [Fact]
    public void JudgingAWholeAssemblyFindsEveryControllerAndRefusesExactlyTheBadOnes()
    {
        // The legs above hand the guard a type. This one hands it an assembly,
        // which is what the plugin's own leg does, so the enumeration is proved
        // rather than assumed: a controller the walk misses is a controller
        // nothing judges.
        var judged = RoutePolicy.Judge(typeof(RoutePolicyFixtures).Assembly);

        Assert.Equal(
            new[]
            {
                "AGuestRoute.Open",
                "ARouteInheritedFromABaseController.Resolve",
                "ARouteMadeAnonymousByASubclassedAttribute.Open",
                "ARouteWhoseAuthorizationAttributeIsMissing.List",
                "ARouteWhosePolicyNameIsMistyped.Revoke",
                "AnAdministratorRoute.Create"
            },
            Names(judged));

        Assert.Equal(
            new[]
            {
                "ARouteInheritedFromABaseController.Resolve",
                "ARouteMadeAnonymousByASubclassedAttribute.Open",
                "ARouteWhoseAuthorizationAttributeIsMissing.List",
                "ARouteWhosePolicyNameIsMistyped.Revoke"
            },
            Names(judged.Where(action => action.IsRefused).ToList()));
    }

    [Fact]
    public void TheFailureMessageNamesEveryRouteItJudgedAndWhatItDecided()
    {
        // A failure that says only "one route is wrong" sends the reader back to
        // reflection by hand. The message carries the whole surface, so the diff
        // between what was expected and what is there is readable in it.
        var described = RoutePolicy.Describe(RoutePolicy.Judge(typeof(RoutePolicyFixtures).Assembly));

        Assert.Contains("REFUSED ARouteWhoseAuthorizationAttributeIsMissing.List", described, StringComparison.Ordinal);
        Assert.Contains("GET ShareLinks/Fixtures/Forgotten + List", described, StringComparison.Ordinal);
        Assert.Contains("ok      AnAdministratorRoute.Create", described, StringComparison.Ordinal);
        Assert.Contains("POST ShareLinks/Fixtures/Administrator + Create", described, StringComparison.Ordinal);
    }

    [Fact]
    public void AnEmptySetIsDescribedAsAnEmptySetRatherThanACleanRun()
    {
        // A run that judged nothing must not read like a run that judged
        // everything and found nothing wrong.
        Assert.Equal(
            "no controller action was found, so nothing was judged.",
            RoutePolicy.Describe(Array.Empty<JudgedAction>()));
    }

    [Fact]
    public void EveryControllerActionThisPluginExposesCarriesAnExplicitPolicy()
    {
        var judged = RoutePolicy.Judge(typeof(Plugin).Assembly);
        var refused = judged.Where(action => action.IsRefused).ToList();

        Assert.True(
            refused.Count == 0,
            "The plugin exposes a route that is not reached under one of the two policies this plugin declares.\n"
            + RoutePolicy.Describe(judged));
    }

    private static IReadOnlyList<string> Names(IReadOnlyList<JudgedAction> judged)
        => judged.Select(action => action.Controller + "." + action.Action).OrderBy(name => name, StringComparer.Ordinal).ToList();
}
