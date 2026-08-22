using System;

namespace Jellyfin.Plugin.ShareLinks;

/// <summary>
/// Which family a request path is in, as far as this plugin's maintained list reaches (#239).
/// </summary>
/// <remarks>
/// Three values, and the third is not a permission. <c>docs/guest-confinement.md</c>
/// accepts that the routes belong to the server and that this repository cannot
/// enumerate them, so a path nobody added is one this plugin is not standing in
/// front of. It is named rather than folded into an allowance, so that a reader
/// counting what the filter refuses cannot read it as something the filter looked
/// at and let through.
/// </remarks>
public enum ConfinedRouteKind
{
    /// <summary>
    /// The list does not reach this path. Nothing was checked, and nothing is
    /// being permitted.
    /// </summary>
    NotJudged = 0,

    /// <summary>
    /// The path names one item, which is the identifier a guest's records are
    /// compared against.
    /// </summary>
    NamesAnItem = 1,

    /// <summary>
    /// The path lists, searches or browses. There is no item in it to compare, so
    /// a guest of this plugin is refused it.
    /// </summary>
    Enumerates = 2,
}

/// <summary>
/// One judged request path (#239).
/// </summary>
/// <param name="Kind">Which family the path is in.</param>
/// <param name="Item">The item the path names, or <c>null</c> on every other kind.</param>
public readonly record struct ConfinedRoute(ConfinedRouteKind Kind, Guid? Item);
