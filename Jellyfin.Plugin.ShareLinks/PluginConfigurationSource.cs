using Jellyfin.Plugin.ShareLinks.Configuration;

namespace Jellyfin.Plugin.ShareLinks;

/// <summary>
/// The settings, read off this plugin on the server (#67).
/// </summary>
/// <remarks>
/// The whole of the implementation is the one line below, and that is the point
/// of it. This is the file that knows the settings live on a static, so every
/// other file takes them from its caller and can be handed a different set by a
/// test. <see cref="PluginServiceRegistrator"/> is where it is wired, beside the
/// store, the key file and the clock, for the same reason.
/// </remarks>
public sealed class PluginConfigurationSource : IPluginConfigurationSource
{
    /// <inheritdoc />
    public PluginConfiguration? Current() => Plugin.Instance?.Configuration;
}
