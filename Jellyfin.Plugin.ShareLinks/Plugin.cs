using System;
using System.Collections.Generic;
using System.Globalization;
using Jellyfin.Plugin.ShareLinks.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.ShareLinks;

/// <summary>
/// The main plugin.
/// </summary>
public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Plugin"/> class.
    /// </summary>
    /// <param name="applicationPaths">Instance of the <see cref="IApplicationPaths"/> interface.</param>
    /// <param name="xmlSerializer">Instance of the <see cref="IXmlSerializer"/> interface.</param>
    public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
        : base(applicationPaths, xmlSerializer)
    {
        Instance = this;
    }

    /// <inheritdoc />
    public override string Name => "Share Links";

    /// <inheritdoc />
    public override Guid Id => Guid.Parse("a3703f07-f83d-49a0-a09f-50b890a2baac");

    /// <summary>
    /// Gets the current plugin instance.
    /// </summary>
    public static Plugin? Instance { get; private set; }

    /// <summary>
    /// Refuses a configuration this plugin cannot work under, and saves one it
    /// can (#71).
    /// </summary>
    /// <param name="configuration">The configuration the server is saving.</param>
    /// <exception cref="ArgumentException">A setting is outside what it admits. The message names the setting, because the operator reading it has to find the line to change.</exception>
    /// <remarks>
    /// <para>
    /// This is the only moment a value is refused as it is written. Every other
    /// refusal in this plugin happens when the setting is next read, which for a
    /// ceiling is the next time somebody creates a share, and an operator who
    /// saves a page and hears nothing has been told the value was accepted.
    /// </para>
    /// <para>
    /// It reaches a value saved through the server and it does not reach a file
    /// edited by hand, which never passes through here. That half is unchanged and
    /// is what the routines that read each setting still refuse;
    /// <c>docs/configuration.md</c> is where the two moments are set out.
    /// </para>
    /// <para>
    /// A configuration of another type is passed through rather than refused. What
    /// to do with one is the base class's question, and answering it here would be
    /// this plugin deciding something about a shape it was not handed.
    /// </para>
    /// </remarks>
    public override void UpdateConfiguration(BasePluginConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        if (configuration is PluginConfiguration mine && ShareConfiguration.Refuse(mine) is { } refusal)
        {
            throw new ArgumentException(refusal, nameof(configuration));
        }

        base.UpdateConfiguration(configuration);
    }

    /// <inheritdoc />
    public IEnumerable<PluginPageInfo> GetPages()
    {
        return
        [
            new PluginPageInfo
            {
                Name = Name,
                EmbeddedResourcePath = string.Format(CultureInfo.InvariantCulture, "{0}.Configuration.configPage.html", GetType().Namespace)
            }
        ];
    }
}
