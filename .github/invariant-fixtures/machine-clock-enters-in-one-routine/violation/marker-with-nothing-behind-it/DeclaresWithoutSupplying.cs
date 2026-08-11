// Fixture for the invariant machine-clock-enters-in-one-routine. It violates that
// invariant on purpose and no project compiles it. The marker is here and nothing
// behind it reaches the machine clock, so a later file could read the clock and
// point at this one as the place that was allowed to.
namespace Jellyfin.Plugin.ShareLinks.InvariantFixtures.MachineClock;

// supplies the machine clock: this file is the one routine (#68)

internal sealed class DeclaresWithoutSupplying
{
    private readonly TimeProvider _clock;

    public DeclaresWithoutSupplying(TimeProvider clock) => _clock = clock;

    public bool HasExpired(DateTimeOffset expiresAt) => _clock.GetUtcNow() >= expiresAt;
}
