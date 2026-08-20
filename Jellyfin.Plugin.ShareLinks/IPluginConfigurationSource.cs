using Jellyfin.Plugin.ShareLinks.Configuration;

namespace Jellyfin.Plugin.ShareLinks;

/// <summary>
/// Where a route reads the operator's settings from (#67).
/// </summary>
/// <remarks>
/// <para>
/// A seam for the same reason the clock is one. The settings live on
/// <see cref="Plugin.Instance"/>, which is one static for the whole process, so a
/// route reading it directly cannot be handed a configuration by a test: what it
/// reads is whatever the last thing to build a plugin left behind. That is not a
/// hypothetical. It was measured while the create route was being written: the
/// route's tests passed inside the whole suite and failed on their own, because
/// another test class had been leaving a configured plugin behind for them.
/// </para>
/// <para>
/// It is read per request rather than captured once, because an operator saving
/// the configuration page changes it while the server is running, and a ceiling a
/// route learned at start-up is a ceiling an operator cannot lower.
/// </para>
/// </remarks>
public interface IPluginConfigurationSource
{
    /// <summary>
    /// The settings as they stand now.
    /// </summary>
    /// <returns>The configuration, or <c>null</c> where the plugin has not been created and the server has therefore said nothing.</returns>
    /// <remarks>
    /// Null rather than a default, because a default is a set of numbers this
    /// plugin chose and answering with one would have a route act on settings the
    /// operator never saw. There is nothing a caller can usefully do with it
    /// except refuse.
    /// </remarks>
    PluginConfiguration? Current();
}
