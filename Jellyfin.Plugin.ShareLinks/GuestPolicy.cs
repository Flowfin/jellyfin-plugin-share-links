using System;
using Jellyfin.Database.Implementations.Enums;
using MediaBrowser.Model.Users;

namespace Jellyfin.Plugin.ShareLinks;

/// <summary>
/// The account policy an invited guest gets, switch by switch (#57).
/// </summary>
/// <remarks>
/// <para>
/// Being allowed to watch is not one permission. It decomposes into playing,
/// resuming, seeking, marking watched, rating, downloading, casting, remote
/// controlling another session and joining a synchronised playback group, and
/// each of those is a separate answer. <c>docs/guest-capabilities.md</c> is where
/// every one of them is decided and why; this is the same list expressed as
/// something a server can be handed.
/// </para>
/// <para>
/// One routine rather than a policy assembled at whatever place happens to create
/// the account. A policy built property by property does not complain about the
/// property nobody set, so a second creation path would differ from the first in
/// exactly the switches nobody thought about.
/// </para>
/// <para>
/// What this cannot do is enforce anything. The server is what honours a policy,
/// and nothing here re-checks a capability when a request arrives. So the guard
/// on this file is that the plugin asks for the narrow answers, and a server that
/// ignored its own policy would pass every test in this repository.
/// </para>
/// <para>
/// Two fields are deliberately left where the server put them.
/// <c>MaxActiveSessions</c> is bounded by #56 and
/// <c>RemoteClientBitrateLimit</c> by #61 and #62; setting either here would
/// decide a number those issues own. Which items the account can see at all is
/// confinement, which is #52, and is a different question from what may be done
/// with the one item a share names.
/// </para>
/// </remarks>
public static class GuestPolicy
{
    /// <summary>
    /// A policy for a guest, with every switch this plugin decides set explicitly.
    /// </summary>
    /// <returns>The policy.</returns>
    public static UserPolicy Create()
    {
        var policy = new UserPolicy();
        Apply(policy);
        return policy;
    }

    /// <summary>
    /// Sets every switch this plugin decides on an existing policy.
    /// </summary>
    /// <param name="policy">The policy to set them on.</param>
    /// <remarks>
    /// Every switch is written rather than only the ones whose default is wrong.
    /// A default is the server's decision and it can move between server lines,
    /// which would silently widen a guest on an upgrade nobody connected to this
    /// plugin.
    /// </remarks>
    public static void Apply(UserPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);

        // Watching the shared item is the whole point.
        policy.EnableMediaPlayback = true;

        // Transcoding stays on, and this is the one place the narrow answer is
        // the wrong one: a bitrate ceiling below what direct play needs forces a
        // transcode, so an account that may not transcode turns every capped
        // share into a broken player.
        policy.EnableVideoPlaybackTranscoding = true;
        policy.EnableAudioPlaybackTranscoding = true;
        policy.EnablePlaybackRemuxing = true;

        // A link that works only inside the operator's own network is not a
        // share.
        policy.EnableRemoteAccess = true;

        // The shared item leaves the server permanently and no expiry undoes
        // that.
        policy.EnableContentDownloading = false;

        // Reaching another session, or another person's, is not part of being
        // handed one item.
        policy.EnableSharedDeviceControl = false;
        policy.EnableRemoteControlOfOtherUsers = false;
        policy.SyncPlayAccess = SyncPlayUserAccessType.None;

        // Nothing that changes the library, the server or the guest's own
        // account.
        policy.IsAdministrator = false;
        policy.EnableContentDeletion = false;
        policy.EnableMediaConversion = false;
        policy.EnableSyncTranscoding = false;
        policy.EnableLiveTvAccess = false;
        policy.EnableLiveTvManagement = false;
        policy.EnableCollectionManagement = false;
        policy.EnableSubtitleManagement = false;
        policy.EnableLyricManagement = false;
        policy.EnableUserPreferenceAccess = false;
        policy.EnablePublicSharing = false;

        // A guest account on the sign-in list tells everybody who visits the
        // server who has been invited to it, which is a disclosure the share
        // itself never made.
        policy.IsHidden = true;
    }
}
