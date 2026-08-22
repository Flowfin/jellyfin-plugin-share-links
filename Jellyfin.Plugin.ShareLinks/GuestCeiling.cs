using System;

namespace Jellyfin.Plugin.ShareLinks;

/// <summary>
/// The ceiling in force for one invited account of one share (#64).
/// </summary>
/// <param name="UserId">The invited account this answer is about.</param>
/// <param name="Reach">What the request-path filter would decide for that account on this share's item.</param>
/// <param name="Cap">The ceiling that would be applied, and which of the three produced it.</param>
/// <remarks>
/// <para>
/// One answer per invited account rather than one per share, because the ceiling
/// is a per-account question and a record names a list. Two guests on one share,
/// one of whom carries a remote client limit of their own, have two different
/// answers and a single number in a column is wrong for at least one of them
/// without saying which.
/// </para>
/// <para>
/// <paramref name="Reach"/> is carried beside the number because "no ceiling is
/// set" and "this plugin applies no ceiling to this account" are opposite
/// statements that both come out as an absent number. An invited account this
/// plugin did not create is
/// <see cref="GuestVerdict.NotAGuestOfThisPlugin"/> and is not capped by this
/// plugin at all, which is <c>docs/guest-confinement.md</c>'s rule and not a
/// property of the share's own ceiling.
/// </para>
/// <para>
/// This reaches the administrator surface and never a guest. A refusal reason
/// handed to the person holding a link is what #26 refuses, and the route this
/// travels on is behind the server's elevation policy.
/// </para>
/// </remarks>
public readonly record struct GuestCeiling(Guid UserId, GuestVerdict Reach, AppliedBitrateCap Cap);
