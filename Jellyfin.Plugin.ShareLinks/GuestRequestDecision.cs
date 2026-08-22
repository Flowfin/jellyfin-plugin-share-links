namespace Jellyfin.Plugin.ShareLinks;

/// <summary>
/// What one request from a guest gets, and under which ceiling (#239).
/// </summary>
/// <param name="Verdict">Whether the account reaches what it asked for, and why not where it does not.</param>
/// <param name="Cap">The ceiling in force for this account on this item, and which of the three produced it.</param>
/// <remarks>
/// <para>
/// One answer carrying both, because they are decided from the same record and
/// at the same instant. Two calls would be two readings of the store, and a
/// second reading is a second answer that another writer can have moved
/// underneath, which is how a request gets confined against one share and capped
/// against another.
/// </para>
/// <para>
/// The ceiling is present on a refusal as well, and it is
/// <see cref="BitrateCeiling.None"/> there. A refused request has no stream to
/// cap, so the field says so rather than carrying a number a caller might apply
/// to something.
/// </para>
/// </remarks>
public readonly record struct GuestRequestDecision(GuestVerdict Verdict, AppliedBitrateCap Cap)
{
    /// <summary>
    /// Gets a value indicating whether this request may go on to the route it asked for.
    /// </summary>
    /// <remarks>
    /// <see cref="GuestVerdict.NotAGuestOfThisPlugin"/> is true here and it is not
    /// an allowance. Nothing was checked, so nothing is being permitted; this
    /// plugin is simply not in the way of an account it did not make.
    /// </remarks>
    public bool MayContinue => Verdict is GuestVerdict.NotAGuestOfThisPlugin or GuestVerdict.Reaches;
}
