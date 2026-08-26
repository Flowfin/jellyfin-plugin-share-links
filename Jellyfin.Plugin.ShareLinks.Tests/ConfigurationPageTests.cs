using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
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
    /// Every route the assembly serves, as the verb and the template together.
    /// </summary>
    /// <returns>The action's name against the call a caller makes.</returns>
    /// <remarks>
    /// The template alone stopped being enough the moment two actions shared one.
    /// The create and the listing are both <c>ShareLinks/Shares</c> and differ only
    /// in the verb, so an assertion over templates cannot tell a page that creates
    /// from a page that only lists.
    /// </remarks>
    private static IReadOnlyDictionary<string, string> ServedWithVerb()
    {
        var served = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var controller in PluginAssembly.GetTypes()
            .Where(type => typeof(ControllerBase).IsAssignableFrom(type) && !type.IsAbstract))
        {
            foreach (var action in controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                foreach (var verb in action.GetCustomAttributes(inherit: true).OfType<HttpMethodAttribute>())
                {
                    served[controller.Name + "." + action.Name] = string.Format(
                        CultureInfo.InvariantCulture,
                        "{0} {1}",
                        string.Join(",", verb.HttpMethods),
                        Served()[controller.Name + "." + action.Name]);
                }
            }
        }

        Assert.NotEmpty(served);
        return served;
    }

    /// <summary>
    /// Every call the page makes, as the verb and the address it names, with the
    /// address resolved through the variable the script holds it in.
    /// </summary>
    /// <returns>The calls.</returns>
    private static IReadOnlyList<string> CallsThePageMakes()
    {
        var page = Page();

        var addresses = Regex.Matches(page, @"var (?<name>[A-Za-z][A-Za-z0-9]*) = ""(?<address>ShareLinks/[^""]*)"";")
            .ToDictionary(match => match.Groups["name"].Value, match => match.Groups["address"].Value, StringComparer.Ordinal);

        Assert.NotEmpty(addresses);

        var calls = new List<string>();

        foreach (System.Text.RegularExpressions.Match call in Regex.Matches(page, @"ApiClient\.getJSON\(ApiClient\.getUrl\((?<name>[A-Za-z][A-Za-z0-9]*)"))
        {
            calls.Add("GET " + addresses[call.Groups["name"].Value]);
        }

        foreach (System.Text.RegularExpressions.Match call in Regex.Matches(page, @"ApiClient\.ajax\(\{(?<options>.*?)\}\)", RegexOptions.Singleline))
        {
            var verb = Regex.Match(call.Groups["options"].Value, @"type:\s*""(?<verb>[A-Za-z]+)""");
            var url = Regex.Match(call.Groups["options"].Value, @"url:\s*ApiClient\.getUrl\((?<name>[A-Za-z][A-Za-z0-9]*)");

            Assert.True(verb.Success, "the page makes a call that names no verb");
            Assert.True(url.Success, "the page makes a call whose address is not one of the ones it declares");

            calls.Add(verb.Groups["verb"].Value.ToUpperInvariant() + " " + addresses[url.Groups["name"].Value]);
        }

        // A page that made none would agree with the assertions below by having
        // nothing to disagree with, which is how they would pass on the day the
        // script was deleted.
        Assert.NotEmpty(calls);
        return calls.Distinct(StringComparer.Ordinal).OrderBy(call => call, StringComparer.Ordinal).ToList();
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
    /// Every member of a share the page reads when it draws a row, taken off the
    /// script rather than listed here.
    /// </summary>
    /// <returns>What the page reads off a share.</returns>
    private static IReadOnlyList<string> ValuesThePageReadsOffAShare() =>
        Regex.Matches(Page(), @"\bshare\.(?<member>[A-Za-z][A-Za-z0-9]*)")
            .Select(match => match.Groups["member"].Value)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

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
    /// The create, the listing and the revocation are each called, judged by the
    /// verb as well as the address. The create and the listing share a template and
    /// differ only in the verb, so a comparison over addresses alone reads a page
    /// that only lists as one that creates, which is the whole of #70's remaining
    /// clause passing on nothing.
    /// </summary>
    [Fact]
    public void ThePageCallsTheCreateAsWellAsTheListingAndTheRevocation()
    {
        var served = ServedWithVerb();
        var calls = CallsThePageMakes();

        Assert.Contains(served["ShareLinksAdminController.Create"], calls, StringComparer.Ordinal);
        Assert.Contains(served["ShareLinksAdminController.List"], calls, StringComparer.Ordinal);
        Assert.Contains(served["ShareLinksAdminController.Revoke"], calls, StringComparer.Ordinal);
    }

    /// <summary>
    /// Every call the page makes is a route this plugin serves under that verb. A
    /// page asking for the right address with the wrong verb reaches nothing, and
    /// the failure is a control that does nothing on a server the suite says is
    /// green.
    /// </summary>
    [Fact]
    public void EveryCallThePageMakesIsServedUnderThatVerb()
    {
        var served = ServedWithVerb().Values.ToHashSet(StringComparer.Ordinal);

        foreach (var call in CallsThePageMakes())
        {
            Assert.Contains(call, served, StringComparer.Ordinal);
        }
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
    /// Every value the page reads off a share is a member the listing answers
    /// with. A page reading a member by a name the type does not carry draws an
    /// empty column and says nothing about it, which is the one failure in this
    /// file that an operator meets as missing information rather than as an error.
    /// </summary>
    [Fact]
    public void EveryValueThePageReadsOffAShareIsOneTheListingAnswersWith()
    {
        var answered = typeof(ShareSummary)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(member => member.Name)
            .ToHashSet(StringComparer.Ordinal);

        var read = ValuesThePageReadsOffAShare();
        Assert.NotEmpty(read);

        foreach (var member in read)
        {
            Assert.Contains(member, answered, StringComparer.Ordinal);
        }
    }

    /// <summary>
    /// The view names the ceiling that is actually in force, which is #64's second
    /// clause.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Three things, because dropping any one of them leaves the column saying
    /// something an operator would act wrongly on. The column has to be there; it
    /// has to be written off the member the listing answers with, rather than from
    /// the record's own number that is already in the column beside it; and it has
    /// to carry the name of the ceiling that produced it, because the whole of
    /// this issue is somebody lowering a number that was never the one holding.
    /// </para>
    /// <para>
    /// What this cannot see is the rendered page. Nothing here reaches a browser,
    /// and the remarks at the top of this file say so of every claim in it.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheViewNamesTheCeilingThatIsInForce()
    {
        Assert.Contains(nameof(ShareSummary.AppliedCeilings), ValuesThePageReadsOffAShare(), StringComparer.Ordinal);
        Assert.Contains("<th scope=\"col\">In force</th>", Page(), StringComparison.Ordinal);
        Assert.Contains("cap.Applied", Page(), StringComparison.Ordinal);
    }

    /// <summary>
    /// Each line of the in-force column says whether that ceiling can be met for
    /// the item, which is #286's first clause.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Three things again, and for the same shape of reason as the test above.
    /// The sentence has to be written off the member the listing answers with
    /// rather than off anything already in the row; the condition an operator has
    /// to act on has to be spelled out rather than named, because the name is this
    /// tree's vocabulary and not theirs; and the page has to say what the column
    /// means, because a warning nobody can act on is a warning that teaches people
    /// to ignore the column.
    /// </para>
    /// <para>
    /// What this cannot see is the rendered page. Nothing here reaches a browser,
    /// and the remarks at the top of this file say so of every claim in it.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheViewSaysWhetherTheCeilingInForceCanBeMet()
    {
        var page = Page();

        Assert.Contains("inForce.CanBeMet", page, StringComparison.Ordinal);
        Assert.Contains(nameof(CapReach.NothingCanBeServed), page, StringComparison.Ordinal);
        Assert.Contains("NOTHING CAN BE SERVED", page, StringComparison.Ordinal);
        Assert.Contains("raise the ceiling", page, StringComparison.Ordinal);
    }

    /// <summary>
    /// The states that are not the condition say nothing, so the column is empty
    /// on an ordinary share.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A column that said something on every line is a column an operator stops
    /// reading, and the two members that are absences would be the lines it said
    /// the most reassuring thing on. Neither is a branch of the routine that
    /// writes the column, so neither ever reaches the screen.
    /// </para>
    /// <para>
    /// The comparison is against the branch rather than against the bare name,
    /// and the difference matters: the routine's own remark names all five
    /// members, so a test looking for the name alone would refuse a comment
    /// explaining why two of them are silent.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheStatesThatAreNotTheConditionSayNothingOnThePage()
    {
        var page = Page();

        Assert.Contains("reach === \"" + nameof(CapReach.NothingCanBeServed) + "\"", page, StringComparison.Ordinal);
        Assert.DoesNotContain("reach === \"" + nameof(CapReach.AVersionIsWithinIt) + "\"", page, StringComparison.Ordinal);
        Assert.DoesNotContain("reach === \"" + nameof(CapReach.NoCeilingIsSet) + "\"", page, StringComparison.Ordinal);
    }

    /// <summary>
    /// The two ways of having no ceiling are two sentences on the page rather than
    /// one blank.
    /// </summary>
    /// <remarks>
    /// An account with no ceiling set anywhere and an account this plugin does not
    /// cap at all are repaired in opposite directions: the first by setting one,
    /// the second by understanding that the filter never stands in front of an
    /// account it did not make. A column that showed both as empty would send an
    /// operator to the wrong repair, which is the failure this page's own
    /// paragraph about the state column is written against.
    /// </remarks>
    [Fact]
    public void TheViewTellsTheTwoKindsOfNoCeilingApart()
    {
        var page = Page();

        Assert.Contains(nameof(GuestVerdict.NotAGuestOfThisPlugin), page, StringComparison.Ordinal);
        Assert.Contains("no ceiling is set anywhere", page, StringComparison.Ordinal);
        Assert.Contains("caps nothing", page, StringComparison.Ordinal);
    }

    /// <summary>
    /// The revocation instant is on the administrator view, which is #46's third
    /// clause. An operator looking for a link somebody says has stopped working
    /// needs to see when it was stopped, and the state alone does not carry it: a
    /// share revoked after it had already expired reads as expired.
    /// </summary>
    [Fact]
    public void TheViewShowsWhenAShareWasRevoked()
    {
        Assert.Contains(nameof(ShareSummary.RevokedAt), ValuesThePageReadsOffAShare(), StringComparer.Ordinal);
        Assert.Contains("<th scope=\"col\">Revoked</th>", Page(), StringComparison.Ordinal);
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
    /// The page says the link and the credentials are shown once and that nothing
    /// can produce them again. #70's clause is "shown once with a copy control",
    /// and an operator who reads the panel as a place they can come back to is the
    /// person this sentence is written to.
    /// </summary>
    [Fact]
    public void ThePageSaysTheLinkIsShownOnce()
    {
        var page = Page();

        Assert.Contains("shown once", page, StringComparison.Ordinal);
        Assert.Contains("cannot produce", page, StringComparison.Ordinal);
    }

    /// <summary>
    /// The created link has a control that copies it. The link is
    /// <see cref="ShareTokens.EncodedLength"/> characters of token on the end of an
    /// address, which is not something an operator retypes, and a panel showing it
    /// once without a way to take it is a panel that loses it.
    /// </summary>
    [Fact]
    public void TheCreatedLinkCarriesACopyControl()
    {
        var page = Page();

        Assert.Matches(@"<button\b[^>]*id=""ShareLinksCopyLink""", page);
        Assert.Contains(@"page.querySelector(""#ShareLinksCopyLink"").addEventListener(""click""", page, StringComparison.Ordinal);

        // The control copies the field the link is written into rather than
        // something the page assembled a second time, so what is copied and what is
        // shown cannot differ.
        var handler = Regex.Match(
            page,
            @"#ShareLinksCopyLink""\)\.addEventListener\(""click"",\s*function\s*\(\)\s*\{(?<body>.*?)\n                \}\);",
            RegexOptions.Singleline);

        Assert.True(handler.Success, "the copy control has no click handler to read");
        Assert.Contains("#ShareLinksCreatedLink", handler.Groups["body"].Value, StringComparison.Ordinal);
        Assert.Contains("writeText", handler.Groups["body"].Value, StringComparison.Ordinal);
    }

    /// <summary>
    /// The page that ships carries no link and no credential of its own, so
    /// everything an operator is shown in that panel came back from a create they
    /// made. It is the same property the shares table has, over the answer that
    /// cannot be asked for twice.
    /// </summary>
    [Fact]
    public void TheShippedPageCarriesNoLinkOrCredentialOfItsOwn()
    {
        var page = Page();

        var rows = Regex.Match(page, @"<tbody id=""ShareLinksCredentialRows"">(?<rows>.*?)</tbody>", RegexOptions.Singleline);
        Assert.True(rows.Success, "the page has no table body for the credentials to be written into");
        Assert.Equal(string.Empty, rows.Groups["rows"].Value.Trim());

        var link = Regex.Match(page, @"<input\b[^>]*id=""ShareLinksCreatedLink""[^>]*>", RegexOptions.Singleline);
        Assert.True(link.Success, "the page has no field for the link to be written into");
        Assert.DoesNotContain("value=", link.Value, StringComparison.Ordinal);

        // Hidden as it ships, so a page that never reached a create shows no panel
        // rather than an empty one that reads as a link that failed to arrive.
        Assert.Matches(@"<div id=""ShareLinksCreated""[^>]*\bhidden\b", page);
    }

    /// <summary>
    /// Every value the page reads off a create is a member the create answers
    /// with, in both halves of the answer. This is the same assertion the listing
    /// has, and it matters more here: a member read by a name the type does not
    /// carry writes an empty link into a panel that says it will not be shown
    /// again.
    /// </summary>
    [Fact]
    public void EveryValueThePageReadsOffACreateIsOneTheCreateAnswersWith()
    {
        AssertEveryMemberRead("created", typeof(ShareCreated));
        AssertEveryMemberRead("guest", typeof(GuestCredential));
    }

    /// <summary>
    /// The number a rotation answers with reaches the operator, and it reaches
    /// them off the answer rather than out of the page (#28).
    /// </summary>
    /// <remarks>
    /// <para>
    /// A rotation stops every live share on the server and cannot be undone, so
    /// how many it stopped is the size of what an operator has just done. The
    /// call already answers with that number and a page that dropped it would
    /// leave them told that something happened and not what.
    /// </para>
    /// <para>
    /// Two ways of losing it are judged here, because they fail identically on a
    /// screen. A sentence carrying no value at all leaves the count in the
    /// answer and nowhere else, and a value read by a name the type does not
    /// carry writes <c>undefined</c> into that sentence. The first is caught by
    /// requiring the count in what the message element is written with, the
    /// second by the same member comparison the create and the listing get.
    /// </para>
    /// <para>
    /// What this cannot see is whether an operator reads the sentence, or
    /// whether the browser renders it. Nothing in this repository reaches a
    /// browser, and the remarks at the top of this file say so of every claim
    /// here.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheRotationTellsTheOperatorHowManySharesItStopped()
    {
        AssertEveryMemberRead("rotated", typeof(ShareKeyRotated));

        var written = Regex.Matches(
                Page(),
                @"#ShareLinksRotateMessage""\)\.textContent = (?<sentence>[^;]*);")
            .Select(match => match.Groups["sentence"].Value)
            .ToList();

        Assert.True(
            written.Count > 0,
            "the page writes nothing into the element that reports a rotation, so an operator who pressed it is told nothing at all.");

        Assert.True(
            written.Any(sentence => sentence.Contains(nameof(ShareKeyRotated.SharesStopped), StringComparison.Ordinal)),
            "no sentence the page writes into the rotation message carries the count the call answers with, so an operator is told a rotation happened and not how much it stopped. What the page writes there: "
            + string.Join(" | ", written));
    }

    /// <summary>
    /// The link comes back from one route the page calls and from no other, which
    /// is what "shown once" rests on. The page can only fail to show it again; the
    /// server has to be unable to answer with it, and a second route carrying a
    /// link would make the panel's sentence false without a line of the page
    /// changing.
    /// </summary>
    /// <remarks>
    /// Judged over the answer types of the actions the page calls rather than over
    /// the page. What it cannot see is a link inside a string a route answers with,
    /// or one reachable through a member of a member.
    /// </remarks>
    [Fact]
    public void TheLinkComesBackFromOneRouteThePageCallsAndFromNoOther()
    {
        var served = Served();
        var addresses = AddressesThePageCalls();

        var carrying = PluginAssembly.GetTypes()
            .Where(type => typeof(ControllerBase).IsAssignableFrom(type) && !type.IsAbstract)
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            .Where(action => action.GetCustomAttributes(inherit: true).OfType<HttpMethodAttribute>().Any())
            .Where(action => addresses.Contains(served[action.DeclaringType!.Name + "." + action.Name], StringComparer.Ordinal))
            .Where(action => Answered(action) is { } answer && CarriesALink(answer))
            .Select(action => action.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        Assert.Equal(new[] { "Create" }, carrying);
    }

    // What an action answers with, unwrapped through the task and the action result
    // the route declares it in, because none of those is what a caller reads.
    private static Type? Answered(MethodInfo action)
    {
        var answer = action.ReturnType;

        while (answer.IsGenericType
            && (answer.GetGenericTypeDefinition() == typeof(Task<>)
                || answer.GetGenericTypeDefinition() == typeof(ActionResult<>)))
        {
            answer = answer.GetGenericArguments()[0];
        }

        return answer == typeof(void) ? null : answer;
    }

    private static bool CarriesALink(Type answer)
        => answer.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Any(member => member.PropertyType == typeof(Uri));

    private static void AssertEveryMemberRead(string variable, Type answered)
    {
        var read = Regex.Matches(Page(), @"\b" + variable + @"\.(?<member>[A-Za-z][A-Za-z0-9]*)")
            .Select(match => match.Groups["member"].Value)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        // A page reading none of them would agree with this by having nothing to
        // disagree with, which is how this passes on the day the panel is deleted.
        Assert.NotEmpty(read);

        var carried = answered
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(member => member.Name)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var member in read)
        {
            Assert.Contains(member, carried, StringComparer.Ordinal);
        }
    }
}
