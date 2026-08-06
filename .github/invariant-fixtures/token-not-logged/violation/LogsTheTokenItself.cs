// Fixture for the invariant token-not-logged. It violates that invariant on
// purpose and it is compiled by nothing: no project in the solution globs this
// directory. It exists so the invariant can be shown to bite without a real
// source file having to carry the mistake.
namespace Jellyfin.Plugin.ShareLinks.InvariantFixtures.TokenNotLogged;

internal sealed class LogsTheTokenItself
{
    public void Resolve(ILogger logger, string shareToken)
    {
        // The token reaches the log as a value, so it reaches the log file, the
        // log shipper and whatever retains them, and it outlives the share.
        logger.LogInformation("Resolving share for {Token}", shareToken);
    }
}
