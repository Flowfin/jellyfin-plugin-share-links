// Fixture for the invariant share-decision-comes-from-one-routine. Violates it on
// purpose; compiled by nothing.
//
// This is the first of the two ways a second decision arrives. A route needs to
// know whether it may serve the item, the conditions are obvious, and each line
// is right: a revoked share is refused, a caller who is not invited is refused.
// Nothing here is a mistake on its own line, which is why a pattern refusing bad
// code would walk past it.
//
// What is lost is that this routine and the real one now have to be edited
// together. The day a condition is added to one of them, the other goes on
// serving under the old rule, and the share that keeps playing is the one
// somebody revoked.
namespace Jellyfin.Plugin.ShareLinks.InvariantFixtures.OneDecision;

internal sealed class DecidesOnItsOwn
{
    public ShareResolutionResult Resolve(ShareRecord? share, Guid caller)
    {
        if (share is null)
        {
            return new ShareResolutionResult(null, ShareRefusal.NoSuchShare);
        }

        if (share.RevokedAt is not null)
        {
            return new ShareResolutionResult(null, ShareRefusal.Revoked);
        }

        return new ShareResolutionResult(share, ShareRefusal.None);
    }
}
