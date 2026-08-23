using System;

namespace Jellyfin.Plugin.ShareLinks.Fuzz;

/// <summary>
/// Thrown when an input broke the property the fuzz target holds (#19).
/// </summary>
/// <remarks>
/// Its own type rather than a general one. libFuzzer records any escaping
/// exception as a crash, so what the type buys is the reader of the reproducer
/// knowing at once whether the target found a defect in the plugin or fell over
/// on something in the harness.
/// </remarks>
public sealed class FuzzedOracleBrokenException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="FuzzedOracleBrokenException"/> class.
    /// </summary>
    public FuzzedOracleBrokenException()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="FuzzedOracleBrokenException"/> class.
    /// </summary>
    /// <param name="message">What the input did.</param>
    public FuzzedOracleBrokenException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="FuzzedOracleBrokenException"/> class.
    /// </summary>
    /// <param name="message">What the input did.</param>
    /// <param name="innerException">What was being handled when it did.</param>
    public FuzzedOracleBrokenException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
