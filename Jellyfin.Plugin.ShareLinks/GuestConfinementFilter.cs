using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.ShareLinks;

/// <summary>
/// The one request-path surface that confines a guest and applies the ceiling (#239).
/// </summary>
/// <remarks>
/// <para>
/// <c>docs/guest-confinement.md</c> chose a filter of this plugin's own over the
/// account's allowed tags, and the decision recorded on #44 gives it this shape:
/// one registration carrying both questions rather than two that can drift apart.
/// The questions are answered from the store, per request, and nothing is written
/// onto an account or into the library.
/// </para>
/// <para>
/// It is an authorization filter and not an action filter, which is what puts it
/// ahead of model binding. That matters for the ceiling leg: the requested value
/// is read out of the query and lowered there, so what the action binds is the
/// lowered value rather than the one the client asked for.
/// </para>
/// <para>
/// What it decides is <see cref="GuestConfinement.Decide"/>'s and not this
/// type's. Everything here is the adapter: which item the path names, what the
/// account and the server carry as their own ceilings, and what a refusal looks
/// like on the wire. The decision is a routine a test can drive with no request
/// at all, which is what <c>docs/testing.md</c> requires of everything in this
/// repository.
/// </para>
/// <para>
/// A refusal is a bare 404 with no body. It is the same answer the guest route
/// gives, for the reason #26 gives: a caller who can tell "not shared with you"
/// from "no such item" has been handed the difference, and the difference is what
/// an enumeration is built out of.
/// </para>
/// <para>
/// A store this plugin cannot read refuses rather than passes. It is the only
/// direction available: a filter that let a request through because it could not
/// read the records would turn a fault into the widest possible permission, and
/// the guest's own route already answers a fault as a refusal.
/// </para>
/// <para>
/// What this cannot do is stand in front of a route nobody added to
/// <see cref="ConfinedRoutes"/>. That is the accepted cost of the mechanism, it
/// is written down in the page that chose it, and it is why
/// <see cref="ConfinedRouteKind.NotJudged"/> exists as an answer of its own.
/// Whether the server applies this filter to any request at all was NOT measured:
/// it is a registration on the server's own pipeline and there is no server here.
/// </para>
/// </remarks>
public sealed class GuestConfinementFilter : IAsyncAuthorizationFilter
{
    private readonly IShareStore _store;
    private readonly IAuthorizationContext _authorizationContext;
    private readonly IUserManager _userManager;
    private readonly IServerConfigurationManager _serverConfiguration;
    private readonly TimeProvider _clock;
    private readonly ILogger<GuestConfinementFilter> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="GuestConfinementFilter"/> class.
    /// </summary>
    /// <param name="store">Where the share records are kept.</param>
    /// <param name="authorizationContext">The server's own answer to who is asking.</param>
    /// <param name="userManager">The server's own accounts, which is where the account's ceiling is read.</param>
    /// <param name="serverConfiguration">The server's own configuration, which is where the server-wide ceiling is read.</param>
    /// <param name="clock">The clock a record is judged live against.</param>
    /// <param name="logger">Where this surface's lines go (#27).</param>
    public GuestConfinementFilter(
        IShareStore store,
        IAuthorizationContext authorizationContext,
        IUserManager userManager,
        IServerConfigurationManager serverConfiguration,
        TimeProvider clock,
        ILogger<GuestConfinementFilter> logger)
    {
        _store = store;
        _authorizationContext = authorizationContext;
        _userManager = userManager;
        _serverConfiguration = serverConfiguration;
        _clock = clock;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var request = context.HttpContext.Request;
        var route = ConfinedRoutes.Judge(request.Path.Value);
        if (route.Kind == ConfinedRouteKind.NotJudged)
        {
            // Not an allowance. The list does not reach this path, so this plugin
            // is not standing in front of it, and saying so here rather than
            // returning early without a word is what keeps the hole readable.
            return;
        }

        var authorization = await _authorizationContext.GetAuthorizationInfo(request).ConfigureAwait(false);
        if (!authorization.IsAuthenticated || authorization.UserId == Guid.Empty)
        {
            // The server has not identified the caller, so there is no account to
            // confine. Whether such a request is answered at all is the server's
            // own authentication and not this plugin's.
            return;
        }

        IReadOnlyList<ShareRecord> records;
        try
        {
            records = await _store.ReadAsync(context.HttpContext.RequestAborted).ConfigureAwait(false);
        }
        catch (ShareStoreUnreadableException)
        {
            ShareLog.StoreUnreadable(_logger);
            context.Result = new NotFoundResult();
            return;
        }

        var decision = GuestConfinement.Decide(
            records,
            authorization.UserId,
            route.Kind == ConfinedRouteKind.NamesAnItem ? route.Item : null,
            TheAccountsOwnCeiling(authorization.UserId),
            TheServersOwnCeiling(),
            _clock.GetUtcNow());

        if (!decision.MayContinue)
        {
            context.Result = new NotFoundResult();
            return;
        }

        if (decision.Verdict == GuestVerdict.Reaches && decision.Cap.BitsPerSecond is { } ceiling)
        {
            ApplyTheCeiling(context, ceiling);
        }
    }

    /// <summary>
    /// The ceiling a request names, as the highest of the parameters it carries.
    /// </summary>
    /// <param name="query">The request's query.</param>
    /// <returns>The highest ceiling named, or <c>null</c> where the request named none this plugin recognises.</returns>
    /// <remarks>
    /// The highest and not the first, because what the refusal leg is about is
    /// whether the request asked for more than it may have, and a request naming
    /// two parameters has asked for the larger of them.
    /// </remarks>
    public static long? CeilingAskedFor(IQueryCollection query)
    {
        ArgumentNullException.ThrowIfNull(query);

        long? asked = null;
        for (var index = 0; index < ConfinedRoutes.RequestedCeilingParameters.Count; index++)
        {
            var name = ConfinedRoutes.RequestedCeilingParameters[index];
            if (!query.TryGetValue(name, out var written)
                || !long.TryParse(written.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
                || value <= 0)
            {
                continue;
            }

            asked = asked is { } highest ? Math.Max(highest, value) : value;
        }

        return asked;
    }

    // The two legs docs/bitrate-cap.md chose, told apart by which family the path
    // is in. On the route that answers with a ceiling the request is lowered, so
    // an honest client is handed a number it can meet. On the route that serves
    // bytes a request above the ceiling is refused, because lowering there would
    // be serving something other than what was asked for without saying so.
    private static void ApplyTheCeiling(AuthorizationFilterContext context, long ceiling)
    {
        var request = context.HttpContext.Request;
        var asked = CeilingAskedFor(request.Query);

        if (ConfinedRoutes.ServesAStream(request.Path.Value))
        {
            if (asked is { } wanted && wanted > ceiling)
            {
                context.Result = new NotFoundResult();
            }

            return;
        }

        if (!ConfinedRoutes.ReportsACeiling(request.Path.Value))
        {
            return;
        }

        if (asked is { } named && named <= ceiling)
        {
            return;
        }

        // Written whether or not the client named one. A request that named no
        // ceiling is a request that would be answered with the server's own
        // largest, and handing a guest that answer is the interception leg not
        // happening.
        var lowered = new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>(StringComparer.Ordinal);
        foreach (var pair in request.Query)
        {
            lowered[pair.Key] = pair.Value;
        }

        lowered["maxStreamingBitrate"] = ceiling.ToString(CultureInfo.InvariantCulture);
        request.Query = new QueryCollection(lowered);
    }

    // Both readings are ServerCeilings' and not this type's, because the
    // administrator listing needs the same two numbers read the same way (#64). A
    // second copy here is how the surface that describes the ceiling comes to
    // disagree with the surface that applies it.
    private long? TheAccountsOwnCeiling(Guid account)
        => ServerCeilings.OfAccount(_userManager, account);

    private long? TheServersOwnCeiling()
        => ServerCeilings.OfServer(_serverConfiguration);
}
