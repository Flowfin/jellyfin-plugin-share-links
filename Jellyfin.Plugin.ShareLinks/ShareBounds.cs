using System;
using System.Collections.Generic;
using System.Globalization;
using Jellyfin.Plugin.ShareLinks.Configuration;

namespace Jellyfin.Plugin.ShareLinks;

/// <summary>
/// The ceilings a create is refused against, and the retention rule that empties
/// what has stopped working (#29).
/// </summary>
/// <remarks>
/// <para>
/// Every route that creates a record is a route that fills a disk, and an
/// administrator route is not exempt: an account can be compromised, and a script
/// with a loop in it does not know it is misbehaving. So the ceilings are checked
/// where the store grows rather than only where a request arrives.
/// </para>
/// <para>
/// Three ceilings refuse, and one rule deletes. A create is refused when the
/// server already holds its ceiling of live shares, when the item already holds
/// its ceiling, or when the lifetime asked for is longer than the ceiling a link
/// may be given. Records that have stopped working are deleted once they are
/// older than the retention length. <c>docs/bounds.md</c> is where each number is
/// argued and <c>docs/configuration.md</c> is where the values are.
/// </para>
/// <para>
/// What this bounds and what it does not. The live ceiling bounds how many
/// records answer at any instant, and the retention rule bounds how long a record
/// that has stopped answering is kept. Their product with the rate an operator
/// creates and revokes at is what the file can actually reach, and nothing here
/// bounds that rate: a script that creates a share and revokes it, in a loop,
/// frees a live place every time and leaves a record behind for the retention
/// window. Refusing that needs a rate limit, which is not one of the four bounds
/// this issue names, and saying so is preferred to a sentence claiming the file
/// is bounded outright.
/// </para>
/// <para>
/// The instant arrives as a parameter rather than from a clock this type reads.
/// A machine clock read here could not be tested at a boundary without sleeping,
/// which is what the <c>clock-comes-from-the-seam</c> invariant refuses; where
/// the instant comes from is the seam in #36 and is the caller's question.
/// </para>
/// </remarks>
public sealed class ShareBounds
{
    /// <summary>
    /// The default ceiling on live shares across the server.
    /// </summary>
    public const int DefaultMaxLiveShares = 100;

    /// <summary>
    /// The default ceiling on live shares naming one item.
    /// </summary>
    public const int DefaultMaxLiveSharesPerItem = 10;

    /// <summary>
    /// The default ceiling, in days, on the lifetime a link may be given.
    /// </summary>
    public const int DefaultMaxShareLifetimeDays = 30;

    /// <summary>
    /// The default number of days a record that has stopped working is kept.
    /// </summary>
    public const int DefaultExpiredShareRetentionDays = 90;

    /// <summary>
    /// Initializes a new instance of the <see cref="ShareBounds"/> class.
    /// </summary>
    /// <param name="maxLiveShares">The ceiling on live shares across the server. At least one.</param>
    /// <param name="maxLiveSharesPerItem">The ceiling on live shares naming one item. At least one.</param>
    /// <param name="maxShareLifetimeDays">The ceiling, in days, on the lifetime a link may be given. At least one.</param>
    /// <param name="expiredShareRetentionDays">The days a record that has stopped working is kept. Zero deletes it at the next write.</param>
    /// <exception cref="ArgumentOutOfRangeException">A value is outside what the setting admits. The message names the setting, because a configuration file edited by hand is where these arrive from and the operator needs to know which line to fix.</exception>
    public ShareBounds(
        int maxLiveShares,
        int maxLiveSharesPerItem,
        int maxShareLifetimeDays,
        int expiredShareRetentionDays)
    {
        // Refused rather than clamped. A ceiling of zero silently refusing every
        // share, or a negative retention silently deleting everything, is a
        // plugin serving under a rule nobody wrote, which is the state the
        // refusal exists against.
        ArgumentOutOfRangeException.ThrowIfLessThan(maxLiveShares, 1, nameof(PluginConfiguration.MaxLiveShares));
        ArgumentOutOfRangeException.ThrowIfLessThan(maxLiveSharesPerItem, 1, nameof(PluginConfiguration.MaxLiveSharesPerItem));
        ArgumentOutOfRangeException.ThrowIfLessThan(maxShareLifetimeDays, 1, nameof(PluginConfiguration.MaxShareLifetimeDays));
        ArgumentOutOfRangeException.ThrowIfNegative(expiredShareRetentionDays, nameof(PluginConfiguration.ExpiredShareRetentionDays));

        MaxLiveShares = maxLiveShares;
        MaxLiveSharesPerItem = maxLiveSharesPerItem;
        MaxShareLifetime = TimeSpan.FromDays(maxShareLifetimeDays);
        ExpiredShareRetention = TimeSpan.FromDays(expiredShareRetentionDays);
    }

    /// <summary>
    /// Gets the ceiling on live shares across the server.
    /// </summary>
    public int MaxLiveShares { get; }

    /// <summary>
    /// Gets the ceiling on live shares naming one item.
    /// </summary>
    public int MaxLiveSharesPerItem { get; }

    /// <summary>
    /// Gets the ceiling on the lifetime a link may be given.
    /// </summary>
    public TimeSpan MaxShareLifetime { get; }

    /// <summary>
    /// Gets how long a record that has stopped working is kept.
    /// </summary>
    public TimeSpan ExpiredShareRetention { get; }

    /// <summary>
    /// Reads the bounds an operator has configured.
    /// </summary>
    /// <param name="configuration">The plugin configuration.</param>
    /// <returns>The bounds.</returns>
    /// <exception cref="ArgumentOutOfRangeException">A configured value is outside what the setting admits.</exception>
    public static ShareBounds From(PluginConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        return new ShareBounds(
            configuration.MaxLiveShares,
            configuration.MaxLiveSharesPerItem,
            configuration.MaxShareLifetimeDays,
            configuration.ExpiredShareRetentionDays);
    }

    /// <summary>
    /// Why the configured ceilings may not be used, or <c>null</c> when they may.
    /// </summary>
    /// <param name="configuration">The plugin configuration.</param>
    /// <returns>A sentence naming the setting and the bound it missed, or <c>null</c>.</returns>
    /// <remarks>
    /// It asks <see cref="From(PluginConfiguration)"/> rather than comparing the
    /// four values again, so there is one copy of each bound and an answer given
    /// here cannot drift from the refusal that actually bites. The sentence names
    /// the setting because the constructor is handed each setting's own name.
    /// </remarks>
    public static string? RefuseSettings(PluginConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        try
        {
            From(configuration);
            return null;
        }
        catch (ArgumentOutOfRangeException refused)
        {
            return refused.Message;
        }
    }

    /// <summary>
    /// Whether a record answers at an instant.
    /// </summary>
    /// <param name="record">The record.</param>
    /// <param name="now">The instant to judge it at.</param>
    /// <returns><c>true</c> when the record is neither revoked nor expired.</returns>
    /// <remarks>
    /// Strictly before the instant, which is <c>docs/expiry.md</c>'s half-open
    /// boundary rather than a second opinion about it.
    /// </remarks>
    public static bool IsLive(ShareRecord record, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(record);

        return record.RevokedAt is null && now < record.ExpiresAt;
    }

    /// <summary>
    /// The instant a record stopped answering, or <c>null</c> while it still does.
    /// </summary>
    /// <param name="record">The record.</param>
    /// <param name="now">The instant to judge it at.</param>
    /// <returns>The earlier of revocation and expiry, or <c>null</c>.</returns>
    /// <remarks>
    /// The earlier of the two rather than whichever field is set. A share revoked
    /// after it had already expired stopped working when it expired, and dating
    /// its retention from the revocation would keep it for the window twice over.
    /// </remarks>
    public static DateTimeOffset? CeasedAt(ShareRecord record, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(record);

        if (IsLive(record, now))
        {
            return null;
        }

        return record.RevokedAt is { } revoked && revoked < record.ExpiresAt
            ? revoked
            : record.ExpiresAt;
    }

    /// <summary>
    /// What a record is doing at an instant, as the operator surface reports it (#39, #67).
    /// </summary>
    /// <param name="record">The record.</param>
    /// <param name="now">The instant to read it at.</param>
    /// <returns>The state.</returns>
    /// <remarks>
    /// <para>
    /// Derived from <see cref="CeasedAt"/> rather than from the fields a second
    /// time, so there is one rule about when a share stopped and this cannot
    /// drift from the rule retention is counted by. A comparison written out
    /// again here would agree with that one on the day it was written and not
    /// afterwards.
    /// </para>
    /// <para>
    /// One consequence of taking it from there, named rather than left to be
    /// discovered. A share revoked after it had already expired reads as
    /// <see cref="ShareState.Expired"/>, because expiry is what stopped it and
    /// the revocation stopped nothing. The revoker, the reason and the instant
    /// are all still on the record for an operator who wants to see that
    /// somebody pressed the button afterwards.
    /// </para>
    /// </remarks>
    public static ShareState StateOf(ShareRecord record, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(record);

        if (CeasedAt(record, now) is not { } instant)
        {
            return ShareState.Live;
        }

        return record.RevokedAt == instant ? ShareState.Revoked : ShareState.Expired;
    }

    /// <summary>
    /// Why a record may not be added, or <c>null</c> when it may.
    /// </summary>
    /// <param name="existing">The records already in the store.</param>
    /// <param name="candidate">The record being created.</param>
    /// <param name="now">The instant the create is happening at.</param>
    /// <returns>A sentence naming the bound that was exceeded, or <c>null</c>.</returns>
    /// <remarks>
    /// The message names the setting as well as the number, because the operator
    /// reading it has to find the line to change and a sentence about "too many
    /// shares" does not say which of three ceilings was met.
    /// </remarks>
    public string? Refuse(IReadOnlyList<ShareRecord> existing, ShareRecord candidate, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(existing);
        ArgumentNullException.ThrowIfNull(candidate);

        var lifetime = candidate.ExpiresAt - candidate.CreatedAt;
        if (lifetime > MaxShareLifetime)
        {
            return string.Create(
                CultureInfo.InvariantCulture,
                $"the lifetime asked for is {lifetime.TotalDays:0.##} days and MaxShareLifetimeDays is {MaxShareLifetime.TotalDays:0.##}");
        }

        var live = 0;
        var liveOnTheItem = 0;
        for (var index = 0; index < existing.Count; index++)
        {
            var record = existing[index];
            if (!IsLive(record, now))
            {
                continue;
            }

            live++;
            if (record.ItemId == candidate.ItemId)
            {
                liveOnTheItem++;
            }
        }

        if (liveOnTheItem >= MaxLiveSharesPerItem)
        {
            return string.Create(
                CultureInfo.InvariantCulture,
                $"this item already has {liveOnTheItem} live shares and MaxLiveSharesPerItem is {MaxLiveSharesPerItem}");
        }

        if (live >= MaxLiveShares)
        {
            return string.Create(
                CultureInfo.InvariantCulture,
                $"this server already holds {live} live shares and MaxLiveShares is {MaxLiveShares}");
        }

        return null;
    }

    /// <summary>
    /// The records retention keeps, dropping the ones that stopped working longer ago than the retention length.
    /// </summary>
    /// <param name="records">The records to sweep.</param>
    /// <param name="now">The instant to sweep at.</param>
    /// <returns>The records that are kept, in the order they were given.</returns>
    /// <remarks>
    /// A retention of zero drops a record at the first write after it stopped
    /// working, which is how an operator empties the store of what has expired.
    /// </remarks>
    public IReadOnlyList<ShareRecord> Retained(IReadOnlyList<ShareRecord> records, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(records);

        var kept = new List<ShareRecord>(records.Count);
        for (var index = 0; index < records.Count; index++)
        {
            var record = records[index];
            var ceased = CeasedAt(record, now);
            if (ceased is { } instant && now - instant >= ExpiredShareRetention)
            {
                continue;
            }

            kept.Add(record);
        }

        return kept;
    }
}
