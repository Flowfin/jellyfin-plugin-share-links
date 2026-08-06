// Near miss for the invariant token-compared-in-constant-time. The names here
// are the same names the violation carries and the comparison is the correct
// one, which is the case a pattern written slightly wrong would refuse.
namespace Jellyfin.Plugin.ShareLinks.InvariantFixtures.ConstantTime;

internal sealed class ComparesInConstantTime
{
    public bool Matches(byte[] storedTokenHash, byte[] candidateTokenHash)
    {
        return CryptographicOperations.FixedTimeEquals(storedTokenHash, candidateTokenHash);
    }

    public bool SameShare(Guid shareId, Guid other)
    {
        // An ordinary comparison over something that is not a secret is ordinary.
        return shareId == other;
    }
}
