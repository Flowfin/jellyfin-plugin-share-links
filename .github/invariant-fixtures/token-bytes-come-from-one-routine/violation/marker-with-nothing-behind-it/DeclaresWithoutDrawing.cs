// Fixture for the invariant token-bytes-come-from-one-routine, third arm. The
// marker is here and nothing behind it draws anything. Compiled by nothing.
//
// A marker laid down in one change and used in the next is how the exemption
// stops being visible: the diff that adds the second caller then contains no
// marker and reads as ordinary. So a marker with nothing behind it is refused as
// well, and the register fails closed in both directions rather than only in the
// direction somebody was thinking about.
//
// draws token bytes: this file is the one routine (#120)
namespace Jellyfin.Plugin.ShareLinks.InvariantFixtures.OneRoutine.Dangling;

internal sealed class DeclaresWithoutDrawing
{
    public string Encode(byte[] bytes)
    {
        return Convert.ToHexString(bytes);
    }
}
