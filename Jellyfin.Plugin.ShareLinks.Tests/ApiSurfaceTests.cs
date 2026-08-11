using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Xunit;

namespace Jellyfin.Plugin.ShareLinks.Tests;

/// <summary>
/// The routes the assembly serves and the routes <c>docs/api.md</c> describes are
/// one list (#72).
/// </summary>
/// <remarks>
/// <para>
/// A description of an API goes stale the first time somebody adds a route and
/// does not touch the page, and nothing about that is visible in a diff. So the
/// page is not read by a person here: the set is taken off the compiled
/// assembly, the page is parsed, and the two are compared in both directions. A
/// route with no row reds this, and a row naming no route reds it too.
/// </para>
/// <para>
/// Both directions matter for different reasons. The first is the drift the
/// issue is about. The second is what stops the page from describing a route
/// that was removed, which is worse than an undescribed one: somebody scripts
/// against it.
/// </para>
/// </remarks>
public class ApiSurfaceTests
{
    /// <summary>
    /// The rows of the table in the page, as "METHOD /path".
    /// </summary>
    /// <returns>What the page says this plugin serves.</returns>
    private static IReadOnlyList<string> Described()
    {
        var page = Path.Combine(AppContext.BaseDirectory, "api.md");
        Assert.True(File.Exists(page), $"docs/api.md was not copied next to the test assembly: {page}");

        // The rows of the one table that lists routes, which are the lines whose
        // second cell is a path in backticks beginning with a slash.
        var rows = Regex.Matches(
            File.ReadAllText(page),
            @"^\|\s*(?<method>[A-Z]+)\s*\|\s*`(?<path>/[^`]+)`\s*\|",
            RegexOptions.Multiline);

        return rows.Select(row => row.Groups["method"].Value + " " + row.Groups["path"].Value)
            .OrderBy(text => text, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// The routes the assembly actually serves, as "METHOD /path", read off the
    /// same attributes the server routes them by.
    /// </summary>
    /// <returns>What the plugin serves.</returns>
    private static IReadOnlyList<string> Served()
    {
        var served = new List<string>();

        foreach (var controller in typeof(Plugin).Assembly.GetTypes().Where(type => typeof(ControllerBase).IsAssignableFrom(type) && !type.IsAbstract))
        {
            var prefix = string.Join(
                "/",
                controller.GetCustomAttributes<RouteAttribute>(inherit: true)
                    .Select(attribute => attribute.Template?.Trim('/'))
                    .Where(template => !string.IsNullOrEmpty(template)));

            foreach (var action in controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                foreach (var verb in action.GetCustomAttributes(inherit: true).OfType<HttpMethodAttribute>())
                {
                    foreach (var method in verb.HttpMethods)
                    {
                        served.Add(string.Format(
                            CultureInfo.InvariantCulture,
                            "{0} /{1}/{2}",
                            method,
                            prefix,
                            (verb.Template ?? string.Empty).Trim('/')));
                    }
                }
            }
        }

        return served.OrderBy(text => text, StringComparer.Ordinal).ToList();
    }

    /// <summary>
    /// The condition #72 asks for, in both directions.
    /// </summary>
    [Fact]
    public void EveryRouteInTheAssemblyIsDescribedHereAndNothingElseIs()
    {
        var served = Served();

        // An empty set would satisfy the comparison below against an empty page,
        // which is how this check would pass on the day every route was deleted.
        Assert.NotEmpty(served);

        Assert.Equal(served, Described());
    }

    /// <summary>
    /// The set this compares is the same set the guard from #69 judges. Two
    /// walks over one assembly that disagree would leave a route described by one
    /// and judged by neither.
    /// </summary>
    [Fact]
    public void TheDescribedSetIsTheSetTheRouteGuardJudges()
        => Assert.Equal(RoutePolicy.Judge(typeof(Plugin).Assembly).Count, Served().Count);

    /// <summary>
    /// The stability sentence is on the page rather than in somebody's memory.
    /// </summary>
    [Fact]
    public void ThePageSaysWhatStabilityItPromises()
    {
        var page = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "api.md"));

        Assert.Contains(
            "The shape may change, and the version it changed in will be recorded.",
            page,
            StringComparison.Ordinal);
    }
}
