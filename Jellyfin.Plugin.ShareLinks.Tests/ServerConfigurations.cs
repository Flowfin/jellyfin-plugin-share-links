using MediaBrowser.Controller.Configuration;
using MediaBrowser.Model.Configuration;
using Moq;

namespace Jellyfin.Plugin.ShareLinks.Tests;

/// <summary>
/// A server configuration for the routes that read a ceiling off it (#64).
/// </summary>
/// <remarks>
/// One helper rather than one per fixture. What "no ceiling" is spelled as on the
/// server is a fact about the server's own field, and a fixture that spelled it
/// its own way would be a second answer to the question
/// <see cref="EffectiveBitrate.FromServerValue"/> exists to answer once.
/// </remarks>
internal static class ServerConfigurations
{
    /// <summary>
    /// A server whose remote client limit is the value given.
    /// </summary>
    /// <param name="remoteClientBitrateLimit">The limit, in bits per second, where zero is the value an untouched server carries.</param>
    /// <returns>The configuration manager.</returns>
    public static IServerConfigurationManager Saying(int remoteClientBitrateLimit)
    {
        var configuration = new Mock<IServerConfigurationManager>();
        configuration.SetupGet(manager => manager.Configuration)
            .Returns(new ServerConfiguration { RemoteClientBitrateLimit = remoteClientBitrateLimit });

        return configuration.Object;
    }

    /// <summary>
    /// A server whose administrator has set no remote client limit, which is what an untouched one carries.
    /// </summary>
    /// <returns>The configuration manager.</returns>
    public static IServerConfigurationManager WithNoCeiling() => Saying(0);
}
