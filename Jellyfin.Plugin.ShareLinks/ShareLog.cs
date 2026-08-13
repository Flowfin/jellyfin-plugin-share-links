using System;
using System.Globalization;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.ShareLinks;

/// <summary>
/// Every line this plugin writes to the server's log (#27).
/// </summary>
/// <remarks>
/// <para>
/// One file, because the never list in <c>docs/logging.md</c> is a rule about
/// what may appear in a line and a rule like that is kept by having one place
/// where lines are made. A caller that wanted a fifth line has to come here and
/// meet the list on the way.
/// </para>
/// <para>
/// A share is named by the first <see cref="NameLength"/> characters of its
/// record identifier and never by its token, which is the credential, and never
/// in full, which the operator surface already shows. <c>docs/logging.md</c> is
/// where the length is argued and where the collision it costs is measured.
/// </para>
/// <para>
/// Nothing here takes an item title or an account, so no line can carry one. The
/// plugin has no way to read a title at all today, and the guard that survives
/// the day it has one is in <c>LoggingTests</c>: it compares the set of
/// placeholders every line emits against the set this policy allows, so a line
/// that grows a field reddens rather than shipping.
/// </para>
/// </remarks>
public static class ShareLog
{
    /// <summary>
    /// How many characters of a share's record identifier a log line carries.
    /// </summary>
    public const int NameLength = 8;

    /// <summary>
    /// The name one share has in every line this plugin writes.
    /// </summary>
    /// <param name="shareId">The record identifier of the share.</param>
    /// <returns>The first <see cref="NameLength"/> characters of the identifier.</returns>
    /// <remarks>
    /// Hexadecimal with no separators, so the value in a line is a prefix of the
    /// identifier as the operator surface writes it and a search for one finds
    /// the other.
    /// </remarks>
    public static string Name(Guid shareId)
        => shareId.ToString("N", CultureInfo.InvariantCulture)[..NameLength];

    /// <summary>
    /// A share was created.
    /// </summary>
    /// <param name="logger">Where the line goes.</param>
    /// <param name="record">The record that was written.</param>
    /// <remarks>
    /// The accounts are counted rather than named. Which person was invited to
    /// which item is what the operator surface holds and what
    /// <c>docs/personal-data.md</c> accounts for; a line repeating it makes a
    /// second copy with a different lifetime and a different reader.
    /// </remarks>
    public static void Created(ILogger logger, ShareRecord record)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(record);

        logger.LogInformation(
            "Share {Share} created for item {Item}, expiring {Expires}, {Invited} account(s) invited",
            Name(record.Id),
            record.ItemId,
            record.ExpiresAt,
            record.InvitedUserIds.Count);
    }

    /// <summary>
    /// A revocation was asked for.
    /// </summary>
    /// <param name="logger">Where the line goes.</param>
    /// <param name="shareId">The share the revocation named.</param>
    /// <param name="outcome">What the store did about it.</param>
    /// <remarks>
    /// Every ask is one line, including the ones that changed nothing.
    /// Revocation is idempotent (#46), so a second press is not an error and an
    /// operator pressing twice should be able to see that the server agreed with
    /// them rather than lost the first one.
    /// </remarks>
    public static void Revoked(ILogger logger, Guid shareId, ShareRevocation outcome)
    {
        ArgumentNullException.ThrowIfNull(logger);

        logger.LogInformation("Share {Share} revocation: {Outcome}", Name(shareId), outcome);
    }

    /// <summary>
    /// A share resolved for the caller that presented its token.
    /// </summary>
    /// <param name="logger">Where the line goes.</param>
    /// <param name="record">The share that resolved.</param>
    /// <remarks>
    /// The caller is not named, for the reason <see cref="Created"/> gives about
    /// the invited accounts. This line says a share was opened; who opened it is
    /// the server's own session record and not a second copy kept here.
    /// </remarks>
    public static void Resolved(ILogger logger, ShareRecord record)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(record);

        logger.LogInformation("Share {Share} resolved", Name(record.Id));
    }

    /// <summary>
    /// Nothing resolved, and this is the reason the server keeps.
    /// </summary>
    /// <param name="logger">Where the line goes.</param>
    /// <param name="refusal">Why nothing resolved.</param>
    /// <remarks>
    /// <para>
    /// No share is named, and that is a consequence of the decision rather than
    /// an omission. <see cref="ShareResolutionResult"/> carries a share or a
    /// refusal and never both, so on this path there is no record to take an
    /// identifier from even where the token matched one. <c>docs/logging.md</c>
    /// records what that costs an operator.
    /// </para>
    /// <para>
    /// The reason is the enumeration member rather than a sentence assembled
    /// from the request, so a line cannot carry back something a caller sent.
    /// The same value never reaches the caller, which is #26.
    /// </para>
    /// </remarks>
    public static void Refused(ILogger logger, ShareRefusal refusal)
    {
        ArgumentNullException.ThrowIfNull(logger);

        logger.LogInformation("A token did not resolve: {Reason}", refusal);
    }

    /// <summary>
    /// The store could not be read, so no decision was made at all.
    /// </summary>
    /// <param name="logger">Where the line goes.</param>
    /// <remarks>
    /// A warning rather than a refusal line. <c>docs/logging.md</c> puts the
    /// states an operator has to act on above information, and a store nobody
    /// can read stops every share on the server while looking, in the caller's
    /// answer, exactly like a token that named nothing. The path is not carried:
    /// the exception the store threw is where a path belongs, and this line's
    /// job is to be visible.
    /// </remarks>
    public static void StoreUnreadable(ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(logger);

        logger.LogWarning("The share store could not be read, so no share can resolve");
    }
}
