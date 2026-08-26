using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.ShareLinks;

/// <summary>
/// Whether a share's ceiling can be met for the item it names (#285).
/// </summary>
/// <remarks>
/// <para>
/// <see cref="EffectiveBitrate.Lowest"/> says what the ceiling is; this says
/// whether anything can be served under it. They are two questions and the second
/// one is the one #63 is about: a cap below everything the item can be played at
/// is a share that either refuses or quietly serves above its own cap, and the
/// second of those is the outcome #63 names as wrong.
/// </para>
/// <para>
/// No server type appears in the signature. The three inputs are a number, a list
/// of two-field values and a boolean, so every case below is drivable with no
/// request and no library, which is what <c>docs/testing.md</c> requires. Reading
/// the server's own sources into <see cref="PlayableVersion"/> is #284's, and so
/// is reading the account's transcode permission.
/// </para>
/// <para>
/// **Nothing calls this yet.** It is landed before its callers so that the first
/// one is written against a routine that already answers the awkward cases rather
/// than against one that grew them afterwards, which is the same order
/// <see cref="EffectiveBitrate"/> was landed in and for the same reason. #284 is
/// the guest's refusal and #286 is what the operator is told; both stand on this
/// and neither exists.
/// </para>
/// <para>
/// The transcode permission is an input rather than a constant, and that is not
/// an accident of shape. <see cref="GuestPolicy"/> turns transcoding on for every
/// account this plugin makes and names #63 as the reason, so the state the second
/// clause of #63's Done-when asks for is one this plugin does not produce: it
/// arises from a source that cannot be transcoded, or from an operator who
/// narrowed the account by hand afterwards. A caller that passed a literal here
/// would be testing that policy rather than this condition.
/// </para>
/// </remarks>
public static class BitrateCapReach
{
    /// <summary>
    /// Whether anything can be served under the ceiling, and how.
    /// </summary>
    /// <param name="ceiling">The ceiling in force in bits per second, or <c>null</c> where none applies.</param>
    /// <param name="versions">What the server says the item can be played at, one entry per version.</param>
    /// <param name="accountMayTranscode">Whether the invited account is permitted to transcode.</param>
    /// <returns>The answer, of which only <see cref="CapReach.NothingCanBeServed"/> is a refusal.</returns>
    /// <remarks>
    /// <para>
    /// A version whose bitrate the server does not report is NOT a version above
    /// the ceiling, and the whole shape of this routine follows from that. An
    /// unreported bitrate is an unknown, so a set of versions carrying one cannot
    /// be shown to be entirely above the ceiling, and the answer is
    /// <see cref="CapReach.NotKnown"/> rather than a refusal. Failing the other
    /// way would refuse working shares on the reading of a field this plugin does
    /// not own, which is the direction
    /// <see cref="EffectiveBitrate.FromServerValue"/> already refuses to fail in.
    /// </para>
    /// <para>
    /// A version exactly at the ceiling is within it. The ceiling is what a guest
    /// may be held to rather than a number they must stay under, which is the
    /// reading <see cref="GuestConfinementFilter"/> already applies where it lets
    /// a request naming exactly the ceiling through.
    /// </para>
    /// <para>
    /// An empty list is <see cref="CapReach.NotKnown"/> and not a refusal. An item
    /// the server offers no version of is a question about the item rather than
    /// about the cap, and <see cref="ShareRefusal.ItemGone"/> is where that one is
    /// already answered.
    /// </para>
    /// <para>
    /// Transcoding is only looked for among the versions above the ceiling,
    /// because a version at or below it has already answered the question and
    /// returns before the flag is read.
    /// </para>
    /// </remarks>
    public static CapReach Of(long? ceiling, IReadOnlyList<PlayableVersion> versions, bool accountMayTranscode)
    {
        ArgumentNullException.ThrowIfNull(versions);

        if (ceiling is not { } limit)
        {
            return CapReach.NoCeilingIsSet;
        }

        var anythingUnreported = false;
        var anythingTranscodable = false;
        for (var index = 0; index < versions.Count; index++)
        {
            var version = versions[index];
            if (version.BitsPerSecond is not { } bitrate)
            {
                anythingUnreported = true;
                continue;
            }

            if (bitrate <= limit)
            {
                return CapReach.AVersionIsWithinIt;
            }

            anythingTranscodable |= version.SupportsTranscoding;
        }

        // Before the count is looked at, because an empty list and a list of
        // versions the server reported nothing about are the same state: no
        // version has been shown to be above the ceiling.
        if (anythingUnreported || versions.Count == 0)
        {
            return CapReach.NotKnown;
        }

        return accountMayTranscode && anythingTranscodable
            ? CapReach.OnlyByTranscoding
            : CapReach.NothingCanBeServed;
    }
}
