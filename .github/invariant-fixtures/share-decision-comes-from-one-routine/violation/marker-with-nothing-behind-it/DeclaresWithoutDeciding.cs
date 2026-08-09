// Fixture for the invariant share-decision-comes-from-one-routine. Violates it on
// purpose; compiled by nothing.
//
// A marker with nothing behind it. The file claims the decision and produces no
// verdict, which is how the rule would be defeated in two changes rather than
// one: lay the marker down here, and the change that adds the second real
// routine afterwards arrives already exempt.
//
// It is also what a rename leaves behind, and that is the ordinary way to meet
// this arm rather than the adversarial one.
//
// decides whether a share resolves: this file is the one routine (#48)
namespace Jellyfin.Plugin.ShareLinks.InvariantFixtures.OneDecision.MarkerOnly;

internal sealed class DeclaresWithoutDeciding
{
    public string Describe(ShareResolutionResult resolution)
    {
        return resolution.IsResolved ? "resolved" : "refused";
    }
}
