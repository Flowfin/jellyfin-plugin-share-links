// Fixture for the invariant token-compared-in-constant-time. Violates it on
// purpose; compiled by nothing.
namespace Jellyfin.Plugin.ShareLinks.InvariantFixtures.ConstantTime;

internal sealed class ComparesTheHashWithEquals
{
    public bool Matches(byte[] storedTokenHash, byte[] candidateTokenHash)
    {
        // Returns as soon as two bytes differ, so how long it took is a
        // measurement of how much of the secret was right.
        return storedTokenHash == candidateTokenHash;
    }
}
