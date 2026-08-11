// Fixture for the invariant machine-clock-enters-in-one-routine. It violates that
// invariant on purpose and no project compiles it. The second routine is how this
// arrives in practice: a later surface needs a clock, the first routine is
// somewhere else in the tree, and copying the line is quicker than finding it.
namespace Jellyfin.Plugin.ShareLinks.InvariantFixtures.MachineClock;

// supplies the machine clock: this file is the one routine (#68)

internal static class SuppliesTheClockToTheTasks
{
    public static TimeProvider ForTheTasks() => TimeProvider.System;
}
