// Fixture for the invariant token-compared-in-constant-time. Violates it on
// purpose; compiled by nothing.
//
// The second reason this invariant refuses. The arm beside it compares with an
// operator, where the secret is on both sides of the comparison and either side
// names it. Here the comparison is a call, the secret is the thing the call is
// made on, and nothing on the argument side says a secret is involved at all.
// That is the shape somebody writes when they already hold the stored hash and
// reach for the method the type offers them.
namespace Jellyfin.Plugin.ShareLinks.InvariantFixtures.ConstantTime;

internal sealed class ComparesTheHashOnTheReceiver
{
    public bool Matches(byte[] storedTokenHash, byte[] presented)
    {
        // Returns as soon as two bytes differ, exactly as the operator does.
        return storedTokenHash.SequenceEqual(presented);
    }

    public bool MatchesTheRecord(ShareRecord record, string presented)
    {
        // Same leak through a member rather than a local, which is how it
        // arrives once the hash is read off a record instead of a parameter.
        return record.TokenHash.Equals(presented, StringComparison.Ordinal);
    }
}
