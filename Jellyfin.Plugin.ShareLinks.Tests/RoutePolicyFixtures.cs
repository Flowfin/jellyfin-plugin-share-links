using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MediaBrowser.Common.Api;

namespace Jellyfin.Plugin.ShareLinks.Tests;

/// <summary>
/// Controllers that exist only to be judged (#69).
/// </summary>
/// <remarks>
/// <para>
/// A guard over the plugin's own routes proves nothing while the plugin has no
/// routes, and it would go on proving nothing on the day somebody adds one and
/// the guard turns out to have been wrong all along. These are the controllers
/// the guard is shown to bite on. They live in the test assembly, so nothing
/// here is compiled into the plugin or reachable on a server.
/// </para>
/// <para>
/// Each violation is a mistake somebody actually makes: the attribute left off,
/// the policy name mistyped by one character, an action opened up under an
/// attribute that does not read as anonymous, and an action inherited from a
/// base controller where the attribute was on neither.
/// </para>
/// </remarks>
public static class RoutePolicyFixtures
{
    /// <summary>
    /// The shape the administrator routes take (#67).
    /// </summary>
    [Authorize(Policy = Policies.RequiresElevation)]
    [Route("ShareLinks/Fixtures/Administrator")]
    public class AnAdministratorRoute : ControllerBase
    {
        /// <summary>
        /// Creates nothing.
        /// </summary>
        /// <returns>An empty success.</returns>
        [HttpPost("Create")]
        public IActionResult Create() => Ok();
    }

    /// <summary>
    /// The shape the guest route takes (#68): the default policy, which is a
    /// caller the server has signed in and nothing more.
    /// </summary>
    [Authorize]
    [Route("ShareLinks/Fixtures/Guest")]
    public class AGuestRoute : ControllerBase
    {
        /// <summary>
        /// Opens nothing.
        /// </summary>
        /// <returns>An empty success.</returns>
        [HttpGet("Open")]
        public IActionResult Open() => Ok();
    }

    /// <summary>
    /// The attribute is simply absent. This is the case the issue is named
    /// after: it looks exactly like a controller that carries one.
    /// </summary>
    [Route("ShareLinks/Fixtures/Forgotten")]
    public class ARouteWhoseAuthorizationAttributeIsMissing : ControllerBase
    {
        /// <summary>
        /// Lists nothing.
        /// </summary>
        /// <returns>An empty success.</returns>
        [HttpGet("List")]
        public IActionResult List() => Ok();
    }

    /// <summary>
    /// The policy name is one character short of the server's. Nothing refuses
    /// the string, and a policy the server does not know is not a policy that
    /// admits administrators.
    /// </summary>
    [Authorize(Policy = "RequiresElevatio")]
    [Route("ShareLinks/Fixtures/Mistyped")]
    public class ARouteWhosePolicyNameIsMistyped : ControllerBase
    {
        /// <summary>
        /// Revokes nothing.
        /// </summary>
        /// <returns>An empty success.</returns>
        [HttpPost("Revoke")]
        public IActionResult Revoke() => Ok();
    }

    /// <summary>
    /// The controller is authorized and one action underneath it is opened up.
    /// </summary>
    /// <remarks>
    /// The attribute is deliberately not spelled the ordinary way. The greppable
    /// invariant lint refuses the literal text of the ordinary spelling, in the
    /// test sources as well as the plugin's, and that refusal is the point:
    /// between the two guards, the common spelling is caught by the text and the
    /// spelling the text cannot see is caught here.
    /// </remarks>
    [Authorize]
    [Route("ShareLinks/Fixtures/Aliased")]
    public class ARouteMadeAnonymousByASubclassedAttribute : ControllerBase
    {
        /// <summary>
        /// Answers anybody.
        /// </summary>
        /// <returns>An empty success.</returns>
        [HttpGet("Open")]
        [OpenToAnybodyUnderAnotherName]
        public IActionResult Open() => Ok();
    }

    /// <summary>
    /// A base controller of the plugin's own, carrying the action.
    /// </summary>
    public abstract class ABaseControllerCarryingTheAction : ControllerBase
    {
        /// <summary>
        /// Resolves nothing.
        /// </summary>
        /// <returns>An empty success.</returns>
        [HttpGet("Resolve")]
        public IActionResult Resolve() => Ok();
    }

    /// <summary>
    /// The action is inherited and the attribute is on neither half. A guard
    /// that reads only the methods a controller declares itself finds no action
    /// here at all and reports a clean controller.
    /// </summary>
    [Route("ShareLinks/Fixtures/Inherited")]
    public class ARouteInheritedFromABaseController : ABaseControllerCarryingTheAction
    {
    }

    /// <summary>
    /// An attribute that is anonymous in metadata and does not say so in text.
    /// </summary>
    [System.AttributeUsage(System.AttributeTargets.Class | System.AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
    public sealed class OpenToAnybodyUnderAnotherNameAttribute : AllowAnonymousAttribute
    {
    }
}
