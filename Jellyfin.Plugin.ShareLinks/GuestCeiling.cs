using System;

namespace Jellyfin.Plugin.ShareLinks;

/// <summary>
/// The ceiling in force for one invited account of one share (#64).
/// </summary>
/// <param name="UserId">The invited account this answer is about.</param>
/// <param name="Reach">What the request-path filter would decide for that account on this share's item.</param>
/// <param name="Cap">The ceiling that would be applied, and which of the three produced it.</param>
/// <param name="CanBeMet">Whether anything can be served under that ceiling for this share's item, and how (#286).</param>
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
/// <paramref name="CanBeMet"/> is a third thing beside those two and not a
/// restatement of either. The number says what a guest would be held to; this
/// says whether the item can be served at all under it, which is #63's condition
/// and is the answer an operator needs before they find out from a guest's error
/// message. It is on this value rather than on a second one because whether a
/// ceiling can be met is a fact about the same pair of an account and an item the
/// ceiling itself is about.
/// </para>
/// <para>
/// <see cref="CapReach.NothingCanBeServed"/> is the one member that is a warning.
/// Every other one is an ordinary state, and two of them are absences rather than
/// good news: <see cref="CapReach.NoCeilingIsSet"/> is a share with nothing to
/// meet, and <see cref="CapReach.NotKnown"/> is a question the server did not
/// answer. A surface that showed the last two as a tick would be claiming
/// something nobody measured.
/// </para>
/// <para>
/// It is true at the instant the listing was read, in the same way the number
/// beside it is. What an item can be played at is read from the server then, and
/// a version added or removed afterwards moves the answer without anything here
/// knowing.
/// </para>
/// <para>
/// This reaches the administrator surface and never a guest. A refusal reason
/// handed to the person holding a link is what #26 refuses, and the route this
/// travels on is behind the server's elevation policy. What a guest is told when
/// the same condition stops them is #284's one sentence, which is a different
/// text on a different route.
/// </para>
/// </remarks>
public readonly record struct GuestCeiling(Guid UserId, GuestVerdict Reach, AppliedBitrateCap Cap, CapReach CanBeMet);
