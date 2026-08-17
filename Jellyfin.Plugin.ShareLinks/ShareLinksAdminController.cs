using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Common.Api;
using MediaBrowser.Controller.Net;
using MediaBrowser.Controller.Session;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.ShareLinks;

/// <summary>
/// The routes an administrator lists and revokes shares with (#67).
/// </summary>
/// <remarks>
/// <para>
/// A second controller rather than two more actions on the guest one, because
/// what admits a caller is declared on the type here. The guest route is reached
/// by anybody the server has signed in and these are reached only under the
/// server's elevation policy, and two policies on one controller is one attribute
/// away from a route that admits the wrong set.
/// </para>
/// <para>
/// The policy is spelled with the server's own constant rather than with the
/// string it holds. A policy name that is one character wrong compiles, deploys
/// and is refused by the server at request time, which is a route nobody can use
/// and nobody was told about; taking the constant makes a rename in the server a
/// compile error here. <c>RoutePolicyTests</c> reads the compiled attribute back
/// and is what refuses the mistake this paragraph is about.
/// </para>
/// <para>
/// Nothing here decides whether a share resolves. That is
/// <see cref="ShareResolution"/>'s and the guest route's, and an operator surface
/// that answered the question a second time would be the second copy the
/// <c>share-decision-comes-from-one-routine</c> invariant exists against. What
/// these two actions do is read what the store holds and ask the store to write a
/// revocation, and both of those already exist over
/// <see cref="IShareStore"/> so that the rule is the same wherever it is asked
/// for. The one thing revocation reaches beyond the store is the server's own
/// session list, which is #55 and is <see cref="GuestSessions"/>.
/// </para>
/// <para>
/// A store that cannot be read is an error to this caller and a refusal to the
/// guest, and the difference is deliberate. The guest is told nothing because a
/// fault told to a guest is a fault told to whoever holds the link; the operator
/// is the person who has to act on it, and an empty listing handed to them reads
/// as a server with no shares on it.
/// </para>
/// <para>
/// What is not here is the create route. #67 asks for three and this file carries
/// two: creating a share now also creates the account it is for, which is
/// decision 2 of #94 and the lifecycle in <c>docs/guest-accounts.md</c>, and that
/// is a surface of its own rather than a third action on the end of this one.
/// </para>
/// </remarks>
[ApiController]
[Authorize(Policy = Policies.RequiresElevation)]
[Route("ShareLinks")]
public class ShareLinksAdminController : ControllerBase
{
    private readonly IShareStore _store;
    private readonly IAuthorizationContext _authorizationContext;
    private readonly ISessionManager _sessionManager;
    private readonly TimeProvider _clock;
    private readonly ILogger<ShareLinksAdminController> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ShareLinksAdminController"/> class.
    /// </summary>
    /// <param name="store">Where the share records are kept.</param>
    /// <param name="authorizationContext">The server's own answer to who is asking.</param>
    /// <param name="sessionManager">The server's session list, which a revocation reaches into (#55).</param>
    /// <param name="clock">The clock a state and a revocation instant are read from.</param>
    /// <param name="logger">Where this surface's lines go (#27).</param>
    public ShareLinksAdminController(
        IShareStore store,
        IAuthorizationContext authorizationContext,
        ISessionManager sessionManager,
        TimeProvider clock,
        ILogger<ShareLinksAdminController> logger)
    {
        _store = store;
        _authorizationContext = authorizationContext;
        _sessionManager = sessionManager;
        _clock = clock;
        _logger = logger;
    }

    /// <summary>
    /// Lists every share the store holds, with what each one is doing now.
    /// </summary>
    /// <param name="cancellationToken">Cancels the read of the store.</param>
    /// <returns>The shares, or an error where the store cannot be read.</returns>
    /// <remarks>
    /// <para>
    /// Every record and not only the live ones. A share that has stopped working
    /// is the share an operator is looking for when somebody says a link no
    /// longer opens, and hiding it would leave them unable to tell a share that
    /// expired from one that was never made. <see cref="ShareSummary.State"/> is
    /// what keeps the two readings apart, which is #39's third clause.
    /// </para>
    /// <para>
    /// The order is the store's own. Sorting is a question about the page rather
    /// than about the route, and a route that sorted would be deciding it for
    /// #70.
    /// </para>
    /// </remarks>
    [HttpGet("Shares")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<IReadOnlyList<ShareSummary>>> List(CancellationToken cancellationToken)
    {
        IReadOnlyList<ShareRecord> records;
        try
        {
            records = await _store.ReadAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (ShareStoreUnreadableException)
        {
            ShareLog.StoreUnreadable(_logger);
            return TheStoreCouldNotBeRead();
        }

        var now = _clock.GetUtcNow();
        var listing = new List<ShareSummary>(records.Count);
        for (var index = 0; index < records.Count; index++)
        {
            listing.Add(ShareSummary.Of(records[index], now));
        }

        return Ok(listing);
    }

    /// <summary>
    /// Revokes a share.
    /// </summary>
    /// <param name="shareId">The share to revoke.</param>
    /// <param name="request">What the operator wants recorded against it, or nothing.</param>
    /// <param name="cancellationToken">Cancels the change.</param>
    /// <returns>The share as it stands afterwards, or a refusal.</returns>
    /// <remarks>
    /// <para>
    /// A share the store does not hold is not found, and that is the one place
    /// this surface tells a caller more than the guest route does. It is right
    /// here for the same reason it is wrong there: the caller is an administrator
    /// of this server, and an operator who cannot tell a revocation that missed
    /// from one that worked will press it again and believe the second press.
    /// </para>
    /// <para>
    /// Pressing it twice succeeds and changes nothing, which is
    /// <see cref="ShareStoreExtensions.RevokeAsync"/>'s rule rather than one this
    /// route restates. The answer carries the record as it stands, so an operator
    /// who pressed twice sees the first press's instant, reason and revoker
    /// rather than their own.
    /// </para>
    /// <para>
    /// The record stopping is half of what an operator asked for and the sessions
    /// it was keeping alive are the other half (#55). They are ended after the
    /// store has written, never before: a revocation that signed a guest out and
    /// was then refused by the store would have stopped a person watching a share
    /// that is still live, and there is nothing to sign them back in with.
    /// <see cref="GuestSessions"/> is where which accounts those are is decided.
    /// </para>
    /// <para>
    /// The store is read a second time for that, because which other shares still
    /// name a guest is what decides whether their session survives, and the answer
    /// has to be the store as it stands after the revocation rather than before
    /// it. A read that fails there is answered as an error even though the record
    /// was written, because the alternative is telling an operator that a share
    /// was stopped while a guest goes on watching it. Pressing revoke again
    /// re-attempts both halves and changes nothing that already happened.
    /// </para>
    /// </remarks>
    [HttpPost("Shares/{shareId}/Revoke")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<ShareSummary>> Revoke(
        [FromRoute] Guid shareId,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] ShareRevocationRequest? request,
        CancellationToken cancellationToken)
    {
        // The elevation policy has already refused a caller the server has not
        // identified, so this cannot be reached without one. It is checked
        // anyway, because the alternative is writing the empty identifier into
        // the field that says who revoked the share, and a revoker nobody can be
        // asked about is worse than no revocation at all.
        if (await WhoIsAsking().ConfigureAwait(false) is not { } caller)
        {
            return StatusCode(StatusCodes.Status403Forbidden);
        }

        ShareRecord? outcome;
        try
        {
            outcome = await _store.RevokeAsync(
                shareId,
                caller,
                _clock.GetUtcNow(),
                _logger,
                request?.Reason,
                cancellationToken).ConfigureAwait(false);
        }
        catch (ShareStoreUnreadableException)
        {
            ShareLog.StoreUnreadable(_logger);
            return TheStoreCouldNotBeRead();
        }
        catch (ShareStoreUnwritableException)
        {
            // A revocation the store could not write is a share that is still
            // live, and an operator told it succeeded would stop looking. The
            // exception carries the path; this answer carries that it failed.
            return TheStoreCouldNotBeRead();
        }

        if (outcome is not { } record)
        {
            return NotFound();
        }

        IReadOnlyList<ShareRecord> remaining;
        try
        {
            remaining = await _store.ReadAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (ShareStoreUnreadableException)
        {
            ShareLog.StoreUnreadable(_logger);
            return TheStoreCouldNotBeRead();
        }

        var now = _clock.GetUtcNow();
        await GuestSessions.EndAsync(
            _sessionManager,
            GuestSessions.LeftWithNothingToWatch(remaining, record, now)).ConfigureAwait(false);

        return Ok(ShareSummary.Of(record, now));
    }

    // One answer for a store this plugin cannot use, made in one place so that
    // the read path and the write path cannot drift into two. It carries no body,
    // because what an operator needs is in the log line beside it and a path in a
    // response is a path in whatever reads the response.
    private static StatusCodeResult TheStoreCouldNotBeRead()
        => new StatusCodeResult(StatusCodes.Status500InternalServerError);

    // The identity comes from the server, exactly as it does on the guest route
    // (#53). Nothing in the request says who is asking.
    private async Task<Guid?> WhoIsAsking()
    {
        var authorization = await _authorizationContext.GetAuthorizationInfo(Request).ConfigureAwait(false);

        return authorization.IsAuthenticated && authorization.UserId != Guid.Empty
            ? authorization.UserId
            : null;
    }
}
