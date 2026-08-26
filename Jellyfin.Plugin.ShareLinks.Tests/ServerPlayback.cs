using System;
using System.Collections.Generic;
using System.Threading;
using Jellyfin.Data;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Database.Implementations.Enums;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Dto;
using Moq;

namespace Jellyfin.Plugin.ShareLinks.Tests;

/// <summary>
/// The two server answers the cap condition stands on, faked (#284).
/// </summary>
/// <remarks>
/// <para>
/// One helper rather than one per fixture, for the same reason
/// <see cref="ServerConfigurations"/> is one: what a version the server reports
/// no bitrate for looks like, and what an account that may not transcode looks
/// like, are facts about the server's own types, and a fixture spelling either
/// its own way would be a second answer to a question this plugin reads in one
/// place.
/// </para>
/// <para>
/// The account is built with the server's own default permissions before any of
/// them is changed. A <see cref="User"/> with no permission rows at all is not a
/// state a server produces, so a fixture that skipped the defaults would be
/// driving <see cref="PlayableVersions.MayTranscode"/> against an object no
/// running server hands it.
/// </para>
/// </remarks>
internal static class ServerPlayback
{
    /// <summary>
    /// A source the server reports a bitrate for.
    /// </summary>
    /// <param name="bitsPerSecond">What the server says the version is served at.</param>
    /// <param name="supportsTranscoding">Whether the server says it can be transcoded.</param>
    /// <returns>The source, as the server's own type.</returns>
    public static MediaSourceInfo AVersionAt(int bitsPerSecond, bool supportsTranscoding)
        => new MediaSourceInfo { Bitrate = bitsPerSecond, SupportsTranscoding = supportsTranscoding };

    /// <summary>
    /// A source the server reports no bitrate for at all.
    /// </summary>
    /// <param name="supportsTranscoding">Whether the server says it can be transcoded.</param>
    /// <returns>The source, with its bitrate absent rather than zero.</returns>
    public static MediaSourceInfo AVersionWithNoReportedBitrate(bool supportsTranscoding)
        => new MediaSourceInfo { Bitrate = null, SupportsTranscoding = supportsTranscoding };

    /// <summary>
    /// A media source manager answering with these sources for every item.
    /// </summary>
    /// <param name="sources">What the server reports, in the order it reports them.</param>
    /// <returns>The manager.</returns>
    public static IMediaSourceManager Reporting(params MediaSourceInfo[] sources)
    {
        var manager = new Mock<IMediaSourceManager>();
        manager.Setup(m => m.GetPlaybackMediaSources(
                It.IsAny<BaseItem>(),
                It.IsAny<User>(),
                It.IsAny<bool>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<MediaSourceInfo>)sources);

        return manager.Object;
    }

    /// <summary>
    /// A media source manager nothing ever asks anything of.
    /// </summary>
    /// <returns>A strict manager, so a call this fixture did not expect fails the test rather than returning a default.</returns>
    /// <remarks>
    /// Strict on purpose. Most fixtures in this repository are about a share with
    /// no ceiling, where the whole point is that the server is not asked what the
    /// item can be played at, and a loose double would answer that unasked
    /// question with an empty list and prove nothing.
    /// </remarks>
    public static IMediaSourceManager AskedNothing() => new Mock<IMediaSourceManager>(MockBehavior.Strict).Object;

    /// <summary>
    /// An account the server knows, with its own ceiling and its own transcode permission.
    /// </summary>
    /// <param name="account">The account the manager answers for. Every other identifier comes back as nothing.</param>
    /// <param name="mayTranscode">Whether the account is permitted to transcode video.</param>
    /// <param name="remoteClientBitrateLimit">The account's own remote client limit, where zero is what an untouched account carries.</param>
    /// <returns>The account manager.</returns>
    public static IUserManager AccountsHolding(Guid account, bool mayTranscode = true, int remoteClientBitrateLimit = 0)
    {
        var accounts = new Mock<IUserManager>();
        accounts.Setup(manager => manager.GetUserById(It.IsAny<Guid>()))
            .Returns((Guid id) => id == account ? AnAccount(id, mayTranscode, remoteClientBitrateLimit) : null);

        return accounts.Object;
    }

    /// <summary>
    /// A server that knows no accounts at all.
    /// </summary>
    /// <returns>The account manager.</returns>
    public static IUserManager NoAccounts() => AccountsHolding(Guid.Empty);

    private static User AnAccount(Guid id, bool mayTranscode, int remoteClientBitrateLimit)
    {
        var account = new User("a guest", "provider", "reset")
        {
            Id = id,
            RemoteClientBitrateLimit = remoteClientBitrateLimit,
        };

        account.AddDefaultPermissions();
        account.SetPermission(PermissionKind.EnableVideoPlaybackTranscoding, mayTranscode);

        return account;
    }
}
