using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Net;
using MediaBrowser.Model.Plugins;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.ShareLinks;

/// <summary>
/// The one route a share link points at (#68).
/// </summary>
/// <remarks>
/// <para>
/// The token is a path segment rather than a value inside the request, so a
/// request carrying no token does not match this route at all and is refused
/// before any code here runs. <c>docs/leaked-link.md</c> is where that was
/// decided and this is where it becomes true.
/// </para>
/// <para>
/// A plain authorize attribute and no policy name: the caller is whoever the
/// server has already signed in. Anonymous access is not an option here and is
/// not a setting, which is #53, and two guards refuse it independently - the
/// text lint over these sources and the reflection guard over the compiled
/// assembly in <c>RoutePolicyTests</c>.
/// </para>
/// <para>
/// Nothing here decides anything. The caller comes from the server's own
/// authorization context, the records come from the store, and whether the two
/// make a share is <see cref="ShareResolution"/>'s, which is the single routine
/// every route has to call and which the lint refuses a second copy of. What is
/// left for this file is reading, and saying one of exactly two things.
/// </para>
/// <para>
/// The two things. A share that resolved sends the caller to the item. Everything
/// else is one refusal, the same status and the same empty body whatever the
/// reason, because a caller who can tell a token that names nothing from a token
/// that names something they may not have is a caller who can map what exists
/// (#26). The reason survives in the result this route throws away, for the
/// operator surface that #67 and #70 build.
/// </para>
/// <para>
/// <b>There is a third thing, and it is an exception to #26 rather than a second
/// refusal shape beside it.</b> A caller who got past the resolution holds a valid
/// share and is one of the accounts it names, so they are already inside and
/// telling them why nothing plays leaks nothing outward. The decision of
/// 2026-08-24 on #63 granted that for one condition only - the share's ceiling
/// cannot be met for the item - and #284 is where it is spent.
/// <c>docs/bitrate-cap.md</c> carries the exception with the decision that
/// granted it.
/// </para>
/// <para>
/// It is held to that one caller and that one condition, and the shape is what
/// holds it there. The bare refusal keeps its own status, so an unauthenticated
/// caller, a caller with no valid share and a caller who is not invited all get
/// the same bytes they got before; the condition answers on a status of its own
/// rather than on a body hung off the bare one, so the two cannot be confused for
/// each other by anybody reading either.
/// <c>GuestRouteTests.EveryOtherRefusalOnThisRouteIsUnchanged</c> is what proves
/// the exception stopped where it was meant to.
/// </para>
/// </remarks>
[ApiController]
[Authorize]
[Route("ShareLinks")]
public class ShareLinksGuestController : ControllerBase
{
    /// <summary>
    /// What a guest is told when the ceiling on their share cannot be met for the
    /// item it names (#284).
    /// </summary>
    /// <remarks>
    /// <para>
    /// It names the condition and nothing else. Not the ceiling, not what the
    /// item can be played at, not which of the three ceilings was the one holding,
    /// and not who made the share. Each of those is a number or a name a guest has
    /// no use for and an operator can already read on their own surface, and the
    /// exception this sentence is spent on was granted for the condition rather
    /// than for the arithmetic behind it.
    /// </para>
    /// <para>
    /// One constant rather than a sentence assembled at the refusal, so that what
    /// a guest is told is a fixed string with nothing of the request in it. A
    /// message built from anything a caller sent is how a caller's own bytes come
    /// back to them.
    /// </para>
    /// </remarks>
    public const string TheCapCannotBeMetHere =
        "This link limits playback quality, and that limit cannot be met for what the link points at, so nothing can be played through it. Ask whoever shared it to raise the limit.";

    private readonly IShareStore _store;
    private readonly ShareKeyFile _keyFile;
    private readonly IAuthorizationContext _authorizationContext;
    private readonly IPluginManager _pluginManager;
    private readonly ILibraryManager _libraryManager;
    private readonly IMediaSourceManager _mediaSources;
    private readonly IUserManager _userManager;
    private readonly IServerConfigurationManager _serverConfiguration;
    private readonly TimeProvider _clock;
    private readonly ILogger<ShareLinksGuestController> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ShareLinksGuestController"/> class.
    /// </summary>
    /// <param name="store">Where the share records are kept.</param>
    /// <param name="keyFile">The file the install's keyed-hash key is kept in.</param>
    /// <param name="authorizationContext">The server's own answer to who is asking.</param>
    /// <param name="pluginManager">The server's own answer to what this plugin's status is.</param>
    /// <param name="libraryManager">The server's own answer to whether an item is still there (#39).</param>
    /// <param name="mediaSources">The server's own answer to what the item can be played at (#284).</param>
    /// <param name="userManager">The server's own accounts, which is where an account's own ceiling and its transcode permission are read (#284).</param>
    /// <param name="serverConfiguration">The server's own configuration, which is where the server-wide ceiling is read (#64).</param>
    /// <param name="clock">The clock an expiry is judged against.</param>
    /// <param name="logger">Where this route's two lines go (#27).</param>
    public ShareLinksGuestController(
        IShareStore store,
        ShareKeyFile keyFile,
        IAuthorizationContext authorizationContext,
        IPluginManager pluginManager,
        ILibraryManager libraryManager,
        IMediaSourceManager mediaSources,
        IUserManager userManager,
        IServerConfigurationManager serverConfiguration,
        TimeProvider clock,
        ILogger<ShareLinksGuestController> logger)
    {
        _store = store;
        _keyFile = keyFile;
        _authorizationContext = authorizationContext;
        _pluginManager = pluginManager;
        _libraryManager = libraryManager;
        _mediaSources = mediaSources;
        _userManager = userManager;
        _serverConfiguration = serverConfiguration;
        _clock = clock;
        _logger = logger;
    }

    /// <summary>
    /// Opens a share.
    /// </summary>
    /// <param name="token">The token out of the link.</param>
    /// <param name="cancellationToken">Cancels the read of the store.</param>
    /// <returns>The item's address in the web client, or a refusal that says nothing.</returns>
    /// <remarks>
    /// The store being unreadable is a refusal here rather than an error, for the
    /// same reason an unreadable key is a refusal inside the decision: a caller
    /// learns nothing either way, and a server fault told to a guest is a server
    /// fault told to whoever holds the link.
    /// </remarks>
    [HttpGet("Guest/{token}")]
    [ProducesResponseType(StatusCodes.Status302Found)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult> Open([FromRoute] string token, CancellationToken cancellationToken)
    {
        var caller = await WhoIsAsking().ConfigureAwait(false);

        IReadOnlyList<ShareRecord> records;
        try
        {
            records = await _store.ReadAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (ShareStoreUnreadableException)
        {
            // Not one of the decision's reasons, because the decision was never
            // made. It is a state an operator has to act on rather than one more
            // refused token, so it is a warning and it is not dressed up as a
            // refusal code that would read as a token that named nothing.
            ShareLog.StoreUnreadable(_logger);
            return TheOnlyRefusal();
        }

        var resolution = ShareResolution.Resolve(
            records,
            _keyFile,
            token,
            caller,
            StatusOfThisPlugin(),
            _clock,
            TheServerStillHoldsTheItem);
        if (resolution.Share is not { } share)
        {
            ShareLog.Refused(_logger, resolution.Refusal);
            return TheOnlyRefusal();
        }

        // The caller is not re-checked for null here and cannot be one: a
        // resolution that came back with a share has already refused a caller the
        // server did not identify, with ShareRefusal.CallerNotSignedIn. The
        // pattern is written out rather than dereferenced so that the guarantee is
        // a compiler's rather than a reader's memory of another file.
        if (caller is { } signedIn
            && await TheCapCannotBeMet(records, share, signedIn, cancellationToken).ConfigureAwait(false))
        {
            ShareLog.CapCannotBeMet(_logger, share);
            return TheCapIsUnmeetable();
        }

        ShareLog.Resolved(_logger, share);
        return Redirect(TheItemsAddress(share.ItemId));
    }

    // Whether this share's ceiling can be met for its item, for this account.
    //
    // The ceiling is GuestConfinement.Decide's rather than a fourth comparison
    // written here, so the number a guest is refused against is the number the
    // request-path filter would apply and the number the operator surface shows.
    // A share carrying no ceiling in force asks the server nothing at all, which
    // is what keeps the lookup off every ordinary open.
    //
    // Every state that is not the condition returns false, including the ones
    // that are absences. An account or an item the server does not hand back is a
    // question this routine cannot answer, and CapReach.NotKnown is already the
    // answer for a server that did not say enough. Failing the other way would
    // refuse a working share on the strength of a lookup that did not happen.
    private async Task<bool> TheCapCannotBeMet(
        IReadOnlyList<ShareRecord> records,
        ShareRecord share,
        Guid caller,
        CancellationToken cancellationToken)
    {
        var decision = GuestConfinement.Decide(
            records,
            caller,
            share.ItemId,
            ServerCeilings.OfAccount(_userManager, caller),
            ServerCeilings.OfServer(_serverConfiguration),
            _clock.GetUtcNow());

        if (decision.Cap.BitsPerSecond is not { } ceiling)
        {
            return false;
        }

        if (_userManager.GetUserById(caller) is not { } account
            || _libraryManager.GetItemById(share.ItemId) is not { } item)
        {
            return false;
        }

        var versions = await PlayableVersions
            .OfAsync(_mediaSources, item, account, cancellationToken)
            .ConfigureAwait(false);

        return BitrateCapReach.Of(ceiling, versions, PlayableVersions.MayTranscode(account))
            == CapReach.NothingCanBeServed;
    }

    // The server's answer to whether an item identifier still names something,
    // handed to the decision rather than read inside it (#39). A share whose item
    // a scan removed is refused here rather than redirected to an address that
    // names nothing, and the guest is told the same nothing every other refusal
    // gives, which is #26.
    private bool TheServerStillHoldsTheItem(Guid itemId) => _libraryManager.GetItemById(itemId) is not null;

    /// <summary>
    /// The address the web client shows one item at.
    /// </summary>
    /// <param name="pathBase">What the server is mounted under, empty at the root.</param>
    /// <param name="itemId">The item the share names.</param>
    /// <returns>An address on this server, beginning with a slash.</returns>
    /// <remarks>
    /// <para>
    /// **This shape was NOT measured against a running web client, and there is
    /// now a second reason it was not.** It is where the client is mounted and how
    /// it addresses an item, taken as an assumption and written in one place so
    /// that correcting it is one line. No test here may reach a server, which is
    /// <c>docs/testing.md</c>; and the one run that did have a real client in
    /// front of it never got this far, because a browser navigating to this route
    /// is refused with <c>401</c> before the redirect is built. #269 is that run
    /// and the answer chosen for it. What the tests below do hold is that the
    /// address carries the item the share named and carries the token nowhere.
    /// </para>
    /// <para>
    /// The plugin adds nothing to the web client today. That was decision 4 of
    /// #94 and #269 reopened it on 2026-08-24, so it is a statement about this
    /// tree rather than a decision still standing over it.
    /// </para>
    /// <para>
    /// The path base is kept rather than dropped, because a server behind a proxy
    /// at <c>/jellyfin</c> serves its client under that segment too, and an
    /// address that lost it would leave the guest at somebody else's root.
    /// </para>
    /// </remarks>
    public static string TheItemsAddress(string? pathBase, Guid itemId)
        => string.Format(
            CultureInfo.InvariantCulture,
            "{0}/web/#/details?id={1}",
            (pathBase ?? string.Empty).TrimEnd('/'),
            itemId.ToString("N", CultureInfo.InvariantCulture));

    // One refusal, made in one place, so that two of them cannot drift apart into
    // two answers a caller can tell apart. It carries no body and no header of
    // this plugin's own.
    private static NotFoundResult TheOnlyRefusal() => new NotFoundResult();

    // The one answer that says something, and the reason it does not weaken the
    // one above. It is reachable only by a caller who already holds a valid share
    // and is named by it, so nothing a caller could not already work out is being
    // handed over; every caller outside that set meets TheOnlyRefusal and its
    // bytes are untouched. The decision of 2026-08-24 on #63 is what granted it.
    //
    // A status of its own rather than a body hung off the bare refusal. Two
    // refusals that differ only in whether they carry a body are two answers a
    // reader has to compare byte by byte to tell apart, and the point of holding
    // this to one condition is that it is obvious which one somebody met. It is
    // not a "not found": the share is there and the item is there, and what
    // cannot be honoured is the pair of them, which is what a conflict is.
    private static ObjectResult TheCapIsUnmeetable()
        => new ObjectResult(TheCapCannotBeMetHere) { StatusCode = StatusCodes.Status409Conflict };

    private string TheItemsAddress(Guid itemId) => TheItemsAddress(Request.PathBase.Value, itemId);

    // The identity comes from the server and never from anything in the link
    // (#53). An unauthenticated caller cannot reach this method at all, because
    // the authorize attribute above refuses first; the null it would produce is
    // handled anyway, because a route that relies on an attribute it does not
    // check is a route that changes meaning when somebody edits the attribute.
    private async Task<Guid?> WhoIsAsking()
    {
        var authorization = await _authorizationContext.GetAuthorizationInfo(Request).ConfigureAwait(false);

        return authorization.IsAuthenticated && authorization.UserId != Guid.Empty
            ? authorization.UserId
            : null;
    }

    // A disabled plugin goes on running until the next restart, which is the
    // server's behaviour and not something this plugin can change from outside.
    // What it can do is ask, on the request, and refuse when the answer is not
    // active - so disable takes effect on the next request rather than on the
    // next restart. docs/plugin-lifecycle.md is where that is argued.
    //
    // An install this manager does not list is not active either, and neither is
    // one asked about before the plugin exists. Which of the non-active values
    // stands for those states says nothing to a caller, because every one of them
    // refuses through the same door; it names the state for a reader of the code.
    //
    // The identifier is read off this plugin rather than written out a second
    // time, because a Guid spelled twice is a Guid that can be spelled wrong once.
    private PluginStatus StatusOfThisPlugin()
    {
        if (Plugin.Instance?.Id is not { } thisPlugin)
        {
            return PluginStatus.Malfunctioned;
        }

        foreach (var plugin in _pluginManager.Plugins)
        {
            if (plugin.Manifest.Id == thisPlugin)
            {
                return plugin.Manifest.Status;
            }
        }

        return PluginStatus.Malfunctioned;
    }
}
