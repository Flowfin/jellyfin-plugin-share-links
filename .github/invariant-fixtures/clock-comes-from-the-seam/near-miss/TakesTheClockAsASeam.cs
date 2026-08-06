// Near miss for the invariant clock-comes-from-the-seam. The type name and the
// word Now are both here, twice, and neither line reads the machine clock. A
// pattern matching either word on its own would refuse the correct code.
namespace Jellyfin.Plugin.ShareLinks.InvariantFixtures.Clock;

internal sealed class TakesTheClockAsASeam
{
    private readonly TimeProvider _time;

    public TakesTheClockAsASeam(TimeProvider time) => _time = time;

    public bool HasExpired(Share share)
    {
        DateTimeOffset now = _time.GetUtcNow();
        return share.ExpiresAt <= now;
    }

    public static DateTimeOffset AFixedInstant() =>
        new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
}
