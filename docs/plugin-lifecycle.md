# Disabling, uninstalling, and what is left behind

This is the behaviour issue #38 asks to have defined. An operator who disables
this plugin expects sharing to stop. An operator who uninstalls it expects the
same and expects the data to go with it. Neither happens by itself, and the
interesting part is what a live share does in between.

Everything below about the server was read out of the packages this plugin
compiles against on the line it is packaged for. `Directory.Build.props` pins a
version per target framework rather than one version:

    grep 'JellyfinVersion Condition' Directory.Build.props
        <JellyfinVersion Condition="'$(TargetFramework)' == 'net9.0'">10.11.11</JellyfinVersion>
        <JellyfinVersion Condition="'$(TargetFramework)' == 'net10.0'">12.0.0-rc5</JellyfinVersion>

The readings below are from `10.11.11`, the `net9.0` arm, which is the line
`build.yaml` names as the one a package is built for.

## Disabling does not stop anything on its own

The server's own documentation says a disabled plugin keeps running until the
server is restarted:

    model=~/.nuget/packages/jellyfin.model/10.11.11/lib/net9.0/MediaBrowser.Model.xml
    grep -A8 'name="F:MediaBrowser.Model.Plugins.PluginStatus.Restart"' "$model"
    <member name="F:MediaBrowser.Model.Plugins.PluginStatus.Restart">
        <summary>
        This plugin requires a restart in order for it to load. This is a memory only status.
        The actual status of the plugin after reload is present in the manifest.
        eg. A disabled plugin will still be active until the next restart, and so will have a memory status of Restart,
        but a disk manifest status of Disabled.
        </summary>

So an operator who presses disable because a share has gone wrong has, until the
next restart, done nothing to that share. The status on disk says disabled and
the code in memory goes on answering. That is the server's behaviour and not a
defect this plugin can fix from outside.

**The rule this plugin takes from it.** Disable is not the control for stopping a
share. Revocation is, it is immediate, and #46 is where it is built. The
documentation says so where an operator reads it, because the button that looks
like the emergency stop is not one.

**What the plugin does anyway.** The resolution routine reads the plugin's own
status and refuses when it is not active, so a disabled plugin refuses a live
share on the request after the operator pressed disable rather than after the
next restart. The routine is #48 and it is the single place the decision is made,
which is what makes this one condition rather than one per route. The status
values are the server's:

    grep -oE 'name="F:MediaBrowser.Model.Plugins.PluginStatus.[A-Za-z]+"' "$model" | sort -u
    name="F:MediaBrowser.Model.Plugins.PluginStatus.Active"
    name="F:MediaBrowser.Model.Plugins.PluginStatus.Deleted"
    name="F:MediaBrowser.Model.Plugins.PluginStatus.Disabled"
    name="F:MediaBrowser.Model.Plugins.PluginStatus.Malfunctioned"
    name="F:MediaBrowser.Model.Plugins.PluginStatus.NotSupported"
    name="F:MediaBrowser.Model.Plugins.PluginStatus.Restart"
    name="F:MediaBrowser.Model.Plugins.PluginStatus.Superceded"
    name="F:MediaBrowser.Model.Plugins.PluginStatus.Superseded"

A refusal for this reason is a refusal like any other on the guest route: the
caller is told nothing about why, which is #26.

The sentence above is a claim about behaviour, so it has a test behind it, over
every status that is not Active rather than over the one an operator presses:

    git grep -n 'public void APluginThatIsNotActiveRefusesALiveShare' -- Jellyfin.Plugin.ShareLinks.Tests/
    Jellyfin.Plugin.ShareLinks.Tests/ShareResolutionTests.cs:225:    public void APluginThatIsNotActiveRefusesALiveShare(PluginStatus status)

Nothing else changes. Records stay where they are, expiry keeps running against
the clock rather than against the plugin, and re-enabling the plugin resolves the
shares that have not expired or been revoked in the meantime. A disabled plugin
is a pause, not a revocation, and it is described that way rather than as a
safety measure.

## Uninstalling

The server offers a hook just before removal:

    common=~/.nuget/packages/jellyfin.common/10.11.11/lib/net9.0/MediaBrowser.Common.xml
    grep -A3 'name="M:MediaBrowser.Common.Plugins.IPlugin.OnUninstalling"' "$common"
    <member name="M:MediaBrowser.Common.Plugins.IPlugin.OnUninstalling">
        <summary>
        Called when just before the plugin is uninstalled from the server.
        </summary>

**Whether that hook runs on every route an operator can take to remove a plugin
was not measured.** A plugin directory deleted from the filesystem, a container
rebuilt from an image, or a server that never came back up to run the hook are
all ways a plugin leaves without being asked. So the hook is where the tidy path
is implemented and it is not where the guarantee comes from. What the plugin
promises is the list below, which holds whether the hook ran or not.

## What is on disk, and what removes it

Three files.

The configuration file, which the base class writes for every plugin, under the
path the server supplies:

    grep -A2 'name="P:MediaBrowser.Common.Plugins.BasePlugin`1.ConfigurationFilePath"' "$common"
    <member name="P:MediaBrowser.Common.Plugins.BasePlugin`1.ConfigurationFilePath">
        <summary>
        Gets the full path to the configuration file.

The share store, which holds the records, and the key the token hashes are
computed with. Both are this plugin's own, both live in the plugin's data
folder, and both names are fixed in one place:

    git grep -nE 'public const string (StoreFileName|KeyFileName)' -- Jellyfin.Plugin.ShareLinks/PluginServiceRegistrator.cs
    Jellyfin.Plugin.ShareLinks/PluginServiceRegistrator.cs:60:    public const string StoreFileName = "shares.json";
    Jellyfin.Plugin.ShareLinks/PluginServiceRegistrator.cs:66:    public const string KeyFileName = "share-key";

The plugin asks the server where that folder is rather than choosing one, and
there is a single place it does the asking:

    git grep -n 'DataFolderPath' -- 'Jellyfin.Plugin.ShareLinks/*.cs'
    Jellyfin.Plugin.ShareLinks/PluginServiceRegistrator.cs:138:        return Path.Combine(plugin.DataFolderPath, fileName);

Why the store sits there rather than in the configuration is
`docs/share-store.md`, and what the key is, how it is made and what rotating it
costs is `docs/share-key.md`.

**The one action that removes it.** Delete the plugin's data folder. The server
reports the path as `DataFolderPath`, and it is a folder this plugin owns
entirely, so removing it removes every share record and the keyed hash secret
with them. The concrete path depends on where the server keeps plugin data and is
the server's to decide, so no path is written here: a path written here that is
wrong on somebody's install is worse than the property, which is that both of the
files this plugin writes are inside one folder and neither is anywhere else.

The third is the configuration file, and it is left out of that action rather
than forgotten. Where it sits is the server's to decide, and what it holds is
settings: no token, no record, and nothing about a guest. `docs/personal-data.md`
is the list of what is held about a person, and it is the store rather than this
file. That is what makes the deliberate purge one action instead of a hunt.
`docs/share-store.md` argues the same choice for other reasons and this is a
third.

## What an uninstall does not undo

Anything the plugin changed on a guest's account is not in the folder above and
is not removed by deleting it. An uninstall leaves whatever the plugin wrote onto
an account exactly as it was, and this sentence is the whole of the disclosure.

Nothing is put back, and that is a decision rather than a gap.
`docs/account-restoration.md` records it: this plugin writes a policy onto an
account it created and onto no other, so there is no prior state any account of
somebody else's is waiting to be restored to. What the end of a share does to an
account it made is disable it, which `docs/guest-accounts.md` decides and #58
built, and an uninstall does not reach that either: the accounts an operator
finds afterwards are the ones whose shares had not ended.

A guest who has already received media data has that data. No uninstall reaches
it. That is a residual risk rather than a gap, and #84 is where the security
page states it beside the others.

## What is not covered here

The removal of the two files this plugin owns. The hook is not implemented, so
nothing here runs when the plugin is uninstalled:

    git grep -n 'OnUninstalling' -- 'Jellyfin.Plugin.ShareLinks/*.cs' ; echo "exit=$?"
    exit=1

and the one deletion in the plugin is not this one. It removes the temporary
file of a write that failed, on a path that has not touched the store:

    git grep -n 'File.Delete' -- 'Jellyfin.Plugin.ShareLinks/*.cs'
    Jellyfin.Plugin.ShareLinks/ShareStore.cs:483:            File.Delete(path);

So the action above is an operator's, and the list is what they act on.

Whether the hook runs on every removal path, as said above. Measuring it needs a
running server, which no test in this repository may reach, and
`docs/testing.md` is where that rule is written.

What a restored backup does to shares that were revoked or expired before the
backup was taken is `docs/backup-restore.md`, not this document.
