using System;

namespace Jellyfin.Plugin.ShareLinks;

/// <summary>
/// What one resolution decision came to (#48).
/// </summary>
/// <remarks>
/// <para>
/// A share or a reason, never both and never neither. The two are held in one
/// object rather than returned as a record and a separate flag, because two
/// values a caller can read independently are two values a caller can read in the
/// wrong order, and the wrong order here is serving an item off a record that
/// came back beside a refusal.
/// </para>
/// <para>
/// Nothing here constructs one of these. Every instance in this plugin is made in
/// <see cref="ShareResolution"/>, and the invariant lint refuses a second file
/// that makes one, which is the check #48 asks for.
/// </para>
/// </remarks>
public sealed class ShareResolutionResult
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ShareResolutionResult"/> class.
    /// </summary>
    /// <param name="share">The share that resolved, or <c>null</c> when one did not.</param>
    /// <param name="refusal">Why nothing resolved, or <see cref="ShareRefusal.None"/> when something did.</param>
    /// <exception cref="ArgumentException">A share was given with a refusal, or neither was given. Both are states the caller cannot act on, and a constructor that admits them is a constructor that puts the contradiction one call further away from where it was made.</exception>
    public ShareResolutionResult(ShareRecord? share, ShareRefusal refusal)
    {
        if (share is not null && refusal != ShareRefusal.None)
        {
            throw new ArgumentException("A resolution carries a share or a refusal, not both.", nameof(refusal));
        }

        if (share is null && refusal == ShareRefusal.None)
        {
            throw new ArgumentException("A resolution that refuses nothing has to carry the share it resolved.", nameof(share));
        }

        Share = share;
        Refusal = refusal;
    }

    /// <summary>
    /// Gets the share the token named and the caller may use, or <c>null</c> when nothing resolved.
    /// </summary>
    public ShareRecord? Share { get; }

    /// <summary>
    /// Gets the reason nothing resolved, which is never shown to the caller.
    /// </summary>
    /// <remarks>
    /// <see cref="ShareRefusal"/> says why this is the server's to read and not
    /// the guest's to be told.
    /// </remarks>
    public ShareRefusal Refusal { get; }

    /// <summary>
    /// Gets a value indicating whether a share resolved.
    /// </summary>
    public bool IsResolved => Share is not null;
}
