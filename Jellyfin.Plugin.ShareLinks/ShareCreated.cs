using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.ShareLinks;

/// <summary>
/// What the create route answers with, once and never again (#67).
/// </summary>
/// <remarks>
/// <para>
/// Two of the four members here cannot be asked for a second time, and the type
/// exists to say so in the shape rather than in a sentence somebody may not read.
/// The store holds a keyed hash of the token, so this plugin cannot produce the
/// link again even when an administrator asks; the credential is minted, handed
/// to the server and not written down anywhere by this plugin.
/// </para>
/// <para>
/// <see cref="ShareSummary"/> carries neither, which is asserted in both
/// directions in the suite, so the listing route cannot become a second way to
/// read what this answer holds.
/// </para>
/// <para>
/// An operator who loses this answer has not lost the share. They revoke it and
/// make another one, which is <c>docs/expiry.md</c>'s reason for having no
/// extension either: issuing a link and reissuing one are the same act and should
/// look like it.
/// </para>
/// </remarks>
public sealed class ShareCreated
{
    /// <summary>
    /// Gets the share as the listing route would show it.
    /// </summary>
    /// <remarks>
    /// The same type the listing answers with rather than a second shape of the
    /// same facts, so a page that shows a row after a create and a row after a
    /// refresh is showing one thing.
    /// </remarks>
    public required ShareSummary Share { get; init; }

    /// <summary>
    /// Gets the link to send the guests.
    /// </summary>
    /// <remarks>
    /// Absolute, and built from <c>PublicBaseUrl</c> where an operator has set
    /// one. Where they have not, it is built from what the request claimed, which
    /// <c>ShareLinkBuilder</c> is where the cost of is written down.
    /// </remarks>
    public required Uri Link { get; init; }

    /// <summary>
    /// Gets the accounts this plugin made for the share, with the credential each one signs in with.
    /// </summary>
    /// <remarks>
    /// In the order the operator named them, so a person reading the answer can
    /// tell which credential belongs to which guest. The credential is
    /// <see cref="ShareTokens.EncodedLength"/> characters, which is unpleasant to
    /// type on a television remote and is the honest cost of not opening a second
    /// source of secret material in this plugin.
    /// </remarks>
    public required IReadOnlyList<GuestCredential> Guests { get; init; }
}
