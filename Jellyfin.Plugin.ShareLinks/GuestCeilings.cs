using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.ShareLinks;

/// <summary>
/// The ceiling in force for each account one share names (#64).
/// </summary>
/// <remarks>
/// <para>
/// #64's first paragraph is the failure this answers: an operator lowers a number
/// and nothing changes, because one of the other two ceilings was the one holding
/// and nothing said so. The share view showed the record's own number, which is
/// what the operator just typed, so it moved every time and meant nothing.
/// </para>
/// <para>
/// The answer comes out of <see cref="GuestConfinement.Decide"/> and not out of a
/// second comparison written here, so the number an operator reads is the number
/// the request-path filter would apply. A routine of its own would be the second
/// copy the <c>share-decision-comes-from-one-routine</c> invariant exists
/// against, and it would drift in exactly the direction that hides this bug: the
/// share's own ceiling here is the tightest across every live record naming the
/// account for the item, so a second share nobody was looking at is part of the
/// answer and a per-record comparison would miss it.
/// </para>
/// <para>
/// **This is the answer at the instant the listing was read, and it is not a
/// promise about the instant a guest asks.** The three inputs are read from the
/// records and from the server as they stand now; the filter reads them again per
/// request and can get a different answer because somebody moved one in between.
/// <see cref="ShareSummary.State"/> is read the same way and for the same reason,
/// and that is a bound rather than a defect: a surface that showed nothing until
/// it could promise everything would show nothing.
/// </para>
/// <para>
/// Whether that ceiling can be MET is answered here too, which is #286, and it is
/// the same routine rather than a second one for the reason above: it is a fact
/// about the same pair of an account and an item, computed from the same reading
/// of the store, and a second surface answering it would be free to disagree with
/// this one about which ceiling was in force before it ever got to whether the
/// item fits under it.
/// </para>
/// </remarks>
public static class GuestCeilings
{
    /// <summary>
    /// What each account this share names would be capped at, right now, and whether that ceiling can be met.
    /// </summary>
    /// <param name="records">Every record the store holds, because the answer is not this record's alone.</param>
    /// <param name="record">The share the answers are about.</param>
    /// <param name="accountCeiling">The remote client limit of one account, or <c>null</c> where it carries none.</param>
    /// <param name="serverCeiling">The server configuration's remote client limit, or <c>null</c> where it carries none.</param>
    /// <param name="accountPlayback">What the server says about playing this share's item for one account. Asked only where a ceiling is in force.</param>
    /// <param name="now">The instant the records are judged live at.</param>
    /// <returns>One answer per invited account, in the order the record names them.</returns>
    /// <remarks>
    /// <para>
    /// The invited accounts are walked rather than the accounts this plugin made,
    /// because the invited list is what an operator sees in the row beside this
    /// one and an answer that silently covered a different set would be read as
    /// covering that one. An invited account this plugin did not create comes back
    /// as <see cref="GuestVerdict.NotAGuestOfThisPlugin"/>, which is the honest
    /// answer: the filter does not stand in front of such an account, so no
    /// ceiling of this plugin's reaches it.
    /// </para>
    /// <para>
    /// <paramref name="accountPlayback"/> is awaited only where a ceiling is
    /// actually in force, and that is the whole of the cost control. An account
    /// with no ceiling has nothing to meet, so asking the server what its item can
    /// be played at would be paying a library call to answer a question nobody
    /// asked. A caller that pre-read every pair would spend that call whether or
    /// not this routine wanted it, which is why the argument is a delegate rather
    /// than a value.
    /// </para>
    /// <para>
    /// A pair with no ceiling comes back as <see cref="CapReach.NoCeilingIsSet"/>,
    /// which is what <see cref="BitrateCapReach"/> would have answered had it been
    /// called with an absent ceiling. The member is spelled here rather than
    /// reached by calling that routine with a <c>null</c>, because the point of not
    /// calling it is that the server was never asked, and a reader should be able
    /// to see that from the code rather than from a comment.
    /// </para>
    /// </remarks>
    public static async Task<IReadOnlyList<GuestCeiling>> OfAsync(
        IReadOnlyList<ShareRecord> records,
        ShareRecord record,
        Func<Guid, long?> accountCeiling,
        long? serverCeiling,
        Func<Guid, Task<AccountPlayback>> accountPlayback,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(records);
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(accountCeiling);
        ArgumentNullException.ThrowIfNull(accountPlayback);

        var invited = record.InvitedUserIds;
        var answers = new List<GuestCeiling>(invited.Count);
        for (var index = 0; index < invited.Count; index++)
        {
            var account = invited[index];
            var decision = GuestConfinement.Decide(
                records,
                account,
                record.ItemId,
                accountCeiling(account),
                serverCeiling,
                now);

            var canBeMet = CapReach.NoCeilingIsSet;
            if (decision.Cap.BitsPerSecond is { } ceiling)
            {
                var playback = await accountPlayback(account).ConfigureAwait(false);
                canBeMet = BitrateCapReach.Of(ceiling, playback.Versions, playback.MayTranscode);
            }

            answers.Add(new GuestCeiling(account, decision.Verdict, decision.Cap, canBeMet));
        }

        return answers;
    }
}
