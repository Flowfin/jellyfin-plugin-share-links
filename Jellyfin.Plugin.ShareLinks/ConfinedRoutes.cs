using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.ShareLinks;

/// <summary>
/// The server routes the request-path filter judges, and how it reads an item out of one (#239).
/// </summary>
/// <remarks>
/// <para>
/// This list is the accepted cost of the mechanism
/// <c>docs/guest-confinement.md</c> chose, written down where it can be read
/// rather than argued about. The routes belong to the server, the server's route
/// table is not in the packages this plugin compiles against, and no reading of
/// this tree derives the list. It is maintained by hand and it is not complete,
/// which is a sentence this file carries on purpose.
/// </para>
/// <para>
/// A path that matches nothing here is NOT JUDGED, and not-judged is not
/// permitted. The filter is not standing in front of it at all, so a guest
/// reaching library content on a route nobody added is a hole and reads as one:
/// <see cref="ConfinedRouteKind.NotJudged"/> is a separate answer from
/// <see cref="GuestVerdict.Reaches"/> for exactly that reason, and the two must
/// not be collapsed by a later reader looking for a simplification.
/// </para>
/// <para>
/// Two families and the difference between them is the whole design. A route that
/// names one item can be checked against the items a guest reaches. A route that
/// lists, searches or browses cannot be, because it has no item in it, and those
/// are the routes #44's second, third and fourth widenings use: the collection,
/// the search, the neighbours. Those are refused outright for a guest of this
/// plugin rather than filtered, because filtering a listing means this plugin
/// rewriting the server's answer, which is a much larger surface and a much
/// easier one to get subtly wrong.
/// </para>
/// <para>
/// A series is in the second family and that has a consequence worth stating.
/// Sharing a series does not let a guest list its episodes, because the record
/// names one item and #44's fifth widening is exactly that walk. What this plugin
/// can share usefully today is therefore an item a client plays directly.
/// </para>
/// <para>
/// The prefixes are matched case-insensitively, because the server's routing is,
/// and matching case-sensitively here would make <c>/items/{id}</c> a hole that
/// <c>/Items/{id}</c> is not.
/// </para>
/// </remarks>
public static class ConfinedRoutes
{
    /// <summary>
    /// Gets the templates whose path carries the identifier of one item. Matched as a
    /// prefix, so everything under one of them is judged against the same item.
    /// </summary>
    /// <remarks>
    /// <c>{itemId}</c> matches a segment that parses as a GUID and nothing else.
    /// A segment that does not parse is not an item, and a template that matched
    /// it would hand the filter a null identifier on a route it had decided names
    /// one, which is the shape that turns a refusal into an exception.
    /// </remarks>
    public static IReadOnlyList<string> NamingOneItem { get; } = new[]
    {
        "Items/{itemId}",
        "Users/{userId}/Items/{itemId}",
        "Videos/{itemId}",
        "Audio/{itemId}",
        "Playing/{itemId}",
        "Sessions/Playing/{itemId}",
    };

    /// <summary>
    /// Gets the templates that list, search or browse rather than naming one item.
    /// Matched as a prefix.
    /// </summary>
    /// <remarks>
    /// <c>{userId}</c> and <c>{itemId}</c> match one segment each. The identifier
    /// is not read out of these, because there is nothing to compare it against:
    /// what a guest asks for on one of them is a set, and a set is what
    /// confinement is refusing.
    /// </remarks>
    public static IReadOnlyList<string> Enumerating { get; } = new[]
    {
        "Items",
        "Users/{userId}/Items",
        "Users/{userId}/Views",
        "UserViews",
        "Shows/{itemId}/Episodes",
        "Shows/{itemId}/Seasons",
        "Shows/NextUp",
        "Shows/Upcoming",
        "Search/Hints",
        "Library/MediaFolders",
        "Artists",
        "Persons",
        "Studios",
        "Genres",
        "MusicGenres",
        "Years",
        "Trailers",
        "Movies/Recommendations",
        "LiveTv",
    };

    /// <summary>
    /// Gets the templates on which the server answers with a ceiling a client then
    /// asks inside. The requested ceiling is lowered on these rather than
    /// refused.
    /// </summary>
    /// <remarks>
    /// This is the interception leg of <c>docs/bitrate-cap.md</c>. A client that
    /// reads the ceiling it is given asks for a stream inside it, which is the
    /// ordinary case and the one that ends in a lower-quality stream rather than
    /// in an error.
    /// </remarks>
    public static IReadOnlyList<string> ReportingACeiling { get; } = new[]
    {
        "Items/{itemId}/PlaybackInfo",
    };

    /// <summary>
    /// Gets the templates on which the server serves bytes. A request naming a
    /// ceiling above the one in force is refused on these rather than lowered.
    /// </summary>
    /// <remarks>
    /// This is the refusal leg of <c>docs/bitrate-cap.md</c>, and it is what a
    /// client that never asks politely meets. Lowering here instead would mean
    /// serving something other than what was asked for without saying so, and the
    /// leg exists precisely for the request that ignored what it was told.
    /// </remarks>
    public static IReadOnlyList<string> ServingAStream { get; } = new[]
    {
        "Videos/{itemId}",
        "Audio/{itemId}",
    };

    /// <summary>
    /// Gets the query parameters a client names a ceiling in, in bits per second.
    /// </summary>
    /// <remarks>
    /// Read from the query rather than from a bound model, because the filter
    /// runs before model binding and because a parameter this plugin does not
    /// recognise has to read as absent rather than as zero. The list is
    /// maintained by hand for the same reason the route list is, and it carries
    /// the same warning: a parameter nobody added is one this plugin does not see.
    /// </remarks>
    public static IReadOnlyList<string> RequestedCeilingParameters { get; } = new[]
    {
        "maxStreamingBitrate",
        "videoBitRate",
        "audioBitRate",
    };

    /// <summary>
    /// Whether a path is one the server answers a ceiling on.
    /// </summary>
    /// <param name="path">The request path.</param>
    /// <returns><c>true</c> where the requested ceiling is lowered rather than refused.</returns>
    public static bool ReportsACeiling(string? path) => AnyOf(ReportingACeiling, path);

    /// <summary>
    /// Whether a path is one the server serves bytes on.
    /// </summary>
    /// <param name="path">The request path.</param>
    /// <returns><c>true</c> where a ceiling above the one in force is refused.</returns>
    public static bool ServesAStream(string? path) => AnyOf(ServingAStream, path);

    /// <summary>
    /// Judges one request path.
    /// </summary>
    /// <param name="path">The request path, with or without a leading slash.</param>
    /// <returns>Which family the path is in, and the item it names where it names one.</returns>
    /// <remarks>
    /// The item family is matched first. <c>Items/{itemId}</c> and <c>Items</c>
    /// are both here and only the first of them carries a GUID, so asking the
    /// narrower question first is what keeps <c>Items/Filters</c> out of the
    /// family that expects an identifier.
    /// </remarks>
    public static ConfinedRoute Judge(string? path)
    {
        var segments = SegmentsOf(path);
        if (segments.Length == 0)
        {
            return new ConfinedRoute(ConfinedRouteKind.NotJudged, null);
        }

        for (var index = 0; index < NamingOneItem.Count; index++)
        {
            if (Matches(NamingOneItem[index], segments, out var item) && item is not null)
            {
                return new ConfinedRoute(ConfinedRouteKind.NamesAnItem, item);
            }
        }

        for (var index = 0; index < Enumerating.Count; index++)
        {
            if (Matches(Enumerating[index], segments, out _))
            {
                return new ConfinedRoute(ConfinedRouteKind.Enumerates, null);
            }
        }

        return new ConfinedRoute(ConfinedRouteKind.NotJudged, null);
    }

    private static bool AnyOf(IReadOnlyList<string> templates, string? path)
    {
        var segments = SegmentsOf(path);
        for (var index = 0; index < templates.Count; index++)
        {
            if (Matches(templates[index], segments, out _))
            {
                return true;
            }
        }

        return false;
    }

    // The path as segments, with empty ones dropped so a leading, trailing or
    // doubled slash cannot change which template matches. A server that routes
    // //Items/{id} to the same action as /Items/{id} would otherwise be a hole
    // one character wide.
    private static string[] SegmentsOf(string? path)
        => string.IsNullOrEmpty(path)
            ? Array.Empty<string>()
            : path.Split('/', StringSplitOptions.RemoveEmptyEntries);

    // A template matches when every one of its segments matches the segment in
    // the same position. The path may be longer, which is what makes this a
    // prefix match: everything under Items/{id} is about that item.
    private static bool Matches(string template, string[] segments, out Guid? item)
    {
        item = null;

        var expected = template.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length < expected.Length)
        {
            return false;
        }

        for (var index = 0; index < expected.Length; index++)
        {
            var part = expected[index];
            if (string.Equals(part, "{itemId}", StringComparison.Ordinal))
            {
                if (!Guid.TryParse(segments[index], out var parsed))
                {
                    return false;
                }

                item = parsed;
                continue;
            }

            if (string.Equals(part, "{userId}", StringComparison.Ordinal))
            {
                continue;
            }

            if (!string.Equals(part, segments[index], StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }
}
