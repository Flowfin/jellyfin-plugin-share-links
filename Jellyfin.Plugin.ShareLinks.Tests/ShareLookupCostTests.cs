using System;
using System.Collections.Generic;
using System.Text;
using Xunit;

namespace Jellyfin.Plugin.ShareLinks.Tests;

/// <summary>
/// What the token lookup costs, which is the half of #26 that is about timing.
/// </summary>
/// <remarks>
/// <para>
/// #26 asks that a token naming no share and a token naming a share the caller may
/// not use be indistinguishable in status code, in body and, as far as is
/// reasonable, in timing. The first two are asserted over what the guest route
/// writes. The third is not measured here and no claim about a duration is made.
/// What the design carries in place of a measurement is that both paths do the same
/// work, and this is where that is held rather than being a sentence in a remark.
/// </para>
/// <para>
/// It is two properties and only one of them was held. That the lookup happens
/// before anything about the caller is held by <c>DecisionTableTests</c>: with the
/// signed-in check moved in front of the lookup, fourteen rows of that table go red,
/// because a token matching no record then answers with the caller's state instead
/// of with the share's. That the scan visits every record rather than stopping at
/// the one that answers was held by nothing, which is what this is for.
/// </para>
/// <para>
/// Nothing here measures a duration. A timing assertion on a machine running
/// something else fails for the reason the machine was busy, and nothing here could
/// tell that apart from a change to the routine. What is asserted instead is a
/// consequence of the work having been done, which a store can be arranged to make
/// visible from outside.
/// </para>
/// <para>
/// The neighbouring mistake, a loop that stops one record short of the end, is
/// deliberately not covered a second time. It is already refused by fifty-nine
/// tests in this suite, because most of them hold a store of one record and such a
/// loop reaches none of it.
/// </para>
/// </remarks>
public class ShareLookupCostTests
{
    private static readonly byte[] Key = Encoding.UTF8.GetBytes("share links lookup cost test key, long enough");

    private static readonly Guid First = new("66666666-6666-6666-6666-666666666661");
    private static readonly Guid Second = new("66666666-6666-6666-6666-666666666662");

    [Fact]
    public void TheScanDoesNotStopAtTheFirstRecordThatAnswers()
    {
        // Two records carrying one stored value. That is not a store this plugin
        // writes: a token is 256 bits, so at most one record can answer for one,
        // and the routine's own remarks say so. It is a store a hand-edited file or
        // a duplicated record produces, and it is the only arrangement that makes
        // the difference visible from outside, because a scan that stops at the
        // first record that answers and one that goes on to the second return the
        // same answer for every store where the value appears once.
        //
        // So what is asserted is not that the later duplicate wins as a rule. It is
        // that the second record was reached at all, and the record handed back is
        // how that is read off. Stopping early, whether by a return, a break or a
        // conditional assignment that keeps the first, hands back the other one.
        var token = ShareTokens.Mint();
        var storedValue = ShareTokenHash.Compute(Key, token);

        var records = new List<ShareRecord>
        {
            RecordFor(storedValue, First),
            RecordFor(storedValue, Second),
        };

        var found = ShareLookup.ByToken(records, Key, token);

        Assert.NotNull(found);
        Assert.Equal(Second, found!.Id);
    }

    private static ShareRecord RecordFor(string storedValue, Guid id) => new()
    {
        SchemaVersion = ShareRecord.CurrentSchemaVersion,
        Id = id,
        ItemId = new Guid("11111111-1111-1111-1111-111111111111"),
        InvitedUserIds = [new Guid("22222222-2222-2222-2222-222222222222")],
        CreatedByUserId = new Guid("44444444-4444-4444-4444-444444444444"),
        CreatedAt = new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero),
        ExpiresAt = new DateTimeOffset(2026, 1, 9, 3, 4, 5, TimeSpan.Zero),
        TokenHash = storedValue,
    };
}
