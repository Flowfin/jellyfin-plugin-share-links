// Fixture for the invariant clock-comes-from-the-seam. It violates that invariant
// on purpose and no project compiles it.
namespace Jellyfin.Plugin.ShareLinks.InvariantFixtures.Clock;

internal sealed class AsksTheMachineWhatTimeItIs
{
    public bool HasExpired(Share share)
    {
        // Testing this means sleeping, or not testing the boundary. The boundary
        // is the only part of an expiry rule that has ever been wrong.
        return share.ExpiresAt <= DateTimeOffset.UtcNow;
    }
}
