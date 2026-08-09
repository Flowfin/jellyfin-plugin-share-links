// Near miss for the invariant share-decision-comes-from-one-routine. This is the
// correct code: one file producing the verdict, saying so, and a second type in
// the same bytes that reads a verdict without producing one. Compiled by nothing.
//
// The reading half is the part worth having here. Every route, every test and
// every future caller names the type, passes it around and branches on it, and a
// check that could not tell those apart from a second decision would be a check
// nobody could satisfy.
//
// decides whether a share resolves: this file is the one routine (#48)
namespace Jellyfin.Plugin.ShareLinks.InvariantFixtures.OneDecision.NearMiss;

internal static class TheOneDecision
{
    public static ShareResolutionResult Resolve(ShareRecord? share, ShareRefusal refusal)
    {
        if (share is null)
        {
            return new ShareResolutionResult(null, refusal);
        }

        return new ShareResolutionResult(share, ShareRefusal.None);
    }
}

internal sealed class ServesWhatTheDecisionAllowed
{
    public string Describe(ShareResolutionResult resolution)
    {
        if (resolution.IsResolved)
        {
            return "the share resolved";
        }

        return resolution.Refusal switch
        {
            ShareRefusal.Expired => "expired",
            ShareRefusal.Revoked => "revoked",
            _ => "refused",
        };
    }
}
