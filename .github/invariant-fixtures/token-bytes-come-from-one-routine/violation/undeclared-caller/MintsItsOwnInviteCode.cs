// Fixture for the invariant token-bytes-come-from-one-routine. Violates it on
// purpose; compiled by nothing.
//
// This is the first of the two ways a second caller arrives: a type that needs
// unguessable bytes, reaches for the generator because that is the correct source
// to reach for, and never learns that the tree already has a routine deciding the
// length and the encoding. Nothing here is wrong on its own line, which is why a
// pattern refusing a bad source would not catch it.
namespace Jellyfin.Plugin.ShareLinks.InvariantFixtures.OneRoutine;

internal sealed class MintsItsOwnInviteCode
{
    public string Mint()
    {
        var bytes = RandomNumberGenerator.GetBytes(16);
        return Convert.ToHexString(bytes);
    }
}
