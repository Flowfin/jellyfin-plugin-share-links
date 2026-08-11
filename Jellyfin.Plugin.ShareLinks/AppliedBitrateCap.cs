namespace Jellyfin.Plugin.ShareLinks;

/// <summary>
/// The ceiling that is actually in force, and which of the three produced it.
/// </summary>
/// <param name="BitsPerSecond">The effective ceiling, or <c>null</c> when none of the three is set.</param>
/// <param name="Applied">Every ceiling sitting at that value. <see cref="BitrateCeiling.None"/> when there is no ceiling.</param>
/// <remarks>
/// A pair rather than a number. A caller that throws the second half away brings
/// back the bug in #64's first paragraph, and a test that compares only the
/// number passes an implementation that guessed the source.
/// </remarks>
public readonly record struct AppliedBitrateCap(long? BitsPerSecond, BitrateCeiling Applied);
