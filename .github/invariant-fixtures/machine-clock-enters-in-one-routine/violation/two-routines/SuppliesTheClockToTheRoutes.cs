// Fixture for the invariant machine-clock-enters-in-one-routine. It violates that
// invariant on purpose and no project compiles it. This file and its neighbour
// both declare themselves the one routine, which is one more than there may be.
namespace Jellyfin.Plugin.ShareLinks.InvariantFixtures.MachineClock;

// supplies the machine clock: this file is the one routine (#68)

internal static class SuppliesTheClockToTheRoutes
{
    public static TimeProvider ForTheRoutes() => TimeProvider.System;
}
