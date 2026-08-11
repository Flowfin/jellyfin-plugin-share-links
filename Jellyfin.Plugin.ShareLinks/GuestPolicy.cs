using System;
using System.Globalization;
using Jellyfin.Database.Implementations.Enums;
using Jellyfin.Plugin.ShareLinks.Configuration;
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
/// One field is deliberately left where the server put it.
/// <c>RemoteClientBitrateLimit</c> is bounded by #61 and #62; setting it here
/// would decide a number those issues own. Which items the account can see at
/// all is confinement, which is #52, and is a different question from what may
/// be done with the one item a share names.
/// </para>
/// <para>
/// <c>MaxActiveSessions</c> is set here and is #56's. It is the one value in this
/// routine an operator chooses rather than one this plugin decides, so it arrives
/// as an argument and the bounds it is refused against are below.
/// </para>
/// </remarks>
public static class GuestPolicy
{
    /// <summary>
    /// The number of sessions one invited guest may hold at once when an operator
    /// has not chosen a different one.
    /// </summary>
    /// <remarks>
    /// A phone, a television and a laptop is the ordinary case #56 names, which is
    /// three. The default is above it rather than equal to it, because a session
    /// the server has not finished with still occupies a place, so a ceiling of
    /// exactly three turns an ordinary third device into a refusal the first time
    /// a guest reopens an app. Five leaves that headroom and is nowhere near the
    /// twenty places the same issue calls worth noticing. It is a judgement about
    /// how many devices one invited person uses and was not measured against a
    /// running server.
    /// </remarks>
    public const int DefaultMaxActiveSessions = 5;

    /// <summary>
    /// The lowest ceiling an operator may set.
    /// </summary>
    /// <remarks>
    /// One session, so an invited guest can watch on one device. Zero is what the
    /// server spells no ceiling at all as, which is the state this ceiling exists
    /// to leave, so it is refused rather than read as unlimited: serve one device
    /// and serve any number of them are opposite instructions and the difference
    /// between them must not be a typing mistake.
    /// </remarks>
    public const int MinimumMaxActiveSessions = 1;

    /// <summary>
    /// The highest ceiling an operator may set.
    /// </summary>
    /// <remarks>
    /// #56 names twenty places as the number worth noticing, and a ceiling at or
    /// past what is worth noticing is not doing the job the setting exists for. It
    /// is a judgement about one invited person's devices rather than a measurement,
    /// and an operator who wants a share open to more people than that wants more
    /// shares, which is what <c>MaxLiveSharesPerItem</c> bounds.
    /// </remarks>
    public const int MaximumMaxActiveSessions = 20;

    /// <summary>
    /// Why a session ceiling may not be used, or <c>null</c> when it may.
    /// </summary>
    /// <param name="maxActiveSessions">The ceiling an operator configured.</param>
    /// <returns>A sentence naming the bound that was missed, or <c>null</c>.</returns>
    /// <remarks>
    /// The sentence names the bound and the value, because an operator reading it
    /// has to know which end they are on, and because zero is the value most
    /// likely to be typed on purpose in the belief that it means no ceiling.
    /// </remarks>
    public static string? Refuse(int maxActiveSessions)
    {
        if (maxActiveSessions < MinimumMaxActiveSessions)
        {
            return string.Create(
                CultureInfo.InvariantCulture,
                $"the ceiling asked for is {maxActiveSessions} and the lowest that may be set is {MinimumMaxActiveSessions}; zero is how the server spells no ceiling at all");
        }

        if (maxActiveSessions > MaximumMaxActiveSessions)
        {
            return string.Create(
                CultureInfo.InvariantCulture,
                $"the ceiling asked for is {maxActiveSessions} and the highest that may be set is {MaximumMaxActiveSessions}");
        }

        return null;
    }

    /// <summary>
    /// The session ceiling an operator has configured.
    /// </summary>
    /// <param name="configuration">The plugin configuration.</param>
    /// <returns>The ceiling.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The configured value is outside the bounds. The message names the setting, because a configuration file edited by hand is where it arrives from and the operator needs to know which line to fix.</exception>
    public static int MaxActiveSessionsFrom(PluginConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        if (Refuse(configuration.GuestMaxActiveSessions) is { } refusal)
        {
            // The setting's name goes in the message rather than in the parameter
            // name, because the parameter here is the whole configuration and an
            // exception naming a parameter the method does not have is refused by
            // the analysers this project builds with.
            throw new ArgumentOutOfRangeException(
                nameof(configuration),
                configuration.GuestMaxActiveSessions,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"{nameof(PluginConfiguration.GuestMaxActiveSessions)}: {refusal}"));
        }

        return configuration.GuestMaxActiveSessions;
    }

    /// <summary>
    /// A policy for a guest, with every switch this plugin decides set explicitly.
    /// </summary>
    /// <param name="maxActiveSessions">How many sessions the guest may hold at once.</param>
    /// <returns>The policy.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The ceiling is outside the bounds.</exception>
    public static UserPolicy Create(int maxActiveSessions)
    {
        var policy = new UserPolicy();
        Apply(policy, maxActiveSessions);
        return policy;
    }

    /// <summary>
    /// Sets every switch this plugin decides on an existing policy.
    /// </summary>
    /// <param name="policy">The policy to set them on.</param>
    /// <param name="maxActiveSessions">How many sessions the guest may hold at once.</param>
    /// <exception cref="ArgumentOutOfRangeException">The ceiling is outside the bounds.</exception>
    /// <remarks>
    /// <para>
    /// Every switch is written rather than only the ones whose default is wrong.
    /// A default is the server's decision and it can move between server lines,
    /// which would silently widen a guest on an upgrade nobody connected to this
    /// plugin.
    /// </para>
    /// <para>
    /// The session ceiling is the exception to writing the value outright, and it
    /// is an exception in the narrow direction. An account that already carries a
    /// lower ceiling keeps it, because this plugin narrows an account and never
    /// widens one, and an operator-prepared account is one somebody else has
    /// already decided a number on. An account carrying the server's own zero is
    /// carrying no ceiling rather than the lowest one, so it takes the configured
    /// value; without that reading, "the lower of the two" would leave every fresh
    /// account unlimited.
    /// </para>
    /// </remarks>
    public static void Apply(UserPolicy policy, int maxActiveSessions)
    {
        ArgumentNullException.ThrowIfNull(policy);

        if (Refuse(maxActiveSessions) is { } refusal)
        {
            throw new ArgumentOutOfRangeException(nameof(maxActiveSessions), maxActiveSessions, refusal);
        }

        policy.MaxActiveSessions = policy.MaxActiveSessions < MinimumMaxActiveSessions
            ? maxActiveSessions
            : Math.Min(policy.MaxActiveSessions, maxActiveSessions);

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
