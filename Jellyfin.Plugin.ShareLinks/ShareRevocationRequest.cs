namespace Jellyfin.Plugin.ShareLinks;

/// <summary>
/// What an operator may say when revoking a share (#46, #67).
/// </summary>
/// <remarks>
/// <para>
/// A body rather than a query parameter, and that is a decision about where the
/// text ends up rather than a preference about shape. A reason in a query string
/// is a reason in the server's access log, in a proxy's access log and in a
/// browser's history, and the reason an operator writes is free text about a
/// person.
/// </para>
/// <para>
/// Everything else a revocation needs is already known without being asked for.
/// The share is the path segment, the instant comes from the clock the plugin
/// was handed, and the revoker is whoever the server signed in.
/// </para>
/// </remarks>
public sealed class ShareRevocationRequest
{
    /// <summary>
    /// Gets or sets what the operator wants another operator to read later, or <c>null</c>.
    /// </summary>
    /// <remarks>
    /// Optional, because a revocation with no reason is still a revocation and a
    /// surface that demanded one would collect a full stop.
    /// </remarks>
    public string? Reason { get; set; }
}
