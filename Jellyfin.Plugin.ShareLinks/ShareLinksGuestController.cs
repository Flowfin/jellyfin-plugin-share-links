using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Controller.Net;
using MediaBrowser.Model.Plugins;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

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
/// </remarks>
[ApiController]
[Authorize]
[Route("ShareLinks")]
public class ShareLinksGuestController : ControllerBase
{
    private readonly IShareStore _store;
    private readonly ShareKeyFile _keyFile;
    private readonly IAuthorizationContext _authorizationContext;
    private readonly IPluginManager _pluginManager;
    private readonly TimeProvider _clock;

    /// <summary>
    /// Initializes a new instance of the <see cref="ShareLinksGuestController"/> class.
    /// </summary>
    /// <param name="store">Where the share records are kept.</param>
    /// <param name="keyFile">The file the install's keyed-hash key is kept in.</param>
    /// <param name="authorizationContext">The server's own answer to who is asking.</param>
    /// <param name="pluginManager">The server's own answer to what this plugin's status is.</param>
    /// <param name="clock">The clock an expiry is judged against.</param>
    public ShareLinksGuestController(
        IShareStore store,
        ShareKeyFile keyFile,
        IAuthorizationContext authorizationContext,
        IPluginManager pluginManager,
        TimeProvider clock)
    {
        _store = store;
        _keyFile = keyFile;
        _authorizationContext = authorizationContext;
        _pluginManager = pluginManager;
        _clock = clock;
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
            return TheOnlyRefusal();
        }

        var resolution = ShareResolution.Resolve(records, _keyFile, token, caller, StatusOfThisPlugin(), _clock);
        if (resolution.Share is not { } share)
        {
            return TheOnlyRefusal();
        }

        return Redirect(TheItemsAddress(share.ItemId));
    }

    /// <summary>
    /// The address the web client shows one item at.
    /// </summary>
    /// <param name="pathBase">What the server is mounted under, empty at the root.</param>
    /// <param name="itemId">The item the share names.</param>
    /// <returns>An address on this server, beginning with a slash.</returns>
    /// <remarks>
    /// <para>
    /// **This shape was NOT measured against a running web client.** It is where
    /// the client is mounted and how it addresses an item, taken as an assumption
    /// and written in one place so that correcting it is one line. Nothing in this
    /// repository can read the client: the plugin adds nothing to it, which is
    /// decision 4 of #94, and no test here may reach a server, which is
    /// <c>docs/testing.md</c>. What the tests below do hold is that the address
    /// carries the item the share named and carries the token nowhere.
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
