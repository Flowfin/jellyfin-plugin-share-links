using System;

namespace Jellyfin.Plugin.ShareLinks;

/// <summary>
/// Takes the lowest of the ceilings that apply and says which ones produced it (#64).
/// </summary>
/// <remarks>
/// <para>
/// Three ceilings can be in play on one stream. The share carries its own, which
/// is <see cref="ShareRecord.MaxBitrateBitsPerSecond"/>; the invited account
/// carries a remote client limit; and the server configuration carries one that
/// applies to everybody. Silently picking one of them is what produces the report
/// that an operator lowered a number and nothing changed.
/// </para>
/// <para>
/// All three are in bits per second here. <see cref="BitrateCap"/> is where the
/// unit an operator writes is converted to the unit a record keeps, and this
/// routine is downstream of that: it does no conversion, so a value arriving in
/// the wrong unit is a mistake made before this point and not one this type can
/// see.
/// </para>
/// <para>
/// Nothing supplies the second and third values yet. Reading the account limit and
/// the server configuration is the other half of #64 and the surface that shows
/// the answer is #70. This is the arithmetic, landed before its callers so that
/// the first caller is written against a routine that already answers the awkward
/// cases rather than one that grew them afterwards.
/// </para>
/// </remarks>
public static class EffectiveBitrate
{
    /// <summary>
    /// The lowest of whichever ceilings are set, and every ceiling sitting at it.
    /// </summary>
    /// <param name="share">The share's own ceiling, or <c>null</c>.</param>
    /// <param name="account">The invited account's remote client limit, or <c>null</c>.</param>
    /// <param name="server">The server configuration's remote client limit, or <c>null</c>.</param>
    /// <returns>The effective ceiling and which of the three produced it.</returns>
    /// <remarks>
    /// A tie reports every ceiling at the value rather than choosing one. That is
    /// the case #64 leaves open, and reporting one of them would mean an operator
    /// who lowers the unreported one sees the same number and the same name and
    /// concludes their change did nothing.
    /// </remarks>
    public static AppliedBitrateCap Lowest(long? share, long? account, long? server)
    {
        var lowest = Lower(Lower(share, account), server);
        if (lowest is not { } value)
        {
            return new AppliedBitrateCap(null, BitrateCeiling.None);
        }

        var applied = BitrateCeiling.None;
        if (share == value)
        {
            applied |= BitrateCeiling.Share;
        }

        if (account == value)
        {
            applied |= BitrateCeiling.Account;
        }

        if (server == value)
        {
            applied |= BitrateCeiling.ServerRemoteClientLimit;
        }

        return new AppliedBitrateCap(value, applied);
    }

    /// <summary>
    /// A ceiling read off one of the server's own integer fields.
    /// </summary>
    /// <param name="bitsPerSecond">The field's value.</param>
    /// <returns>The ceiling, or <c>null</c> when the field carries no ceiling.</returns>
    /// <remarks>
    /// <para>
    /// Both server-side ceilings are plain integers with no way to be absent, so
    /// the state "nobody set one" has to be spelled by a value. Zero is what a
    /// policy that nobody has touched carries, which
    /// <c>EffectiveBitrateTests</c> asserts against the server this plugin
    /// compiles against rather than taking on trust.
    /// </para>
    /// <para>
    /// Zero and below are read here as no ceiling rather than as a ceiling of
    /// nothing, for the reason <see cref="BitrateCap"/> gives for refusing zero
    /// from an operator: serve nothing and serve without a limit are opposite
    /// instructions. The difference is where the value came from. An operator
    /// typed theirs and can be told it is wrong; these two arrive from a field
    /// this plugin does not own, where refusing would mean refusing every server
    /// whose administrator has set no limit, which is most of them.
    /// </para>
    /// <para>
    /// **How the server itself reads these two fields was not measured here.**
    /// This is the reading this plugin takes of a value it is handed, and it is
    /// the safe direction: a ceiling read as absent leaves the other two to
    /// decide, where a zero taken literally would cap every share at nothing.
    /// </para>
    /// </remarks>
    public static long? FromServerValue(int bitsPerSecond)
        => bitsPerSecond > 0 ? bitsPerSecond : null;

    // The lower of two ceilings, where absent loses to anything set. Written once
    // rather than at each pair, because "null is not lower than a number" is the
    // half of this that a chain of comparisons gets wrong first.
    private static long? Lower(long? first, long? second)
    {
        if (first is not { } left)
        {
            return second;
        }

        return second is { } right ? Math.Min(left, right) : left;
    }
}
