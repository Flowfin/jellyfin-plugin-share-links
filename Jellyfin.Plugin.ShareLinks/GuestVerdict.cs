namespace Jellyfin.Plugin.ShareLinks;

/// <summary>
/// What the request-path filter decided about one request (#239).
/// </summary>
/// <remarks>
/// <para>
/// Four values, and the first one is the important one. An account this plugin
/// never made is not confined at all, and that is a different answer from being
/// confined and allowed through. Collapsing the two would make an ordinary
/// account on the server look like a guest who happened to be asking for the
/// right item, and the day the item check changed, every account on the server
/// would change with it.
/// </para>
/// <para>
/// The two refusals are kept apart for the operator rather than for the caller.
/// A guest whose last share ended and a guest asking for somebody else's item are
/// the same 404 on the wire, which is #26, and they are two different things to
/// read in a log.
/// </para>
/// </remarks>
public enum GuestVerdict
{
    /// <summary>
    /// The account is not one this plugin created, so this plugin has nothing to
    /// say about the request. It is not an allowance: nothing was checked.
    /// </summary>
    NotAGuestOfThisPlugin = 0,

    /// <summary>
    /// The account is a guest of this plugin and a live record naming it names
    /// the item asked for.
    /// </summary>
    Reaches = 1,

    /// <summary>
    /// The account is a guest of this plugin and no live record names it. This is
    /// the state a guest lands in the moment the last record naming them ends.
    /// </summary>
    RefusedNothingLive = 2,

    /// <summary>
    /// The account is a guest of this plugin, it has at least one live record,
    /// and the item asked for is not one of the items those records name.
    /// </summary>
    RefusedItemNotShared = 3,

    /// <summary>
    /// The account is a guest of this plugin and the route asked for enumerates,
    /// searches or browses rather than naming one item. There is no item to
    /// compare, so the answer is a refusal and not a check.
    /// </summary>
    RefusedRouteEnumerates = 4,
}
