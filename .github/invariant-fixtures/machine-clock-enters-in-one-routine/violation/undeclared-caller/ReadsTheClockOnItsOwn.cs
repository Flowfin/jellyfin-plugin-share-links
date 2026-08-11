// Fixture for the invariant machine-clock-enters-in-one-routine. It violates that
// invariant on purpose and no project compiles it.
namespace Jellyfin.Plugin.ShareLinks.InvariantFixtures.MachineClock;

internal sealed class ReadsTheClockOnItsOwn
{
    // The line somebody writes while wiring one more type up. It is correct on
    // its own, and it is a second place the real clock reaches the tree, so the
    // list of what a test cannot stand beside is no longer one file long.
    private readonly TimeProvider _clock = TimeProvider.System;

    public bool HasExpired(DateTimeOffset expiresAt) => _clock.GetUtcNow() >= expiresAt;
}
