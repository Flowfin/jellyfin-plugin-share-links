// Near miss for the invariant route-is-not-anonymous. The attribute's name is
// written twice here, in a comment and in a string, and neither is an attribute.
// A pattern matching the bare word would refuse the file that explains the rule.
namespace Jellyfin.Plugin.ShareLinks.InvariantFixtures.Routes;

internal sealed class SaysTheWordWithoutMeaningIt
{
    // Deliberately not AllowAnonymous: the caller is identified by the server
    // before this route is reached.
    [Authorize(Policy = "RequiresElevation")]
    public object Resolve(string shareToken) => Refuse("AllowAnonymous");

    private static object Refuse(string reason) => reason;
}
