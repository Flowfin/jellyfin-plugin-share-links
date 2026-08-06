using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using MediaBrowser.Common.Api;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;

namespace Jellyfin.Plugin.ShareLinks.Tests;

/// <summary>
/// What the guard decided about one action (#69).
/// </summary>
public enum RouteVerdict
{
    /// <summary>
    /// The action is reached only by a caller the server has identified as an
    /// administrator. This is the shape the administrator routes take.
    /// </summary>
    RequiresElevation,

    /// <summary>
    /// The action is reached only by a caller the server has already
    /// authenticated, under the default policy. This is the shape the guest
    /// route takes.
    /// </summary>
    RequiresAuthentication,

    /// <summary>
    /// An attribute implementing <see cref="IAllowAnonymous"/> sits on the
    /// action or on its controller, so the server answers it without
    /// identifying the caller.
    /// </summary>
    ReachableWithoutAuthentication,

    /// <summary>
    /// Nothing on the action or its controller says who may call it.
    /// </summary>
    CarriesNoAuthorization,

    /// <summary>
    /// The action names a policy, a role or a scheme that this plugin has not
    /// declared, so what it admits is decided somewhere this repository cannot
    /// read.
    /// </summary>
    CarriesSomethingThisPluginHasNotDeclared
}

/// <summary>
/// One action and what the guard made of it.
/// </summary>
/// <param name="Controller">The controller type's name.</param>
/// <param name="Action">The method's name.</param>
/// <param name="Route">The route text the attributes spell out, or a sentence saying there is none.</param>
/// <param name="Verdict">What the guard decided.</param>
/// <param name="Detail">Why, in the words a reader of a failure message needs.</param>
public sealed record JudgedAction(string Controller, string Action, string Route, RouteVerdict Verdict, string Detail)
{
    /// <summary>
    /// Gets a value indicating whether this action is one the plugin may not ship.
    /// </summary>
    public bool IsRefused => Verdict is not (RouteVerdict.RequiresElevation or RouteVerdict.RequiresAuthentication);
}

/// <summary>
/// Reads an assembly's controller actions and says what each one admits (#69).
/// </summary>
/// <remarks>
/// <para>
/// An authorization attribute that is missing looks exactly like one that is
/// present, in a diff and in review. It stops looking the same to reflection,
/// which reads the metadata the server itself reads rather than the text
/// somebody wrote, so this guard is over the compiled attributes and not over
/// the source.
/// </para>
/// <para>
/// Two shapes are admitted and every other one is refused. Elevation, spelled
/// with the server's own constant so a rename in the server is a compile error
/// here rather than a route that silently admits nobody:
/// </para>
/// <para>
/// <c>grep -oE 'name="F:MediaBrowser[.]Common[.]Api[.]Policies[.]RequiresElevation"' ~/.nuget/packages/jellyfin.common/10.11.11/lib/net9.0/MediaBrowser.Common.xml</c>
/// </para>
/// <para>
/// And the default policy, which is a plain <c>[Authorize]</c> naming no policy
/// at all: the caller is whoever the server has already signed in. Those are the
/// administrator routes and the guest route respectively (#53, #67, #68).
/// </para>
/// <para>
/// What this reaches that the greppable invariant lint cannot: an attribute
/// deriving from <c>AllowAnonymousAttribute</c> under another name, a policy
/// name that is one character wrong, a route that carries nothing at all, and an
/// action inherited from a base controller. The lint reads text and refuses the
/// ordinary spelling of the anonymous attribute, which is why that spelling is
/// not written out here either; this reads metadata. Neither guard replaces the
/// other and both are cheap.
/// </para>
/// <para>
/// What it does not reach. It judges the attributes on the type and the method,
/// so a policy applied by a convention, a filter registered in the server's
/// pipeline, or a route added by anything other than an attribute is invisible
/// to it. It also says nothing about which route is supposed to be the
/// administrator one and which the guest one: it asserts membership of the two
/// declared shapes, and the map from route to expected shape arrives with the
/// routes themselves.
/// </para>
/// </remarks>
public static class RoutePolicy
{
    /// <summary>
    /// How a plain <c>[Authorize]</c> is written in the failure message. It is a
    /// sentence rather than a name so it cannot be mistaken for a policy the
    /// server could have.
    /// </summary>
    public const string TheDefaultPolicy = "(the default policy)";

    /// <summary>
    /// Judges every controller action in an assembly.
    /// </summary>
    /// <param name="assembly">The assembly to read.</param>
    /// <returns>One entry per action, ordered so two runs read the same.</returns>
    public static IReadOnlyList<JudgedAction> Judge(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        return assembly.GetTypes()
            .Where(IsAController)
            .OrderBy(type => type.FullName, StringComparer.Ordinal)
            .SelectMany(Judge)
            .ToList();
    }

    /// <summary>
    /// Judges every action on one controller.
    /// </summary>
    /// <param name="controller">The controller type to read.</param>
    /// <returns>One entry per action, ordered so two runs read the same.</returns>
    public static IReadOnlyList<JudgedAction> Judge(Type controller)
    {
        ArgumentNullException.ThrowIfNull(controller);

        return ActionsOf(controller)
            .OrderBy(action => action.Name, StringComparer.Ordinal)
            .Select(action => JudgeAction(controller, action))
            .ToList();
    }

    /// <summary>
    /// Writes the judged actions out as the table a failure message carries, so
    /// whoever reads the red sees every route and not only the refused one.
    /// </summary>
    /// <param name="judged">What <see cref="Judge(Assembly)"/> returned.</param>
    /// <returns>The table, or a sentence saying the set was empty.</returns>
    public static string Describe(IReadOnlyList<JudgedAction> judged)
    {
        ArgumentNullException.ThrowIfNull(judged);

        if (judged.Count == 0)
        {
            // An empty set is not a clean run and must not read like one.
            return "no controller action was found, so nothing was judged.";
        }

        var text = new StringBuilder();
        foreach (var action in judged)
        {
            text.AppendLine(string.Format(
                CultureInfo.InvariantCulture,
                "{0} {1}.{2}  route: {3}  {4}: {5}",
                action.IsRefused ? "REFUSED" : "ok     ",
                action.Controller,
                action.Action,
                action.Route,
                action.Verdict,
                action.Detail));
        }

        return text.ToString();
    }

    private static bool IsAController(Type type)
        => typeof(ControllerBase).IsAssignableFrom(type) && !type.IsAbstract;

    /// <summary>
    /// The actions of a controller, including any it inherits from a base
    /// controller of this repository's own. The walk stops at the framework,
    /// because <c>ControllerBase</c> and <c>Controller</c> carry public methods
    /// nobody routes and judging them would bury the real routes in noise.
    /// </summary>
    /// <param name="controller">The controller type to read.</param>
    /// <returns>The methods a request can reach.</returns>
    private static IEnumerable<MethodInfo> ActionsOf(Type controller)
    {
        var framework = typeof(ControllerBase).Assembly;
        var seen = new HashSet<string>(StringComparer.Ordinal);

        for (var type = controller; type is not null && type.Assembly != framework && type != typeof(object); type = type.BaseType)
        {
            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                if (method.IsSpecialName || method.GetCustomAttribute<NonActionAttribute>(inherit: true) is not null)
                {
                    continue;
                }

                if (seen.Add(method.ToString() ?? method.Name))
                {
                    yield return method;
                }
            }
        }
    }

    private static JudgedAction JudgeAction(Type controller, MethodInfo action)
    {
        var route = RouteOf(controller, action);

        var anonymous = AttributesOf<IAllowAnonymous>(controller, action).ToList();
        if (anonymous.Count > 0)
        {
            return new JudgedAction(
                controller.Name,
                action.Name,
                route,
                RouteVerdict.ReachableWithoutAuthentication,
                string.Format(
                    CultureInfo.InvariantCulture,
                    "{0} implements IAllowAnonymous, so the server answers this action without identifying the caller",
                    string.Join(", ", anonymous.Select(attribute => attribute.GetType().Name))));
        }

        var authorization = AttributesOf<IAuthorizeData>(controller, action).ToList();
        if (authorization.Count == 0)
        {
            return new JudgedAction(
                controller.Name,
                action.Name,
                route,
                RouteVerdict.CarriesNoAuthorization,
                "no attribute implementing IAuthorizeData sits on the action or on its controller");
        }

        var undeclared = new List<string>();
        var policies = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var data in authorization)
        {
            if (!string.IsNullOrEmpty(data.Roles))
            {
                undeclared.Add("roles: " + data.Roles);
            }

            if (!string.IsNullOrEmpty(data.AuthenticationSchemes))
            {
                undeclared.Add("schemes: " + data.AuthenticationSchemes);
            }

            policies.Add(string.IsNullOrEmpty(data.Policy) ? TheDefaultPolicy : data.Policy);
        }

        undeclared.AddRange(policies
            .Where(policy => policy != TheDefaultPolicy && policy != Policies.RequiresElevation)
            .Select(policy => "policy: " + policy));

        if (undeclared.Count > 0)
        {
            return new JudgedAction(
                controller.Name,
                action.Name,
                route,
                RouteVerdict.CarriesSomethingThisPluginHasNotDeclared,
                string.Join("; ", undeclared));
        }

        return policies.Contains(Policies.RequiresElevation)
            ? new JudgedAction(controller.Name, action.Name, route, RouteVerdict.RequiresElevation, Policies.RequiresElevation)
            : new JudgedAction(controller.Name, action.Name, route, RouteVerdict.RequiresAuthentication, TheDefaultPolicy);
    }

    /// <summary>
    /// The attributes of one kind sitting on an action or anywhere up its
    /// controller's inheritance chain. Both halves matter: a controller-level
    /// attribute covers every action under it, and an action-level one is how a
    /// single route departs from its controller.
    /// </summary>
    /// <typeparam name="T">The attribute interface to collect.</typeparam>
    /// <param name="controller">The controller type.</param>
    /// <param name="action">The action method.</param>
    /// <returns>Every matching attribute, controller first.</returns>
    private static IEnumerable<T> AttributesOf<T>(Type controller, MethodInfo action)
        => controller.GetCustomAttributes(inherit: true).OfType<T>()
            .Concat(action.GetCustomAttributes(inherit: true).OfType<T>());

    private static string RouteOf(Type controller, MethodInfo action)
    {
        var prefixes = controller.GetCustomAttributes<RouteAttribute>(inherit: true)
            .Select(attribute => attribute.Template)
            .Where(template => !string.IsNullOrEmpty(template))
            .ToList();

        var verbs = action.GetCustomAttributes(inherit: true)
            .OfType<HttpMethodAttribute>()
            .ToList();

        var methods = verbs.SelectMany(verb => verb.HttpMethods).Distinct(StringComparer.Ordinal).ToList();
        var templates = verbs.Select(verb => verb.Template)
            .Concat(action.GetCustomAttributes<RouteAttribute>(inherit: true).Select(attribute => attribute.Template))
            .Where(template => !string.IsNullOrEmpty(template))
            .ToList();

        if (methods.Count == 0 && prefixes.Count == 0 && templates.Count == 0)
        {
            return "(no routing attribute)";
        }

        return string.Format(
            CultureInfo.InvariantCulture,
            "{0} {1}",
            methods.Count == 0 ? "(any method)" : string.Join("|", methods),
            string.Join(
                " + ",
                new[] { string.Join(",", prefixes), string.Join(",", templates) }.Where(part => part.Length > 0)));
    }
}
