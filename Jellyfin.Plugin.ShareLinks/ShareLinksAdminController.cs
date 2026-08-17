using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.ShareLinks.Configuration;
using MediaBrowser.Common.Api;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Net;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.ShareLinks;

/// <summary>
/// The routes an administrator creates, lists and revokes shares with (#67).
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
/// for.
/// </para>
/// <para>
/// A store that cannot be read is an error to this caller and a refusal to the
/// guest, and the difference is deliberate. The guest is told nothing because a
/// fault told to a guest is a fault told to whoever holds the link; the operator
/// is the person who has to act on it, and an empty listing handed to them reads
/// as a server with no shares on it.
/// </para>
/// <para>
/// Creating a share also creates the accounts it is for, which is decision 2 of
/// #94 and the lifecycle <c>docs/guest-accounts.md</c> writes down. That makes the
/// create the one action here that changes something outside this plugin's own
/// store, and it is why it is written as a sequence with a way back rather than
/// as a read and a write: an account made for a record that was then refused is
/// an account on somebody's server that no later path would ever name.
/// </para>
/// </remarks>
[ApiController]
[Authorize(Policy = Policies.RequiresElevation)]
[Route("ShareLinks")]
public class ShareLinksAdminController : ControllerBase
{
    private readonly IShareStore _store;
    private readonly ShareKeyFile _keyFile;
    private readonly IUserManager _userManager;
    private readonly ILibraryManager _libraryManager;
    private readonly IPluginConfigurationSource _configuration;
    private readonly IAuthorizationContext _authorizationContext;
    private readonly TimeProvider _clock;
    private readonly ILogger<ShareLinksAdminController> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ShareLinksAdminController"/> class.
    /// </summary>
    /// <param name="store">Where the share records are kept.</param>
    /// <param name="keyFile">The file the install's keyed-hash key is kept in.</param>
    /// <param name="userManager">The server's own account management, which is what makes a guest account and what takes one away again.</param>
    /// <param name="libraryManager">The server's own answer to whether an item exists.</param>
    /// <param name="configuration">Where the operator's settings are read from, per request.</param>
    /// <param name="authorizationContext">The server's own answer to who is asking.</param>
    /// <param name="clock">The clock a state and a revocation instant are read from.</param>
    /// <param name="logger">Where this surface's lines go (#27).</param>
    public ShareLinksAdminController(
        IShareStore store,
        ShareKeyFile keyFile,
        IUserManager userManager,
        ILibraryManager libraryManager,
        IPluginConfigurationSource configuration,
        IAuthorizationContext authorizationContext,
        TimeProvider clock,
        ILogger<ShareLinksAdminController> logger)
    {
        _store = store;
        _keyFile = keyFile;
        _userManager = userManager;
        _libraryManager = libraryManager;
        _configuration = configuration;
        _authorizationContext = authorizationContext;
        _clock = clock;
        _logger = logger;
    }

    /// <summary>
    /// Creates a share, and the accounts it is for.
    /// </summary>
    /// <param name="request">The item, the guests, the expiry and the ceiling.</param>
    /// <param name="cancellationToken">Cancels the change.</param>
    /// <returns>The share, the link and one credential per guest, or a refusal.</returns>
    /// <remarks>
    /// <para>
    /// The link and the credentials are in this answer and in no other, and they
    /// cannot be asked for again. Only the keyed hash of the token is written
    /// down, so this plugin cannot rebuild the link, and the credential is handed
    /// to the server and kept nowhere here. <see cref="ShareCreated"/> is where
    /// that is argued and the listing route is asserted not to carry either.
    /// </para>
    /// <para>
    /// The order of the steps is the whole design of this action. Everything that
    /// can refuse without changing anything happens first: the request's own
    /// shape, the key, the link, the ceilings read off the store, the item, and
    /// the names. Only then are the accounts made, and only then is the record
    /// written. So the ordinary mistakes an operator makes - a lifetime past the
    /// ceiling, a name that is taken, an item that is not there - cost nothing at
    /// all.
    /// </para>
    /// <para>
    /// What that order cannot remove is the race, and it is not pretended away.
    /// The authoritative ceiling check is inside the store mutation, because a
    /// check outside it can be overtaken by a second administrator creating at the
    /// same moment, so a create that loses that race has already made its
    /// accounts. Those accounts are then removed, and this is the one place this
    /// plugin deletes one. It is bounded as narrowly as it can be: only
    /// identifiers <see cref="IUserManager.CreateUserAsync"/> returned inside this
    /// call, only on a path where no record names them, and never a name or an
    /// identifier that arrived in the request. Leaving them instead was the
    /// alternative, and it means an account with a credential nobody was ever
    /// shown, hidden from the server's user list, that no later path can reach:
    /// removal follows the record, and there is no record.
    /// </para>
    /// <para>
    /// A configuration outside its own bounds answers with a fault rather than a
    /// refusal, and that answer carries a body where the store's faults do not.
    /// The sentence names a setting and a number, which is what an operator needs
    /// to find the line to change; the store's carries a path, which is why it is
    /// kept to the log.
    /// </para>
    /// </remarks>
    [HttpPost("Shares")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<ShareCreated>> Create(
        [FromBody] ShareCreationRequest? request,
        CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return BadRequest("a create needs a body saying what is being shared and who with");
        }

        // The elevation policy has already refused a caller the server has not
        // identified. It is checked anyway, for the reason the revocation gives:
        // the alternative is writing the empty identifier into the field that says
        // who made the share.
        if (await WhoIsAsking().ConfigureAwait(false) is not { } caller)
        {
            return StatusCode(StatusCodes.Status403Forbidden);
        }

        if (_configuration.Current() is not { } configuration)
        {
            return AServerFault();
        }

        // Every setting this action is about to read, judged by the routine that
        // owns it, so that none of the readings below can throw. A configuration
        // saved through the page cannot be in this state (#71); one edited by hand
        // can.
        if (ShareConfiguration.Refuse(configuration) is { } settings)
        {
            return AServerFault(settings);
        }

        var now = _clock.GetUtcNow();
        if (ShareCreation.Refuse(request, now) is { } refusal)
        {
            return BadRequest(refusal);
        }

        byte[] key;
        try
        {
            key = _keyFile.Read();
        }
        catch (ShareKeyUnavailableException)
        {
            // A key this install cannot read is a share nobody could ever open,
            // so it is refused before anything is made rather than discovered by
            // the first guest.
            return AServerFault();
        }

        var token = ShareTokens.Mint();
        Uri link;
        try
        {
            link = ShareLinkBuilder.Build(configuration.PublicBaseUrl, TheAddressTheRequestClaims(), ShareCreation.PathOf(token));
        }
        catch (InvalidOperationException error)
        {
            // Nothing to build a link from is a refusal and not a fault. The
            // sentence says which setting to fill in, and a share created without
            // a link is a record an operator cannot use and cannot re-read the
            // link out of.
            return BadRequest(error.Message);
        }

        var bounds = ShareBounds.From(configuration);
        var shareId = Guid.NewGuid();
        var tokenHash = ShareTokenHash.Compute(key, token);

        IReadOnlyList<ShareRecord> existing;
        try
        {
            existing = await _store.ReadAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (ShareStoreUnreadableException)
        {
            ShareLog.StoreUnreadable(_logger);
            return AServerFault();
        }

        // The same routine the store runs inside its own mutation, read here
        // against the same records so that an operator's over-long lifetime or a
        // server at its ceiling costs no account. The one inside the mutation is
        // still what decides.
        var judged = ShareCreation.Record(configuration, request, shareId, caller, Array.Empty<Guid>(), tokenHash, now);
        if (bounds.Refuse(bounds.Retained(existing, now), judged, now) is { } bound)
        {
            return BadRequest(bound);
        }

        if (_libraryManager.GetItemById(request.ItemId) is null)
        {
            return BadRequest("ItemId: this server holds no item with that identifier");
        }

        var names = request.GuestNames!;
        for (var index = 0; index < names.Count; index++)
        {
            var name = names[index].Trim();
            if (_userManager.GetUserByName(name) is not null)
            {
                // Asked before creating rather than caught afterwards, because a
                // list whose third name is taken would otherwise leave two
                // accounts made for a share that was refused. The server is asked
                // again by the creation itself, which is what covers the moment
                // between this answer and that call.
                return BadRequest("GuestNames: this server already has an account called " + name);
            }
        }

        var guests = new List<GuestCredential>(names.Count);
        try
        {
            for (var index = 0; index < names.Count; index++)
            {
                guests.Add(await TheGuestAccountFor(names[index].Trim(), configuration).ConfigureAwait(false));
            }
        }
        catch (ArgumentException error)
        {
            // The name was taken between the check above and the call. Everything
            // made so far goes, because a half-made share is not a share.
            await RemoveTheAccountsThisCallMade(guests).ConfigureAwait(false);
            return BadRequest("GuestNames: " + error.Message);
        }

        var record = ShareCreation.Record(configuration, request, shareId, caller, Identifiers(guests), tokenHash, now);
        try
        {
            await _store.AddAsync(record, bounds, now, _logger, cancellationToken).ConfigureAwait(false);
        }
        catch (ShareBoundExceededException error)
        {
            await RemoveTheAccountsThisCallMade(guests).ConfigureAwait(false);
            return BadRequest(error.Message);
        }
        catch (ShareStoreUnreadableException)
        {
            await RemoveTheAccountsThisCallMade(guests).ConfigureAwait(false);
            ShareLog.StoreUnreadable(_logger);
            return AServerFault();
        }
        catch (ShareStoreUnwritableException)
        {
            await RemoveTheAccountsThisCallMade(guests).ConfigureAwait(false);
            return AServerFault();
        }

        return Ok(new ShareCreated
        {
            Share = ShareSummary.Of(record, now),
            Link = link,
            Guests = guests,
        });
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

        return outcome is { } record
            ? Ok(ShareSummary.Of(record, _clock.GetUtcNow()))
            : NotFound();
    }

    // One answer for a store this plugin cannot use, made in one place so that
    // the read path and the write path cannot drift into two. It carries no body,
    // because what an operator needs is in the log line beside it and a path in a
    // response is a path in whatever reads the response.
    private static StatusCodeResult TheStoreCouldNotBeRead() => AServerFault();

    // The same answer, and the same absence of a body, for every state that is
    // this server's fault rather than the caller's.
    private static StatusCodeResult AServerFault()
        => new StatusCodeResult(StatusCodes.Status500InternalServerError);

    // A fault that does carry a sentence, because the sentence names a setting and
    // a bound rather than a path, and the person reading it is the person who
    // edits that setting.
    private static ObjectResult AServerFault(string detail)
        => new ObjectResult(detail) { StatusCode = StatusCodes.Status500InternalServerError };

    // The accounts, in the order the operator named them, which is the order the
    // credentials come back in.
    private static Guid[] Identifiers(List<GuestCredential> guests)
    {
        var identifiers = new Guid[guests.Count];
        for (var index = 0; index < guests.Count; index++)
        {
            identifiers[index] = guests[index].UserId;
        }

        return identifiers;
    }

    // One account, made and narrowed and given something to sign in with. The
    // three calls are one step because a server holding an account this plugin
    // made but has not narrowed is a server holding an account with the defaults
    // somebody else chose, and the window between them is not one to widen.
    //
    // The credential is drawn by the routine that already draws token bytes,
    // which the token-bytes-come-from-one-routine invariant refuses a second of,
    // and it is returned rather than stored: nothing in this plugin writes one
    // down.
    private async Task<GuestCredential> TheGuestAccountFor(string name, PluginConfiguration configuration)
    {
        var account = await _userManager.CreateUserAsync(name).ConfigureAwait(false);
        await _userManager.UpdatePolicyAsync(account.Id, GuestPolicy.Create(GuestPolicy.MaxActiveSessionsFrom(configuration))).ConfigureAwait(false);

        var credential = ShareTokens.Mint();
        await _userManager.ChangePassword(account.Id, credential).ConfigureAwait(false);

        return new GuestCredential
        {
            UserId = account.Id,
            Name = account.Username,
            Credential = credential,
        };
    }

    // The way back out of a create that got as far as making accounts and no
    // further. Only what this call made, only while no record names it.
    //
    // A removal that itself fails is not turned into a success and is not turned
    // into a different answer either: the caller is told the create failed, which
    // is true, and the accounts that are left are what an operator finds in the
    // server's own user list, hidden, with the names they asked for. There is no
    // line for it because there is no field a line could carry that
    // docs/logging.md admits, and inventing one here would put a person's name in
    // the log.
    private async Task RemoveTheAccountsThisCallMade(List<GuestCredential> guests)
    {
        for (var index = 0; index < guests.Count; index++)
        {
            try
            {
                await _userManager.DeleteUserAsync(guests[index].UserId).ConfigureAwait(false);
            }
            catch (ArgumentException)
            {
                // The account is already gone, which is the state this was after.
            }
        }
    }

    // What the request claims this server is reachable at, which is read only
    // where the operator has configured nothing. ShareLinkBuilder is where that
    // trust boundary is decided and this is the one place the parts are handed to
    // it.
    private string? TheAddressTheRequestClaims()
        => ShareLinkBuilder.FromRequestParts(Request.Scheme, Request.Host.Value, Request.PathBase.Value);

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
