// Fixture for the invariant share-decision-comes-from-one-routine. Violates it on
// purpose; compiled by nothing.
//
// The second way a second decision arrives, and the harder one to see in review:
// the marker is copied along with the code, so both files say they are the one
// routine and the sentence stops meaning anything. This is the guest half of the
// pair.
//
// decides whether a share resolves: this file is the one routine (#48)
namespace Jellyfin.Plugin.ShareLinks.InvariantFixtures.OneDecision.TwoRoutines;

internal sealed class DecidesForTheGuestRoute
{
    public ShareResolutionResult Resolve(ShareRecord? share)
    {
        if (share is null)
        {
            return new ShareResolutionResult(null, ShareRefusal.NoSuchShare);
        }

        return new ShareResolutionResult(share, ShareRefusal.None);
    }
}
