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

    public bool SameShareById(Guid shareId, Guid other)
    {
        // So is Equals on something that is not a secret. The arm that reads the
        // thing a comparison is made on has to tell this from the same call made
        // on a hash, and a pattern that refused every Equals would refuse this.
        return shareId.Equals(other);
    }
}
