// Near miss for the invariant route-is-not-anonymous. The attribute's name is
// written here in a comment, in a string and inside a longer name in an
// attribute list, and none of the three is the attribute. A pattern matching
// the bare word would refuse the file that explains the rule.
namespace Jellyfin.Plugin.ShareLinks.InvariantFixtures.Routes;

internal sealed class SaysTheWordWithoutMeaningIt
{
    // Deliberately not AllowAnonymous: the caller is identified by the server
    // before this route is reached.
    [Authorize(Policy = "RequiresElevation")]
    public object Resolve(string shareToken) => Refuse("AllowAnonymous");

    // The name carries the word and is not the attribute. A reading that looks
    // inside an attribute list rather than at the name next to the bracket has
    // to tell these apart, and this is where it would go wrong.
    [Authorize(Policy = NotAllowAnonymousGuests)]
    public object ResolveAgain(string shareToken) => shareToken;

    private const string NotAllowAnonymousGuests = "RequiresElevation";

    private static object Refuse(string reason) => reason;
}
