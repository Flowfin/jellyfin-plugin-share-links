using System;
using System.Globalization;

namespace Jellyfin.Plugin.ShareLinks;

/// <summary>
/// Thrown when the share store cannot be written (#78).
/// </summary>
/// <remarks>
/// <para>
/// The other half of <see cref="ShareStoreUnreadableException"/>. A directory
/// whose permissions changed, a path where something else now sits, a volume
/// that filled up: each of those arrives as a framework exception about a file
/// handle, and a caller that meets one has no way to tell it from a bug in this
/// plugin. Every one of them means the same thing to a caller, which is that the
/// records are unchanged and the reason is on the disk rather than in the
/// request.
/// </para>
/// <para>
/// The records are unchanged, and that is a property of how the write is made
/// rather than a hope. The new list goes to a temporary file which is renamed
/// over the store at the end, so a write that fails is a rename that never
/// happened, and the store still holds what it held.
/// </para>
/// </remarks>
public sealed class ShareStoreUnwritableException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ShareStoreUnwritableException"/> class.
    /// </summary>
    /// <param name="path">The store that could not be written.</param>
    /// <param name="reason">What was wrong.</param>
    /// <param name="innerException">The failure underneath this one.</param>
    public ShareStoreUnwritableException(string path, string reason, Exception innerException)
        : base(Describe(path, reason), innerException)
    {
        Path = path;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ShareStoreUnwritableException"/> class.
    /// </summary>
    /// <param name="path">The store that could not be written.</param>
    /// <param name="reason">What was wrong.</param>
    public ShareStoreUnwritableException(string path, string reason)
        : base(Describe(path, reason))
    {
        Path = path;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ShareStoreUnwritableException"/> class.
    /// </summary>
    public ShareStoreUnwritableException()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ShareStoreUnwritableException"/> class.
    /// </summary>
    /// <param name="message">The message.</param>
    public ShareStoreUnwritableException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ShareStoreUnwritableException"/> class.
    /// </summary>
    /// <param name="message">The message.</param>
    /// <param name="innerException">The failure underneath this one.</param>
    public ShareStoreUnwritableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>
    /// Gets the store that could not be written, or null when this instance was built without one.
    /// </summary>
    public string? Path { get; }

    private static string Describe(string path, string reason)
        => string.Create(
            CultureInfo.InvariantCulture,
            $"The share store at {path} could not be written: {reason}. Nothing was changed, because the records are replaced by a rename that never happened.");
}
