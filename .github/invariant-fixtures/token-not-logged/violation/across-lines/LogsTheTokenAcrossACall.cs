// Fixture for the invariant token-not-logged. Violates it on purpose; compiled
// by nothing.
//
// The second reason this invariant refuses, and it is the shape the plugin's own
// logging already has. A call with more arguments than fit on a line is wrapped,
// and one of the arguments before the token is itself a call, so a reader
// following the text from the logging call meets a closing parenthesis that
// belongs to something else long before it meets the token.
//
// Both of those hide the token from a reading that stops at the end of the line
// or at the first closing parenthesis, and neither of them is unusual. This file
// is written the way `ShareLog` writes its own lines, with a token added.
namespace Jellyfin.Plugin.ShareLinks.InvariantFixtures.TokenNotLogged;

internal sealed class LogsTheTokenAcrossACall
{
    public void Created(ILogger logger, ShareRecord record, string shareToken)
    {
        logger.LogInformation(
            "Share {Share} created for item {Item}, {Invited} account(s) invited",
            Name(record.Id),
            record.ItemId,
            record.InvitedUserIds.Count,
            shareToken);
    }

    private static string Name(Guid id) => id.ToString("N");
}
