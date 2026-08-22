namespace Jellyfin.Plugin.ShareLinks;

/// <summary>
/// Why a share was not resolved (#48).
/// </summary>
/// <remarks>
/// <para>
/// This is for the server's own use and never for the caller. A guest who is told
/// which of these happened can tell a token that names no share from a token that
/// names one they are not invited to, and telling those two apart is how a person
/// with a list of guesses learns which guesses were close. Every one of these is
/// the same answer on the wire, which is #26, and the reason survives only where
/// the operator can see it.
/// </para>
/// <para>
/// The order they are declared in is the order they are decided in, and
/// <see cref="ShareResolution"/> is where that order is argued.
/// </para>
/// </remarks>
public enum ShareRefusal
{
    /// <summary>
    /// Nothing was refused. The share resolved.
    /// </summary>
    None = 0,

    /// <summary>
    /// The plugin is not active, so it answers for nothing.
    /// </summary>
    PluginNotActive = 1,

    /// <summary>
    /// The request carried no token.
    /// </summary>
    NoTokenPresented = 2,

    /// <summary>
    /// The install's key could not be read, so no stored hash can be checked (#28).
    /// </summary>
    /// <remarks>
    /// It is a refusal rather than a fresh key. Replacing a key that could not be
    /// read would stop every live share on the server, which is safe and silent,
    /// and the silence is the part that is not acceptable.
    /// </remarks>
    KeyUnavailable = 8,

    /// <summary>
    /// No record in the store answers for the presented token.
    /// </summary>
    NoSuchShare = 3,

    /// <summary>
    /// The share was revoked.
    /// </summary>
    Revoked = 4,

    /// <summary>
    /// The share has reached or passed the instant it stops working at.
    /// </summary>
    Expired = 5,

    /// <summary>
    /// The caller is not somebody the server has identified.
    /// </summary>
    CallerNotSignedIn = 6,

    /// <summary>
    /// The caller is signed in and is not one of the accounts the share names.
    /// </summary>
    CallerNotInvited = 7,

    /// <summary>
    /// The server no longer holds the item the record names (#39).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Last of the reasons, and last on purpose. It is the only one that asks the
    /// server a question, so it is asked once everything the record and the caller
    /// can settle has been settled, and a caller who was never entitled to the
    /// share does not make the server look anything up.
    /// </para>
    /// <para>
    /// It says the item is gone and never why, and it is not a permission answer.
    /// <c>docs/gone.md</c> is where that is argued: an item removed by a scan and
    /// an item this caller may not see are different questions, and one reason
    /// carrying both would make a permissions problem read as a deleted film.
    /// </para>
    /// </remarks>
    ItemGone = 9,
}
