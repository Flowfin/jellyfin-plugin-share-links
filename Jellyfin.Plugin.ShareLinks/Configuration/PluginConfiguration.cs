using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.ShareLinks.Configuration;

/// <summary>
/// Plugin configuration.
/// </summary>
/// <remarks>
/// Deliberately empty. The plugin has no settings yet, and the milestone that
/// defines the first one adds it here together with the control that edits it and
/// the validation that bounds it. A setting that means nothing is a setting
/// somebody eventually wires to something.
/// </remarks>
public class PluginConfiguration : BasePluginConfiguration
{
}
