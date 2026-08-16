// Near miss for the invariant token-not-logged. Every line here mentions tokens
// and none of them puts one in a log record, so the invariant must stay quiet.
// An invariant that reddened this file would be turned off within a week.
namespace Jellyfin.Plugin.ShareLinks.InvariantFixtures.TokenNotLogged;

internal sealed class LogsAboutTokensWithoutOne
{
    public void Refuse(ILogger logger, Guid shareId)
    {
        // Prose naming the thing that happened, with no token in it.
        logger.LogWarning("A share token was presented and refused");

        // The share identifier names the same record and is not a credential.
        logger.LogInformation("Share {ShareId} was revoked", shareId);
    }

    public void Created(ILogger logger, ShareRecord record)
    {
        // The same wrapped shape the violation beside this file carries, with
        // the token left out of it. The reading that follows a logging call to
        // the end of its statement has to tell these two apart, and one that
        // refused every wrapped call would refuse the way this plugin already
        // writes its own lines.
        logger.LogInformation(
            "Share {Share} created for item {Item}, {Invited} account(s) invited",
            Name(record.Id),
            record.ItemId,
            record.InvitedUserIds.Count);
    }

    private static string Name(Guid id) => id.ToString("N");
}
