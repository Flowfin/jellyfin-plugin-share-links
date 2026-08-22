using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.ShareLinks;

/// <summary>
/// Which items a guest of this plugin reaches, and under which ceiling (#239, #52, #61).
/// </summary>
/// <remarks>
/// <para>
/// <c>docs/guest-confinement.md</c> chose a filter of this plugin's own over the
/// account's allowed tags, and the reason it gives is that nothing is written
/// onto the account or into the library: the answer is computed per request from
/// the records. This is that computation, kept away from the request pipeline so
/// that every case can be driven without one.
/// </para>
/// <para>
/// The confinement and the ceiling are answered together rather than by two
/// routines, because #44's decision of 2026-08-20 is one surface carrying both.
/// Two readings of the store are two answers, and a request confined against one
/// reading and capped against another is the drift that decision exists against.
/// </para>
/// <para>
/// Membership is by <see cref="ShareRecord.PluginCreatedUserIds"/> and never by
/// <see cref="ShareRecord.InvitedUserIds"/>, and the two are not the same
/// question. An account this plugin made exists only for the share, so confining
/// it takes nothing away. An invited account this plugin did not make belongs to
/// somebody who uses the server, and confining that account to one item would
/// take their own library away from them because an operator shared a film with
/// them. That direction is refused here in the same way
/// <c>docs/revocation.md</c> refuses to end such an account's session.
/// </para>
/// <para>
/// A guest is judged against every record that names them and not only the live
/// ones, for membership, because a guest whose last share has just ended is still
/// this plugin's guest and is exactly the account that must not be let loose.
/// Reachability is judged against the live ones alone.
/// </para>
/// </remarks>
public static class GuestConfinement
{
    /// <summary>
    /// Whether this account is one this plugin created for a share.
    /// </summary>
    /// <param name="records">Every record the store holds.</param>
    /// <param name="account">The account the server says is asking.</param>
    /// <returns><c>true</c> where any record, live or stopped, names the account as one this plugin made.</returns>
    /// <remarks>
    /// Over every record rather than the live ones. The moment the last share
    /// naming a guest ends, the live set stops naming them, and an answer taken
    /// from the live set alone would say at that instant that the account is
    /// nothing to do with this plugin. That is the widest possible reading of a
    /// share ending and it is the wrong direction to fail in.
    /// </remarks>
    public static bool IsAGuestOfThisPlugin(IReadOnlyList<ShareRecord> records, Guid account)
    {
        ArgumentNullException.ThrowIfNull(records);

        if (account == Guid.Empty)
        {
            return false;
        }

        for (var index = 0; index < records.Count; index++)
        {
            var made = records[index].PluginCreatedUserIds;
            for (var made_index = 0; made_index < made.Count; made_index++)
            {
                if (made[made_index] == account)
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// The items a guest reaches at this instant.
    /// </summary>
    /// <param name="records">Every record the store holds.</param>
    /// <param name="account">The account the server says is asking.</param>
    /// <param name="now">The instant a record is judged live at.</param>
    /// <returns>The items, each one once, in the order the records name them.</returns>
    /// <remarks>
    /// Live is <see cref="ShareBounds.IsLive"/>'s answer rather than a second
    /// comparison over the same two fields, so what a guest reaches and what the
    /// operator's listing shows as live cannot drift apart.
    /// </remarks>
    public static IReadOnlyList<Guid> ItemsReachableBy(
        IReadOnlyList<ShareRecord> records,
        Guid account,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(records);

        var items = new List<Guid>();
        for (var index = 0; index < records.Count; index++)
        {
            var record = records[index];
            if (!ShareBounds.IsLive(record, now) || !Names(record.InvitedUserIds, account))
            {
                continue;
            }

            if (!Names(items, record.ItemId))
            {
                items.Add(record.ItemId);
            }
        }

        return items;
    }

    /// <summary>
    /// Decides one request.
    /// </summary>
    /// <param name="records">Every record the store holds.</param>
    /// <param name="account">The account the server says is asking.</param>
    /// <param name="item">The item the route names, or <c>null</c> where the route enumerates rather than naming one.</param>
    /// <param name="accountLimit">The account's own remote client limit in bits per second, or <c>null</c> where it carries none.</param>
    /// <param name="serverLimit">The server configuration's remote client limit in bits per second, or <c>null</c> where it carries none.</param>
    /// <param name="now">The instant the records are judged at.</param>
    /// <returns>The verdict and the ceiling in force.</returns>
    /// <remarks>
    /// <para>
    /// A <c>null</c> item is a route that lists, searches or browses. There is
    /// nothing to compare, so a guest of this plugin is refused rather than
    /// checked: those are #44's second, third and fourth widenings, and the whole
    /// point of them is that they reach the item's neighbours without ever naming
    /// the item.
    /// </para>
    /// <para>
    /// The ceiling is the lowest of the three, which is
    /// <see cref="EffectiveBitrate.Lowest"/>'s rule rather than one restated here,
    /// and the share's own is the lowest cap across the live records naming this
    /// account for this item. Lowest and not first: a guest invited to one item
    /// twice, under two caps, gets the tighter one, because the alternative is
    /// that an operator lowers a cap and a second record nobody was looking at
    /// keeps the stream where it was.
    /// </para>
    /// </remarks>
    public static GuestRequestDecision Decide(
        IReadOnlyList<ShareRecord> records,
        Guid account,
        Guid? item,
        long? accountLimit,
        long? serverLimit,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(records);

        if (!IsAGuestOfThisPlugin(records, account))
        {
            return new GuestRequestDecision(GuestVerdict.NotAGuestOfThisPlugin, new AppliedBitrateCap(null, BitrateCeiling.None));
        }

        var reachable = ItemsReachableBy(records, account, now);
        if (reachable.Count == 0)
        {
            return new GuestRequestDecision(GuestVerdict.RefusedNothingLive, new AppliedBitrateCap(null, BitrateCeiling.None));
        }

        if (item is not { } asked)
        {
            return new GuestRequestDecision(GuestVerdict.RefusedRouteEnumerates, new AppliedBitrateCap(null, BitrateCeiling.None));
        }

        if (!Names(reachable, asked))
        {
            return new GuestRequestDecision(GuestVerdict.RefusedItemNotShared, new AppliedBitrateCap(null, BitrateCeiling.None));
        }

        return new GuestRequestDecision(
            GuestVerdict.Reaches,
            EffectiveBitrate.Lowest(TightestShareCap(records, account, asked, now), accountLimit, serverLimit));
    }

    // Membership over a read-only list, written once. The extension the compiler
    // reaches for on a bare Contains is the span one, which this interface is not.
    private static bool Names(IReadOnlyList<Guid> list, Guid wanted)
    {
        for (var index = 0; index < list.Count; index++)
        {
            if (list[index] == wanted)
            {
                return true;
            }
        }

        return false;
    }

    // The lowest cap any live record naming this account names for this item, or
    // null where every one of them names none. Written out rather than folded
    // into Decide, because "a record with no cap does not make the cap absent" is
    // the half of this a chain of comparisons gets wrong first.
    private static long? TightestShareCap(
        IReadOnlyList<ShareRecord> records,
        Guid account,
        Guid item,
        DateTimeOffset now)
    {
        long? tightest = null;

        for (var index = 0; index < records.Count; index++)
        {
            var record = records[index];
            if (record.ItemId != item
                || !ShareBounds.IsLive(record, now)
                || !Names(record.InvitedUserIds, account)
                || record.MaxBitrateBitsPerSecond is not { } cap)
            {
                continue;
            }

            tightest = tightest is { } lowest ? Math.Min(lowest, cap) : cap;
        }

        return tightest;
    }
}
