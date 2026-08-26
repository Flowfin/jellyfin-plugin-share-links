using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Database.Implementations.Enums;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;

namespace Jellyfin.Plugin.ShareLinks;

/// <summary>
/// The two questions this plugin asks the server about playing an item (#284).
/// </summary>
/// <remarks>
/// <para>
/// <see cref="BitrateCapReach"/> takes a number, a list of two-field values and a
/// boolean, and takes no server type at all, so that every case of the condition
/// is drivable with no server. This is the adapter that stands between that
/// routine and a running one, and it is deliberately the only file in this plugin
/// that names <see cref="IMediaSourceManager"/> or a permission.
/// </para>
/// <para>
/// One place rather than one per caller, for the same reason
/// <see cref="ServerCeilings"/> is one place: the guest surface and the operator
/// surface ask the same two questions about the same pair of an account and an
/// item, and two readings of "what can this be played at" is how the number a
/// guest is refused against comes to differ from the one an operator is shown.
/// </para>
/// <para>
/// <b>Neither question is asked on the request path.</b> #284 puts the lookup on
/// the surface that opens a share, which happens once, and keeps it out of
/// <see cref="GuestConfinementFilter"/>, which stands in front of every stream
/// request a guest makes. A library call per segment is a cost with no ceiling on
/// it.
/// </para>
/// </remarks>
public static class PlayableVersions
{
    /// <summary>
    /// What the server says the item can be played at, for this account.
    /// </summary>
    /// <param name="sources">The server's own answer about an item's media sources.</param>
    /// <param name="item">The item the share names.</param>
    /// <param name="account">The invited account the answer is for.</param>
    /// <param name="cancellationToken">Cancels the lookup.</param>
    /// <returns>One entry per version the server reports, in the order it reports them.</returns>
    /// <remarks>
    /// <para>
    /// The probe is not asked for. A probe reads the file to find out what it
    /// holds, which is a filesystem cost this plugin has no business spending on
    /// somebody opening a link; what is wanted here is what the server already
    /// knows. A version the server knows no bitrate for therefore stays a version
    /// with no bitrate, which is a state
    /// <see cref="BitrateCapReach"/> answers <see cref="CapReach.NotKnown"/> to
    /// rather than refusing on.
    /// </para>
    /// <para>
    /// Path substitution is not asked for either. It rewrites where a file is
    /// read from and says nothing about what the item can be served at, so
    /// turning it on here would be asking a question this routine does not use
    /// the answer to.
    /// </para>
    /// <para>
    /// A server that answers with nothing at all comes back as an empty list
    /// rather than as an exception, and an empty list is
    /// <see cref="CapReach.NotKnown"/>. An item the server offers no version of
    /// is a question about the item rather than about the cap, and
    /// <see cref="ShareRefusal.ItemGone"/> is where that one is already answered.
    /// </para>
    /// </remarks>
    public static async Task<IReadOnlyList<PlayableVersion>> OfAsync(
        IMediaSourceManager sources,
        BaseItem item,
        User account,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(account);

        var reported = await sources.GetPlaybackMediaSources(
            item,
            account,
            allowMediaProbe: false,
            enablePathSubstitution: false,
            cancellationToken).ConfigureAwait(false);

        if (reported is null)
        {
            return Array.Empty<PlayableVersion>();
        }

        var versions = new List<PlayableVersion>(reported.Count);
        for (var index = 0; index < reported.Count; index++)
        {
            var source = reported[index];
            versions.Add(PlayableVersion.From(source.Bitrate, source.SupportsTranscoding));
        }

        return versions;
    }

    /// <summary>
    /// Whether this account is permitted to transcode video.
    /// </summary>
    /// <param name="account">The invited account.</param>
    /// <returns><c>true</c> where the server's own policy for the account permits a video transcode.</returns>
    /// <remarks>
    /// <para>
    /// Video and not audio, because the condition this feeds is about a version
    /// being brought under a bitrate ceiling, and that is the video transcode.
    /// <see cref="GuestPolicy"/> sets both together for every account this plugin
    /// makes, so the two only ever differ on an account an operator narrowed by
    /// hand, which is the one route into the state
    /// <see cref="CapReach.NothingCanBeServed"/>'s transcoding arm is about.
    /// </para>
    /// <para>
    /// The permission is read rather than assumed. <see cref="GuestPolicy"/>
    /// turns transcoding on and names #63 as the reason, so an account carrying
    /// it off is one somebody changed afterwards, and a caller that passed a
    /// literal here would be testing that policy rather than this account.
    /// </para>
    /// </remarks>
    public static bool MayTranscode(User account)
    {
        ArgumentNullException.ThrowIfNull(account);

        return account.HasPermission(PermissionKind.EnableVideoPlaybackTranscoding);
    }
}
