// Fixture for the invariant route-is-not-anonymous. Violates it on purpose;
// compiled by nothing.
//
// The third spelling. The attribute written out with its namespace is the same
// attribute, and it is what somebody writes when the using directive is missing
// and the editor offers to qualify the name instead of adding one.
namespace Jellyfin.Plugin.ShareLinks.InvariantFixtures.Routes;

internal sealed class OpensARouteFullyQualified
{
    [Microsoft.AspNetCore.Authorization.AllowAnonymous]
    public object Resolve(string token) => token;
}
