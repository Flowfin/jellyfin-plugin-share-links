// Fixture for the invariant share-decision-comes-from-one-routine. Violates it on
// purpose; compiled by nothing.
//
// The playback half of the pair. It answers the same question as its neighbour
// and answers it differently: this one has no opinion about revocation, which is
// exactly the divergence the rule exists against and exactly the divergence
// nobody notices while both files are green.
//
// decides whether a share resolves: this file is the one routine (#48)
namespace Jellyfin.Plugin.ShareLinks.InvariantFixtures.OneDecision.TwoRoutines;

internal sealed class DecidesForThePlaybackRoute
{
    public ShareResolutionResult Resolve(ShareRecord? share, Guid caller)
    {
        if (share is null || !share.InvitedUserIds.Contains(caller))
        {
            return new ShareResolutionResult(null, ShareRefusal.CallerNotInvited);
        }

        return new ShareResolutionResult(share, ShareRefusal.None);
    }
}
