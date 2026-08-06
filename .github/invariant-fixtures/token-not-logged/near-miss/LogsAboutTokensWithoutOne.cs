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
}
