// Near miss for the invariant token-bytes-come-from-one-routine. This is the
// correct code: one file, drawing from the cryptographic generator, saying so.
// Compiled by nothing.
//
// It carries the same type, the same call and the same words as the violation
// next to it, and the only difference is the marker line. A check that refused
// this would be a check nobody could satisfy, because the property is that one
// file does this rather than that no file does.
//
// draws token bytes: this file is the one routine (#120)
namespace Jellyfin.Plugin.ShareLinks.InvariantFixtures.OneRoutine.NearMiss;

internal sealed class TheOneRoutine
{
    public byte[] Mint(int length)
    {
        return RandomNumberGenerator.GetBytes(length);
    }

    public void Fill(byte[] buffer)
    {
        System.Security.Cryptography.RandomNumberGenerator.Fill(buffer);
    }
}
