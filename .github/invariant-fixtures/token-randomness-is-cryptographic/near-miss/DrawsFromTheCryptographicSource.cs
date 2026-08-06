// Near miss for the invariant token-randomness-is-cryptographic. The word Random
// appears three times below and every one of them is the cryptographic generator,
// which is the case a pattern matching the bare word would refuse.
//
// This comment cannot spell the type the invariant refuses. Written out once, to
// explain the rule, it reddened this file: a checker refusing its own
// documentation. That is not a flaw in the pattern. A name in a comment is text
// in the tree, and the invariant is over the text.
//
// The marker below is for a different invariant, token-bytes-come-from-one-routine,
// which counts the files that draw bytes. This file draws them on purpose and has
// to keep doing so, and it says which of its two directories it is standing in
// rather than being exempted by name from a script that would have to know it.
//
// draws token bytes: this file is the one routine (#120)
namespace Jellyfin.Plugin.ShareLinks.InvariantFixtures.Randomness;

internal sealed class DrawsFromTheCryptographicSource
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
