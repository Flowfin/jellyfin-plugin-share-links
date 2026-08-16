using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using Jellyfin.Plugin.ShareLinks.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Model.Serialization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.ShareLinks.Tests;

/// <summary>
/// What the dashboard page asks the server for, and what it offers an operator
/// (#70).
/// </summary>
/// <remarks>
/// <para>
/// Nothing here runs the page. No test in this repository may reach a browser or
/// a server, which <c>docs/testing.md</c> fixes and
/// <c>.github/workflows/headless.yml</c> proves, so every claim below is a claim
/// about the text of the page and never about what a dashboard does with it. That
/// the server hands the page to the browser at all, that <c>ApiClient</c> and
/// <c>Dashboard</c> are there when it loads, and that a control renders, are not
/// measured anywhere and no claim about them is made in either direction.
/// </para>
/// <para>
/// What is worth judging about the text is the part that goes stale in silence. A
/// page naming a route by a path somebody typed keeps naming it after the route
/// moves, and the failure is a button that does nothing on a server the suite says
/// is green. So the addresses, the plugin identifier and the settings are taken
/// off the compiled assembly and compared with what the page spells, in both
/// directions where both directions are defects.
/// </para>
/// </remarks>
public class ConfigurationPageTests
{
    private static readonly Assembly PluginAssembly = typeof(Plugin).Assembly;

    /// <summary>
    /// The page as it ships, read out of the assembly rather than off disk,
    /// because the embedded copy is the one an operator is handed.
    /// </summary>
    /// <returns>The page.</returns>
    private static string Page()
    {
        using var stream = PluginAssembly.GetManifestResourceStream(
            string.Format(CultureInfo.InvariantCulture, "{0}.Configuration.configPage.html", typeof(Plugin).Namespace));
        Assert.NotNull(stream);

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>
    /// Every address of this plugin's own that the page names, as the route
    /// template it is.
    /// </summary>
    /// <returns>What the page calls.</returns>
    private static IReadOnlyList<string> AddressesThePageCalls()
    {
        var addresses = Regex.Matches(Page(), @"""(ShareLinks/[^""]*)""")
            .Select(match => match.Groups[1].Value)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(text => text, StringComparer.Ordinal)
            .ToList();

        // A page that named none would agree with every assertion below by having
        // nothing to disagree with, which is how this file would pass on the day
        // the script was deleted.
        Assert.NotEmpty(addresses);
        return addresses;
    }

    /// <summary>
    /// Every route the assembly serves, as the template a caller writes, against
    /// the action that serves it.
    /// </summary>
    /// <returns>The action's name against its template.</returns>
    private static IReadOnlyDictionary<string, string> Served()
    {
        var served = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var controller in PluginAssembly.GetTypes()
            .Where(type => typeof(ControllerBase).IsAssignableFrom(type) && !type.IsAbstract))
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
                    served[controller.Name + "." + action.Name] = string.Format(
                        CultureInfo.InvariantCulture,
                        "{0}/{1}",
                        prefix,
                        (verb.Template ?? string.Empty).Trim('/'));
                }
            }
        }

        Assert.NotEmpty(served);
        return served;
    }

    /// <summary>
    /// Every input on the page that carries a setting, against the control type it
    /// was given.
    /// </summary>
    /// <returns>The setting's name against the control's type.</returns>
    private static IReadOnlyDictionary<string, string> ControlsOnThePage()
    {
        var controls = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (System.Text.RegularExpressions.Match element in Regex.Matches(Page(), @"<input\b[^>]*>", RegexOptions.Singleline))
        {
            var setting = Regex.Match(element.Value, @"data-setting=""([^""]+)""");
            if (!setting.Success)
            {
                continue;
            }

            var type = Regex.Match(element.Value, @"type=""([^""]+)""");
            Assert.True(type.Success, $"the control for {setting.Groups[1].Value} names no type");

            controls[setting.Groups[1].Value] = type.Groups[1].Value;
        }

        return controls;
    }

    /// <summary>
    /// Every setting the configuration holds, against the type it holds it in.
    /// </summary>
    /// <returns>The setting's name against its type.</returns>
    private static IReadOnlyDictionary<string, Type> Settings() =>
        typeof(PluginConfiguration)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.CanRead && property.CanWrite)
            .ToDictionary(property => property.Name, property => property.PropertyType, StringComparer.Ordinal);

    /// <summary>
    /// The page reaches the server through routes this plugin serves, and through
    /// no address somebody typed from memory. This is the half of #70's first
    /// clause that a reading of the tree can hold: the rows come from a route
    /// rather than from anything the page carries.
    /// </summary>
    [Fact]
    public void EveryAddressThePageCallsIsARouteThisPluginServes()
    {
        var served = Served().Values.ToHashSet(StringComparer.Ordinal);

        foreach (var address in AddressesThePageCalls())
        {
            Assert.Contains(address, served, StringComparer.Ordinal);
        }
    }

    /// <summary>
    /// The listing and the revocation are both called, and each is named by the
    /// template the action carries rather than by a path repeated here. So a route
    /// that moves by one segment reds this and says which action moved.
    /// </summary>
    [Fact]
    public void ThePageCallsTheListingAndTheRevocation()
    {
        var served = Served();
        var addresses = AddressesThePageCalls();

        Assert.Contains(served["ShareLinksAdminController.List"], addresses, StringComparer.Ordinal);
        Assert.Contains(served["ShareLinksAdminController.Revoke"], addresses, StringComparer.Ordinal);
    }

    /// <summary>
    /// Every route the page calls is one the server admits only an administrator
    /// to. The page never asks who is looking at it, so what stops a signed-in
    /// guest from listing every share on the server is the attribute on the action
    /// and nothing on this page.
    /// </summary>
    [Fact]
    public void EveryRouteThePageCallsIsReachedOnlyUnderTheServersOwnElevationPolicy()
    {
        var judged = RoutePolicy.Judge(PluginAssembly);
        var served = Served();

        var called = served
            .Where(route => AddressesThePageCalls().Contains(route.Value, StringComparer.Ordinal))
            .Select(route => route.Key.Split('.')[1])
            .ToList();

        Assert.NotEmpty(called);

        foreach (var action in called)
        {
            var verdict = judged.Single(entry => string.Equals(entry.Action, action, StringComparison.Ordinal));
            Assert.Equal(RouteVerdict.RequiresElevation, verdict.Verdict);
        }
    }

    /// <summary>
    /// #70's last clause, over the server rather than over the page: turning the
    /// script off changes nothing about what the server permits.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The way a page comes to be load-bearing is not that it checks something. It
    /// is that the route grows an input only the page is expected to set, and then
    /// a caller who sets it differently gets something the page would never have
    /// offered. So what is asserted is that every parameter of every action the
    /// page calls is bound out of the route or out of the body, which are the two
    /// things <c>docs/api.md</c> describes to somebody writing a script.
    /// </para>
    /// <para>
    /// What this does not reach: whether the server honours the attributes, and
    /// what a request carries that never becomes a parameter. Neither is readable
    /// here.
    /// </para>
    /// </remarks>
    [Fact]
    public void NoRouteThePageCallsTakesAnInputOnlyThePageWouldKnowToSet()
    {
        var addresses = AddressesThePageCalls();
        var actions = PluginAssembly.GetTypes()
            .Where(type => typeof(ControllerBase).IsAssignableFrom(type) && !type.IsAbstract)
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            .Where(action => action.GetCustomAttributes(inherit: true).OfType<HttpMethodAttribute>().Any())
            .Where(action => addresses.Contains(Served()[action.DeclaringType!.Name + "." + action.Name], StringComparer.Ordinal))
            .ToList();

        Assert.NotEmpty(actions);

        foreach (var parameter in actions.SelectMany(action => action.GetParameters()))
        {
            var bound = parameter.ParameterType == typeof(CancellationToken)
                || parameter.GetCustomAttributes(inherit: true).OfType<FromRouteAttribute>().Any()
                || parameter.GetCustomAttributes(inherit: true).OfType<FromBodyAttribute>().Any();

            Assert.True(
                bound,
                $"{parameter.Member.DeclaringType!.Name}.{parameter.Member.Name} takes {parameter.Name}, which is bound from neither the route nor the body");
        }
    }

    /// <summary>
    /// #70's first clause, the other half: the page carries no share of its own.
    /// The body of the table is empty in the file that ships, so every row an
    /// operator sees was written from what the listing answered.
    /// </summary>
    [Fact]
    public void TheShippedPageCarriesNoShareOfItsOwn()
    {
        var body = Regex.Match(Page(), @"<tbody id=""ShareLinksShareRows"">(?<rows>.*?)</tbody>", RegexOptions.Singleline);

        Assert.True(body.Success, "the page has no table body for the shares to be written into");
        Assert.Equal(string.Empty, body.Groups["rows"].Value.Trim());
    }

    /// <summary>
    /// The controls on the page and the settings the configuration holds are one
    /// list. A setting added to the class and not to the page is a setting an
    /// operator cannot reach, and a control naming a setting that no longer exists
    /// writes a value into a configuration nothing reads.
    /// </summary>
    [Fact]
    public void TheControlsAreTheSettingsTheConfigurationHolds()
        => Assert.Equal(
            Settings().Keys.OrderBy(name => name, StringComparer.Ordinal),
            ControlsOnThePage().Keys.OrderBy(name => name, StringComparer.Ordinal));

    /// <summary>
    /// A number gets a number control and everything else gets a text one. The
    /// page reads an empty number as the absence of a value rather than as a zero,
    /// and it can only do that where the browser agrees the field is a number.
    /// </summary>
    [Fact]
    public void ANumericSettingGetsANumberControl()
    {
        var settings = Settings();

        foreach (var control in ControlsOnThePage())
        {
            var type = Nullable.GetUnderlyingType(settings[control.Key]) ?? settings[control.Key];
            var numeric = type != typeof(string) && type.IsPrimitive || type == typeof(decimal);

            Assert.Equal(numeric ? "number" : "text", control.Value);
        }
    }

    /// <summary>
    /// The identifier the page asks the server for its configuration by is the one
    /// the assembly declares. The template this repository started from carried a
    /// third copy of its own identifier in a script literal and nothing kept it in
    /// step with the other two; this is what refuses that.
    /// </summary>
    [Fact]
    public void TheIdentifierThePageAsksForItsConfigurationByIsThePluginsOwn()
    {
        var declared = Regex.Match(Page(), @"var pluginId = ""(?<id>[^""]+)"";");

        Assert.True(declared.Success, "the page names no plugin identifier, so nothing here can compare one");

        var paths = new Mock<IApplicationPaths>();

        // BasePlugin's constructor joins several of these into paths and a null one
        // throws before the identifier can be read. Nothing here writes a file.
        paths.SetReturnsDefault(Path.GetTempPath());

        Assert.Equal(
            new Plugin(paths.Object, Mock.Of<IXmlSerializer>()).Id.ToString("D", CultureInfo.InvariantCulture),
            declared.Groups["id"].Value);
    }

    /// <summary>
    /// The page says that it enforces nothing, in the words #70's second paragraph
    /// asks for. A reader who takes a hidden button for a permission is the person
    /// this sentence is written to.
    /// </summary>
    [Fact]
    public void ThePageSaysItEnforcesNothing()
    {
        var page = Page();

        Assert.Contains("This page enforces nothing.", page, StringComparison.Ordinal);
        Assert.Contains("checked again on the server", page, StringComparison.Ordinal);
    }

    /// <summary>
    /// The page says a link is never shown on it and why. #70 asks for the created
    /// link with a copy control, this plugin serves no route that creates a share,
    /// and a page silent about that reads as one where the feature is elsewhere.
    /// </summary>
    [Fact]
    public void ThePageSaysWhyNoLinkIsShownOnIt()
        => Assert.Contains("a link is therefore never shown on this page", Page(), StringComparison.Ordinal);
}
