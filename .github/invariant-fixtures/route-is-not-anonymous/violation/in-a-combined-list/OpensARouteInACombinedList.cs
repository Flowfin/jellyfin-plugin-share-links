// Fixture for the invariant route-is-not-anonymous. Violates it on purpose;
// compiled by nothing.
//
// The second spelling. C# lets attributes share one pair of brackets, and the
// route is exactly as anonymous as it is with the attribute standing alone. A
// reading that expects the closing bracket immediately after the name sees
// nothing here, and there is nothing unusual about the file to warn a reviewer.
namespace Jellyfin.Plugin.ShareLinks.InvariantFixtures.Routes;

internal sealed class OpensARouteInACombinedList
{
    [AllowAnonymous, Produces("application/json")]
    public object Resolve(string token) => token;

    [Produces("application/json"), AllowAnonymous]
    public object ResolveAgain(string token) => token;
}
