using System;

namespace Jellyfin.Plugin.ShareLinks;

/// <summary>
/// Which of the three ceilings produced the effective one (#64).
/// </summary>
/// <remarks>
/// A set rather than one answer, because two ceilings can sit at the same value
/// and both of them apply. Naming one of them would be the bug this issue is
/// about wearing a different face: an operator lowers the one that was not
/// reported and nothing appears to change.
/// </remarks>
[Flags]
public enum BitrateCeiling
{
    /// <summary>
    /// No ceiling was set anywhere, so nothing was applied.
    /// </summary>
    None = 0,

    /// <summary>
    /// The ceiling the share itself carries.
    /// </summary>
    Share = 1,

    /// <summary>
    /// The invited account's own remote client limit.
    /// </summary>
    Account = 2,

    /// <summary>
    /// The server configuration's remote client limit, which applies to every
    /// account on the server.
    /// </summary>
    ServerRemoteClientLimit = 4
}
