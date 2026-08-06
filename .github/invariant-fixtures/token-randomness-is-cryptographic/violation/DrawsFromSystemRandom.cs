// Fixture for the invariant token-randomness-is-cryptographic. Violates it on
// purpose; compiled by nothing.
namespace Jellyfin.Plugin.ShareLinks.InvariantFixtures.Randomness;

internal sealed class DrawsFromSystemRandom
{
    public byte[] Mint(int length)
    {
        // Seeded from a source somebody can reason about, and documented as
        // unsuitable for anything that has to be unguessable.
        var random = new Random();
        var bytes = new byte[length];
        random.NextBytes(bytes);
        return bytes;
    }
}
