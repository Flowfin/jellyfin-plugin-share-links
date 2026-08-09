using System;

namespace Jellyfin.Plugin.ShareLinks;

/// <summary>
/// The install's key could not be read, so nothing can be resolved (#28).
/// </summary>
/// <remarks>
/// <para>
/// This exists so that an unreadable key is a refusal with a reason rather than
/// whatever the filesystem happened to throw. What it must never become is a
/// signal to generate a fresh key: a plugin that quietly replaces a key it could
/// not read has invalidated every live share on the server and said nothing.
/// </para>
/// <para>
/// The message names the path and what went wrong, because the operator is the
/// only person who can fix either, and it is never shown to a guest.
/// </para>
/// </remarks>
public class ShareKeyUnavailableException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ShareKeyUnavailableException"/> class.
    /// </summary>
    public ShareKeyUnavailableException()
        : base("The share key could not be read.")
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ShareKeyUnavailableException"/> class.
    /// </summary>
    /// <param name="message">What went wrong.</param>
    public ShareKeyUnavailableException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ShareKeyUnavailableException"/> class.
    /// </summary>
    /// <param name="message">What went wrong.</param>
    /// <param name="innerException">What the filesystem said.</param>
    public ShareKeyUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ShareKeyUnavailableException"/> class.
    /// </summary>
    /// <param name="path">The file the key was expected in.</param>
    /// <param name="detail">What went wrong, in the words an operator can act on.</param>
    /// <param name="innerException">What the filesystem said, or <c>null</c>.</param>
    public ShareKeyUnavailableException(string path, string detail, Exception? innerException)
        : base($"The share key at {path} could not be read: {detail}. No share resolves until this is fixed, and nothing here replaces the key, because a fresh key would silently stop every share that already exists.", innerException)
    {
        Path = path;
    }

    /// <summary>
    /// Gets the file the key was expected in.
    /// </summary>
    public string? Path { get; }
}
