// Fixture for the invariant route-is-not-anonymous. Violates it on purpose;
// compiled by nothing.
namespace Jellyfin.Plugin.ShareLinks.InvariantFixtures.Routes;

internal sealed class OpensARouteToAnybody
{
    // One attribute is the whole design undone: sharing is for invited guests who
    // sign in, and a route reachable without an identified caller is the
    // anonymous public link the plugin does not have.
    [AllowAnonymous]
    public object Resolve(string token) => token;
}
