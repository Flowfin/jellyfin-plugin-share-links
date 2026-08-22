using System;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Library;

namespace Jellyfin.Plugin.ShareLinks;

/// <summary>
/// The two ceilings this plugin reads off the server rather than out of a record (#64).
/// </summary>
/// <remarks>
/// <para>
/// One place rather than one per caller. The request-path filter and the
/// administrator listing both need the same two numbers, read the same way, and
/// two copies of "zero means no ceiling" is the drift that makes an operator
/// surface disagree with the thing it is describing.
/// </para>
/// <para>
/// Both readings go through <see cref="EffectiveBitrate.FromServerValue"/>, so
/// what counts as an absent ceiling is decided once and is argued there.
/// </para>
/// </remarks>
public static class ServerCeilings
{
    /// <summary>
    /// The account's own remote client limit.
    /// </summary>
    /// <param name="accounts">The server's own accounts.</param>
    /// <param name="account">The account to read.</param>
    /// <returns>The ceiling in bits per second, or <c>null</c> where the account carries none.</returns>
    /// <remarks>
    /// Two ways of saying "no ceiling" reach this: an account the server does not
    /// know, and a field nobody has set. Both read as absent rather than as a
    /// ceiling of nothing, because a ceiling of nothing is a guest who may watch
    /// at no bitrate at all.
    /// </remarks>
    public static long? OfAccount(IUserManager accounts, Guid account)
    {
        ArgumentNullException.ThrowIfNull(accounts);

        return accounts.GetUserById(account)?.RemoteClientBitrateLimit is { } limit
            ? EffectiveBitrate.FromServerValue(limit)
            : null;
    }

    /// <summary>
    /// The server configuration's remote client limit, which applies to everybody.
    /// </summary>
    /// <param name="configuration">The server's own configuration.</param>
    /// <returns>The ceiling in bits per second, or <c>null</c> where the server carries none.</returns>
    public static long? OfServer(IServerConfigurationManager configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        return EffectiveBitrate.FromServerValue(configuration.Configuration.RemoteClientBitrateLimit);
    }
}
