using System;
using System.Globalization;

namespace Jellyfin.Plugin.ShareLinks;

/// <summary>
/// Thrown when a create would take the store past one of its bounds (#29).
/// </summary>
/// <remarks>
/// A refusal rather than a return value nobody has to read. The failure this is
/// written against is a loop that creates shares faster than anybody notices, and
/// a caller that forgets to check a flag is exactly the caller that loop runs
/// through.
/// </remarks>
public sealed class ShareBoundExceededException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ShareBoundExceededException"/> class.
    /// </summary>
    /// <param name="bound">Which bound was met, in the words <see cref="ShareBounds.Refuse"/> produced, naming the setting.</param>
    public ShareBoundExceededException(string bound)
        : base(Describe(bound))
    {
        Bound = bound;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ShareBoundExceededException"/> class.
    /// </summary>
    public ShareBoundExceededException()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ShareBoundExceededException"/> class.
    /// </summary>
    /// <param name="message">The message.</param>
    /// <param name="innerException">The failure underneath this one.</param>
    public ShareBoundExceededException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>
    /// Gets which bound was met, or null when this instance was built without one.
    /// </summary>
    public string? Bound { get; }

    private static string Describe(string bound)
        => string.Create(
            CultureInfo.InvariantCulture,
            $"The share was not created: {bound}. Raise the setting, or revoke a share that is no longer needed.");
}
