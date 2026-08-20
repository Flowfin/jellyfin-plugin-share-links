using System;
using System.IO;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.ShareLinks;

// The marker the invariant lint reads. It sits outside the documentation comment
// because the compiler wants that comment against the declaration it describes.
//
// supplies the machine clock: this file is the one routine (#68)

/// <summary>
/// Hands the plugin's routes the things they are not allowed to make for
/// themselves (#68).
/// </summary>
/// <remarks>
/// <para>
/// A route that opened its own store would decide where the store is, and a
/// route that read its own clock could not be stood on either side of an expiry
/// boundary by a test. Both come from here instead, which is the one file in this
/// plugin that knows what a running server looks like.
/// </para>
/// <para>
/// This is where the machine clock enters the tree, and it says so in a line
/// above that the lint counts. Every other file takes <see cref="TimeProvider"/>
/// from its caller, so a test hands it an instant and no test ever sleeps.
/// </para>
/// <para>
/// The two file names are decided here rather than in the types that read them.
/// <see cref="ShareStore"/> and <see cref="ShareKeyFile"/> each take a path,
/// which is what lets a test point them at a temporary directory; deciding where
/// a real install keeps them is a different question and it belongs to whoever
/// wires the plugin to a server. <c>docs/share-store.md</c> and
/// <c>docs/share-key.md</c> are where the folder and the key's name are argued.
/// </para>
/// <para>
/// The registrations are factories rather than instances, because the server
/// builds its container before it creates the plugin, so
/// <see cref="Plugin.Instance"/> is null while this method runs and is not null
/// when a request first asks for one of these.
/// </para>
/// </remarks>
public class PluginServiceRegistrator : IPluginServiceRegistrator
{
    /// <summary>
    /// The name of the file the share records are kept in, inside the plugin's
    /// data folder.
    /// </summary>
    public const string StoreFileName = "shares.json";

    /// <summary>
    /// The name of the file the keyed-hash key is kept in, inside the plugin's
    /// data folder. <c>docs/share-key.md</c> is where the name is fixed.
    /// </summary>
    public const string KeyFileName = "share-key";

    /// <inheritdoc />
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        ArgumentNullException.ThrowIfNull(serviceCollection);

        serviceCollection.AddSingleton<IShareStore>(_ => new ShareStore(InTheDataFolder(StoreFileName)));
        serviceCollection.AddSingleton(_ => new ShareKeyFile(InTheDataFolder(KeyFileName)));
        serviceCollection.AddSingleton(TimeProvider.System);
        serviceCollection.AddSingleton<IPluginConfigurationSource>(_ => new PluginConfigurationSource());
    }

    /// <summary>
    /// Composes the full path of one of this plugin's files.
    /// </summary>
    /// <param name="fileName">The file's name inside the data folder.</param>
    /// <returns>The full path.</returns>
    /// <exception cref="InvalidOperationException">The plugin has not been created yet, so the server has not said where its data folder is. Nothing is guessed in its place: a store written to a folder of this plugin's own choosing is a store an upgrade or an uninstall does not touch.</exception>
    public static string InTheDataFolder(string fileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

        // A ROOTED NAME WOULD LEAVE THE DATA FOLDER, and Path.Combine would let
        // it: given an absolute second argument it discards the first and
        // returns the second unchanged. So Path.Combine("/var/lib/.../share-links",
        // "/etc/passwd") is "/etc/passwd", silently, with no error anywhere.
        //
        // Every caller in this plugin passes a compile-time constant, so nothing
        // reaches this today. That is the problem rather than the reassurance:
        // this method is public and static, its safety rests on two call sites
        // somebody has to go and read, and a third caller added next year would
        // not be warned by anything. CodeQL reported it as cs/path-combine and
        // was right to - it cannot see the callers either.
        //
        // The check costs one line and moves the guarantee out of the call sites
        // and into the method that makes the promise.
        if (Path.IsPathRooted(fileName) || fileName.Contains("..", StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "A file inside the data folder is named by a plain file name. "
                + $"'{fileName}' is rooted or walks upwards, and combining it would leave the data folder.",
                nameof(fileName));
        }

        var plugin = Plugin.Instance
            ?? throw new InvalidOperationException("The plugin has not been created, so the server has not said where its data folder is.");

        return Path.Combine(plugin.DataFolderPath, fileName);
    }
}
