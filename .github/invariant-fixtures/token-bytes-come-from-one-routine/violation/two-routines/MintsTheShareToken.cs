// Fixture for the invariant token-bytes-come-from-one-routine, second arm. This
// file and its neighbour both declare themselves the one routine, which is one
// routine too many. Compiled by nothing.
//
// The arm exists because the first one can be answered by adding the marker, and
// a rule a violation can exempt itself from is not a rule. Two markers in the
// scanned set is a second routine that said so out loud, and it is refused for
// the same reason as one that said nothing.
//
// draws token bytes: this file is the one routine (#120)
namespace Jellyfin.Plugin.ShareLinks.InvariantFixtures.OneRoutine.TwoRoutines;

internal sealed class MintsTheShareToken
{
    public byte[] Mint(int length)
    {
        return RandomNumberGenerator.GetBytes(length);
    }
}
