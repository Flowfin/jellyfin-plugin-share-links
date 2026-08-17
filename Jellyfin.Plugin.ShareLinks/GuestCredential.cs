using System;

namespace Jellyfin.Plugin.ShareLinks;

/// <summary>
/// One account this plugin made for a share, with what it signs in with (#67).
/// </summary>
/// <remarks>
/// <para>
/// It appears in the answer to a create and nowhere else. Nothing in this plugin
/// writes a credential down, so this type has no place to be read back from and
/// no route that produces one.
/// </para>
/// <para>
/// The account identifier is here as well as the name, because the name is what
/// the operator recognises in the server's user list and the identifier is what
/// the record holds. An operator matching a row in a listing to a person needs
/// both, and having them in one place is what stops a page pairing them by
/// position.
/// </para>
/// </remarks>
public sealed class GuestCredential
{
    /// <summary>
    /// Gets the account's identifier on this server.
    /// </summary>
    public required Guid UserId { get; init; }

    /// <summary>
    /// Gets the name the guest is known by, exactly as the operator asked for it.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Gets the credential the guest signs in with, shown this once.
    /// </summary>
    /// <remarks>
    /// Drawn by <see cref="ShareTokens.Mint"/> rather than by a second routine of
    /// this plugin's own, which is what the <c>token-bytes-come-from-one-routine</c>
    /// invariant refuses a second of. It is handed to the server and forgotten
    /// here.
    /// </remarks>
    public required string Credential { get; init; }
}
