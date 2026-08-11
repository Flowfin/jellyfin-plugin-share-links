// Near miss for the invariant machine-clock-enters-in-one-routine. The type is
// named four times, its namespace is spelled out beside it, and the word the
// invariant turns on is in the file twice. Nothing here reaches the machine
// clock: every instant comes from the caller's seam.
namespace Jellyfin.Plugin.ShareLinks.InvariantFixtures.MachineClock;

internal sealed class HandsTheClockOnWithoutReadingIt
{
    private readonly System.TimeProvider _clock;

    public HandsTheClockOnWithoutReadingIt(System.TimeProvider clock) => _clock = clock;

    public TimeProvider Clock => _clock;

    public bool HasExpired(DateTimeOffset expiresAt) => _clock.GetUtcNow() >= expiresAt;
}
