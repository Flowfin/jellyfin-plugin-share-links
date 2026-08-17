using System;
using System.Collections.Generic;
using System.Globalization;
using Jellyfin.Plugin.ShareLinks.Configuration;

namespace Jellyfin.Plugin.ShareLinks;

/// <summary>
/// What a create asks for, judged before anything is made (#67).
/// </summary>
/// <remarks>
/// <para>
/// The rules here are the ones that can be decided from the request and the
/// configuration alone. Whether the item exists and whether a name is free are
/// questions for the server, so they stay in the route where the server's own
/// interfaces are; whether a ceiling on live shares is reached stays in
/// <see cref="ShareBounds.Refuse"/> inside the store mutation, because a check
/// outside the mutation can be overtaken by a second administrator creating at
/// the same moment.
/// </para>
/// <para>
/// So this is not every refusal a create can meet, and it is deliberately not.
/// What it is, is every refusal that costs nothing to find out: a request refused
/// here has made no account, written no record and asked the server for nothing.
/// </para>
/// <para>
/// It is a routine over a request rather than validation attributes on the
/// request type, for one reason. An attribute refuses a value before the action
/// runs and answers in the framework's own shape, which is a body this
/// repository does not choose the words of and no test here can read as the
/// sentence an operator sees.
/// </para>
/// </remarks>
public static class ShareCreation
{
    /// <summary>
    /// The most guests one share may be created for.
    /// </summary>
    /// <remarks>
    /// Every name in a request becomes a real account on the server, so an
    /// unbounded list is an unbounded number of accounts made by one call, and
    /// the accounts outlive the call. Ten is the size of a household plus its
    /// visitors, and it is the same number <see cref="ShareBounds.DefaultMaxLiveSharesPerItem"/>
    /// takes for the neighbouring question. It is a judgement about who a share is
    /// for and it was not measured against anything; an operator who wants a
    /// hundred people on one item wants a library permission rather than a share.
    /// </remarks>
    public const int MaximumGuestsPerShare = 10;

    /// <summary>
    /// Why a create may not proceed, or <c>null</c> when it may.
    /// </summary>
    /// <param name="request">What the operator asked for.</param>
    /// <param name="now">The instant the create is happening at.</param>
    /// <returns>A sentence naming what is wrong, or <c>null</c>.</returns>
    /// <remarks>
    /// The sentence names the field and the value, because the operator reading it
    /// is looking at a form and has to know which box to change. It names no path
    /// and no identifier of anything on the server.
    /// </remarks>
    public static string? Refuse(ShareCreationRequest request, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.ItemId == Guid.Empty)
        {
            return "ItemId: a share names one item and this request names none";
        }

        if (RefuseGuests(request.GuestNames) is { } guests)
        {
            return guests;
        }

        if (request.ExpiresAt is { } instant && instant <= now)
        {
            // Half-open, exactly as docs/expiry.md fixes it: a share is live
            // strictly before its instant. Creating one at or before the instant
            // creates a share that is already refused, which reads to an operator
            // as a link that never worked rather than as an expiry they chose.
            return string.Create(
                CultureInfo.InvariantCulture,
                $"ExpiresAt: the instant asked for is {instant:o} and it is not after now, so the share would be expired before the link was sent");
        }

        // The cap's own bounds, refused by the routine that owns them rather than
        // by a copy of the two numbers.
        return BitrateCap.Refuse(request.MaxBitrateMbps) is { } cap
            ? "MaxBitrateMbps: " + cap
            : null;
    }

    /// <summary>
    /// When a share created now stops resolving.
    /// </summary>
    /// <param name="configuration">The plugin configuration the default lifetime comes from.</param>
    /// <param name="request">What the operator asked for.</param>
    /// <param name="now">The instant the create is happening at.</param>
    /// <returns>The expiry instant, in UTC.</returns>
    /// <remarks>
    /// The instant an operator supplied is taken as the instant it is, converted
    /// to UTC rather than reinterpreted, which is what makes a value typed in a
    /// local zone mean the same thing after the server's offset moves. An absent
    /// value takes the configured default lifetime, so an operator with nothing to
    /// say about expiry still gets a share that expires.
    /// </remarks>
    public static DateTimeOffset ExpiryOf(PluginConfiguration configuration, ShareCreationRequest request, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(request);

        return request.ExpiresAt is { } instant
            ? instant.ToUniversalTime()
            : now + ShareConfiguration.DefaultShareLifetimeFrom(configuration);
    }

    /// <summary>
    /// The record a create writes.
    /// </summary>
    /// <param name="configuration">The plugin configuration the defaults come from.</param>
    /// <param name="request">What the operator asked for.</param>
    /// <param name="shareId">The identifier the share is known by.</param>
    /// <param name="createdByUserId">The account the server says is asking.</param>
    /// <param name="guestUserIds">The accounts this plugin made for the share, or none while the record is being judged before they exist.</param>
    /// <param name="tokenHash">The keyed hash of the token the link carries.</param>
    /// <param name="now">The instant the create is happening at.</param>
    /// <returns>The record.</returns>
    /// <remarks>
    /// <para>
    /// Called twice on the way through the route, with the same identifier, the
    /// same instants and the same hash, and with the guests empty the first time.
    /// The first call is what the ceilings are read against before any account is
    /// made, which is what keeps an operator's over-long lifetime from costing a
    /// created account; the second is what is written. Nothing
    /// <see cref="ShareBounds.Refuse"/> reads moves between the two, so the early
    /// answer and the authoritative one differ only where a second administrator
    /// created a share in between, which is the case the check inside the mutation
    /// exists for.
    /// </para>
    /// <para>
    /// Every invited account is also a created one. This route has no way to
    /// invite an account somebody else made, so the two lists are the same list,
    /// and <see cref="ShareRecord.PluginCreatedUserIds"/> is what lets a later
    /// removal path tell that apart from a record that arrived another way (#144).
    /// </para>
    /// </remarks>
    public static ShareRecord Record(
        PluginConfiguration configuration,
        ShareCreationRequest request,
        Guid shareId,
        Guid createdByUserId,
        IReadOnlyList<Guid> guestUserIds,
        string tokenHash,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(guestUserIds);

        return new ShareRecord
        {
            SchemaVersion = ShareRecord.CurrentSchemaVersion,
            Id = shareId,
            ItemId = request.ItemId,
            InvitedUserIds = guestUserIds,
            PluginCreatedUserIds = guestUserIds,
            CreatedByUserId = createdByUserId,
            CreatedAt = now,
            ExpiresAt = ExpiryOf(configuration, request, now),
            MaxBitrateBitsPerSecond = request.MaxBitrateMbps is null
                ? BitrateCap.DefaultForNewShares(configuration)
                : BitrateCap.InBitsPerSecond(request.MaxBitrateMbps),
            TokenHash = tokenHash,
        };
    }

    /// <summary>
    /// The path a share link points at.
    /// </summary>
    /// <param name="token">The token the link carries.</param>
    /// <returns>The path, beginning with a slash.</returns>
    /// <remarks>
    /// Composed here rather than written out at the route, so the link an operator
    /// is handed and the route a guest arrives at cannot be spelled differently.
    /// <c>ApiSurfaceTests</c> compares the route against the page; this is what
    /// keeps the link in the same comparison.
    /// </remarks>
    public static string PathOf(string token)
    {
        ArgumentException.ThrowIfNullOrEmpty(token);

        return "/ShareLinks/Guest/" + token;
    }

    // The names, judged as a set rather than one at a time, because two of the
    // three failures here are about the list and not about any one entry.
    private static string? RefuseGuests(IReadOnlyList<string>? names)
    {
        if (names is null || names.Count == 0)
        {
            return "GuestNames: a share is for somebody and this request names nobody";
        }

        if (names.Count > MaximumGuestsPerShare)
        {
            return string.Create(
                CultureInfo.InvariantCulture,
                $"GuestNames: {names.Count} guests were asked for and the most one share may have is {MaximumGuestsPerShare}");
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < names.Count; index++)
        {
            var name = names[index];
            if (string.IsNullOrWhiteSpace(name))
            {
                return "GuestNames: one of the names is blank, and an account with no name is one nobody can find in the server's user list";
            }

            if (!seen.Add(name.Trim()))
            {
                // Not left to the server. Creating the first of two identical
                // names succeeds and the second is refused, which would leave the
                // create half done for a mistake that is visible before anything
                // is made.
                return string.Create(
                    CultureInfo.InvariantCulture,
                    $"GuestNames: the name {name.Trim()} is asked for twice, and two guests cannot share one account");
            }
        }

        return null;
    }
}
