using System;
using System.Buffers.Text;
using System.Security.Cryptography;

namespace Jellyfin.Plugin.ShareLinks;

// The marker the invariant lint reads. It sits outside the documentation comment
// because the compiler wants that comment against the declaration it describes.
//
// draws token bytes: this file is the one routine (#120)

/// <summary>
/// Mints the token a share link carries (#42).
/// </summary>
/// <remarks>
/// <para>
/// One routine draws token material, and it draws it from the operating system's
/// cryptographic generator. A token that can be predicted from another token, or
/// from the time it was minted, is the share handed to whoever guessed it, and no
/// expiry or revocation elsewhere in the plugin repairs that.
/// </para>
/// <para>
/// The length is not a round number picked here. It is 256 bits, the entropy
/// recorded against #26: guessing a live share is then a search of a space no
/// number of attempts against a server reduces, whatever the number of shares in
/// the store, so rate limiting is a defence against noise rather than the thing
/// standing between an attacker and a share. The same 256 bits is what the keyed
/// hash in #43 hashes, so the token is not the weaker half of that pair.
/// </para>
/// <para>
/// The encoding is base64url, RFC 4648 section 5, with the padding dropped.
/// Every character is unreserved in a URI, so the token needs no escaping
/// wherever in the link it ends up, and a chat client or a mail scanner that
/// rewrites a link cannot corrupt it by percent-encoding a character. Padding is
/// dropped because <c>=</c> is not unreserved and the length is fixed, so nothing
/// reads the padding to know where the token ends.
/// </para>
/// <para>
/// That this routine is the only caller of the cryptographic generator is
/// refused by <c>token-bytes-come-from-one-routine</c> in the greppable
/// invariant lint (#120), and the marker above this comment is how the file
/// declares itself that routine. A second file drawing bytes is refused whether
/// it carries the marker or not, so the exemption cannot be taken by the change
/// that needs exempting.
/// </para>
/// <para>
/// What the check does not reach is the text it cannot see: a call made through
/// reflection, a helper inside a compiled dependency, or an alias the source
/// gives the type. It reads the tree, like every invariant beside it.
/// </para>
/// </remarks>
public static class ShareTokens
{
    /// <summary>
    /// The number of random bytes behind one token.
    /// </summary>
    public const int EntropyBytes = 32;

    /// <summary>
    /// The number of characters in the encoded form of a token.
    /// </summary>
    public const int EncodedLength = 43;

    /// <summary>
    /// The alphabet the encoded form is drawn from.
    /// </summary>
    public const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_";

    /// <summary>
    /// Mints one token.
    /// </summary>
    /// <returns>The encoded token, <see cref="EncodedLength"/> characters drawn from <see cref="Alphabet"/>.</returns>
    public static string Mint()
    {
        Span<byte> bytes = stackalloc byte[EntropyBytes];
        RandomNumberGenerator.Fill(bytes);
        return Base64Url.EncodeToString(bytes);
    }
}
