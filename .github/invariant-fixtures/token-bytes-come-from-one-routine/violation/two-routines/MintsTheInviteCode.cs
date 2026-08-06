// Fixture for the invariant token-bytes-come-from-one-routine, second arm. The
// neighbour of this file declares the same thing about itself. Compiled by
// nothing.
//
// draws token bytes: this file is the one routine (#120)
namespace Jellyfin.Plugin.ShareLinks.InvariantFixtures.OneRoutine.TwoRoutines;

internal sealed class MintsTheInviteCode
{
    public byte[] Mint(int length)
    {
        return RandomNumberGenerator.GetBytes(length);
    }
}
