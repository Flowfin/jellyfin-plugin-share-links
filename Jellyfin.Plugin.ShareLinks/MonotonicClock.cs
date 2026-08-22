using System;
using System.Threading;

namespace Jellyfin.Plugin.ShareLinks;

/// <summary>
/// A clock that never reports an instant earlier than one it has already
/// reported (#79).
/// </summary>
/// <remarks>
/// <para>
/// Server clocks step. An NTP correction, a virtual machine resuming from a
/// snapshot, or an operator fixing a wrong time zone all move the clock, and one
/// of those directions is backwards. Expiry is a comparison against the clock, so
/// a backwards step used to make a share that had already been refused as expired
/// answer again for the size of the step. <c>docs/expiry.md</c> is where that is
/// argued and where this type's price is written down.
/// </para>
/// <para>
/// What it does is one subtraction's worth of arithmetic: it remembers the
/// highest instant it has handed out and hands that out again whenever the clock
/// underneath reads earlier. Nothing is written to disk and nothing is written on
/// a read path, which is what the alternative weighed on that page costs.
/// </para>
/// <para>
/// WHAT IT DOES NOT COVER IS A RESTART. The high-water mark is this object's
/// field, so a server that stops and starts asks the machine again and believes
/// whatever it is told. A clock stepped backwards while the server was down, or
/// stepped backwards and then restarted, revives an expired share exactly as
/// before. The residual is smaller than it was and it is not gone.
/// </para>
/// <para>
/// THE PRICE IS PAID IN THE OTHER DIRECTION AND IS DELIBERATE. A clock that jumps
/// forwards by a year and is then corrected leaves this holding the wrong year
/// until the process ends, so shares expire early and the sweep drops records
/// early. That is the direction a share stops working in rather than the direction
/// it comes back in, which is the one worth being wrong in for a plugin that hands
/// out links.
/// </para>
/// <para>
/// It is a decorator rather than a clock of its own: every member other than
/// <see cref="GetUtcNow"/> is the one underneath, so wrapping a test's clock in it
/// changes what the time is and nothing else.
/// </para>
/// </remarks>
public sealed class MonotonicClock : TimeProvider
{
    private readonly TimeProvider _underneath;

    private long _highWaterUtcTicks;

    /// <summary>
    /// Initializes a new instance of the <see cref="MonotonicClock"/> class.
    /// </summary>
    /// <param name="underneath">The clock this one reads and clamps.</param>
    /// <remarks>
    /// The high-water mark starts at what the clock underneath says now, so an
    /// instance made at startup carries no opinion about anything that happened
    /// before it existed.
    /// </remarks>
    public MonotonicClock(TimeProvider underneath)
    {
        ArgumentNullException.ThrowIfNull(underneath);

        _underneath = underneath;
        _highWaterUtcTicks = underneath.GetUtcNow().UtcTicks;
    }

    /// <inheritdoc />
    public override TimeZoneInfo LocalTimeZone => _underneath.LocalTimeZone;

    /// <inheritdoc />
    public override long TimestampFrequency => _underneath.TimestampFrequency;

    /// <inheritdoc />
    /// <remarks>
    /// The highest instant this clock has handed out, which is the clock
    /// underneath whenever that one is moving forwards.
    /// </remarks>
    public override DateTimeOffset GetUtcNow()
    {
        var read = _underneath.GetUtcNow().UtcTicks;
        var highest = Interlocked.Read(ref _highWaterUtcTicks);

        // Compare and exchange rather than a lock, and a loop rather than one
        // attempt: two requests reading at once may both find the mark behind
        // them, and the one that loses the exchange has to re-read what the
        // winner put there instead of overwriting it with an older instant.
        while (read > highest)
        {
            var found = Interlocked.CompareExchange(ref _highWaterUtcTicks, read, highest);
            if (found == highest)
            {
                highest = read;
                break;
            }

            highest = found;
        }

        return new DateTimeOffset(highest, TimeSpan.Zero);
    }

    /// <inheritdoc />
    public override long GetTimestamp() => _underneath.GetTimestamp();

    /// <inheritdoc />
    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) =>
        _underneath.CreateTimer(callback, state, dueTime, period);
}
