# Where share records are stored

This is the comparison issue #34 asks for and the choice it asks for, written
where a reader meets it rather than in a comment on the issue.

A share record is small and long lived. It outlives the request that made it, the
server restart after it, and usually the plugin version that wrote it. What it
needs from a store is four things: frequent small writes, durability across a
restart, survival across a plugin upgrade, and permissions tight enough that a
keyed hash can sit in it.

Everything below about the server API was read out of the packages this plugin
compiles against, `Jellyfin.Controller` and `Jellyfin.Model` at the version
`Directory.Build.props` pins:

    grep -n 'JellyfinVersion' Directory.Build.props
    15:        <JellyfinVersion>10.11.11</JellyfinVersion>

The member names and the sentences quoted from their documentation come from the
XML files shipped in those packages.

## The three candidates

### The plugin configuration file

`BasePlugin<TConfigurationType>` already gives the plugin a configuration file
and the code to write it. `Plugin.cs` inherits it today.

    common=~/.nuget/packages/jellyfin.common/10.11.11/lib/net9.0/MediaBrowser.Common.xml
    grep -oE 'name="[PM]:MediaBrowser[.]Common[.]Plugins[.]BasePlugin.1[.](ConfigurationFilePath|SaveConfiguration|XmlSerializer)"' "$common" | sort -u
    name="M:MediaBrowser.Common.Plugins.BasePlugin`1.SaveConfiguration"
    name="P:MediaBrowser.Common.Plugins.BasePlugin`1.ConfigurationFilePath"
    name="P:MediaBrowser.Common.Plugins.BasePlugin`1.XmlSerializer"

It is the path of least resistance and it is wrong here for two reasons that are
not about effort.

The write is whole-file. `SaveConfiguration` is documented as "Saves the current
configuration to the file system", and the interface method that takes a new
configuration object is documented as "Completely overwrites the current
configuration with a new copy":

    grep -A3 'IHasPluginConfiguration.UpdateConfiguration' "$common"
    <member name="M:MediaBrowser.Common.Plugins.IHasPluginConfiguration.UpdateConfiguration(MediaBrowser.Model.Plugins.BasePluginConfiguration)">
        <summary>
        Completely overwrites the current configuration with a new copy.
        </summary>

Which caller the dashboard's save button reaches was not traced, so the exact
sequence is a claim rather than a measurement. What is not a claim is the shape:
the supported way to change this file replaces all of it, so two writers of one
file lose each other's work by construction rather than by a race that better
timing avoids. Shares change far more often than configuration does, which is
exactly the traffic pattern a whole-file rewrite is worst at.

The second reason is who else touches the file. It is the file an operator edits
by hand, and it is the file the configuration page writes. A store that a person
is invited to edit is a store whose invariants are somebody else's discipline. A
keyed hash of a token belongs in a file nobody is asked to open.

### A file of the plugin's own, under the plugin data folder

The base class exposes a folder for exactly this.

    grep -A2 'name="P:MediaBrowser.Common.Plugins.BasePlugin.DataFolderPath"' "$common"
    <member name="P:MediaBrowser.Common.Plugins.BasePlugin.DataFolderPath">
        <summary>
        Gets the full path to the data folder, where the plugin can store any miscellaneous files needed.

It is more work than the configuration file, because the format, the write
strategy and the permissions all become this plugin's to decide rather than the
base class's to supply. That is also the reason to take it: each of those three
is a decision this record needs made deliberately, and the configuration file
makes all three by default and none of them the way a credential store wants.

### The server database

The plugin would be storing its records in a schema it does not own, versioned by
the server's migrations rather than by anything in this repository. Whatever that
costs in principle, on this line it is not reachable from the surface the plugin
compiles against at all:

    grep -c 'DbContext' ~/.nuget/packages/jellyfin.controller/10.11.11/lib/net9.0/MediaBrowser.Controller.xml
    0

Nothing in the controller assembly's documented surface names a database context.
Reaching one would mean referencing `Jellyfin.Database.Implementations`, which the
project does not reference:

    grep -c 'Jellyfin.Database.Implementations' Jellyfin.Plugin.ShareLinks/Jellyfin.Plugin.ShareLinks.csproj
    0

That is a package of server entities, and adding it puts this plugin's build,
its ABI floor and its upgrade story on a schema it does not control. The store
this plugin needs is one small table's worth of records. It does not buy enough
to pay for that.

## The choice

A file of the plugin's own, under `DataFolderPath`.

Against the four requirements. Frequent small writes are the plugin's own problem
to solve rather than a whole-file rewrite it inherits. Durability across a restart
is the filesystem's, the same as for the configuration file. Survival across a
plugin upgrade is the data folder's stated purpose, and it is a different folder
from the one holding the assembly. Permissions are decidable, because the plugin
creates the file rather than receiving it.

## What the choice does not cover

The permissions on that file are not evaluated here. The plugin can set a POSIX
mode on a file it creates; what a Windows server gives it by inheritance was not
measured, and no claim about it is made in either direction. Issue #28 owns the
key's storage and permissions and is where that measurement belongs, because the
key is the part where getting it wrong is worst.

How the file is written so that a crash or a concurrent request cannot leave it
half-written is #35, and this choice does not answer it. It only makes it
answerable, which the configuration file did not.

The format and the two version numbers in the file are #37, and the section below
is where they are written down. This choice fixed neither.

## The format, and the two numbers in it

The file is one JSON object. It carries the layout version first and the records
after it.

| What                    | Where                                   | What it says                               |
| ----------------------- | --------------------------------------- | ------------------------------------------ |
| The layout of the file  | `StoreVersion` at the top of the object | The shape of the file around the records.  |
| The shape of one record | `SchemaVersion` on each record          | Which fields that record was written with. |

Two numbers rather than one, because they answer different questions. A directory
of records each stamped with their own version says nothing about whether the file
holding them moved, and a file stamped once says nothing about a record inside it
that a newer plugin wrote. Both are checked on load and each has its own refusal.

The layout before the stamp was a bare JSON array of records. It is read as store
version 0 and migrated forward rather than refused, because a store written by a
version that predates the stamp is an ordinary upgrade and refusing it would lose
every share an early operator made. A JSON object with no `StoreVersion` is a
different thing and is refused: it is a file this code cannot place, and placing it
by assumption is the guess the refusal exists against.

Older is migrated, newer is refused. A store or a record from a version this code
does not understand is a downgrade, and the refusal names the number found and the
number understood, because an operator who has rolled a plugin back needs to know
which way to go. Reading it as far as it happens to parse would let a share resolve
under rules nobody in this version wrote.

The migration is in memory. A read returns records in the current shape and does
not rewrite the file, so a plugin that starts, reads and is stopped again leaves
the store exactly as it found it, and the new shape lands on the next write. The
cost of that is a store that is read many times and written never stays in the old
layout, which is the state the migration is written to keep readable anyway.

What a record's upgrade does is copy it and stamp it. Every field a later schema
added has a documented reading for its absence, and that reading is what the
property already does when nothing sets it, so there is no computation to get
wrong. The one hazard is a field added to the record and forgotten in the copy,
which would silently drop it from every migrated record, and what refuses that is a
test comparing a migrated record against its source field by field rather than a
list in this document.

What happens to that folder when the plugin is disabled, upgraded or uninstalled
is #38. The base class has a hook to reason about:

    grep -A2 'name="M:MediaBrowser.Common.Plugins.BasePlugin.OnUninstalling"' "$common"
    <member name="M:MediaBrowser.Common.Plugins.BasePlugin.OnUninstalling">
        <summary>
        Called just before the plugin is uninstalled from the server.

Whether it runs in every path an operator can take to remove a plugin was not
measured here.

What happens when the folder comes back from a backup while the key has moved on
is #40.

The last clause of #34, an interface narrow enough that this decision could be
revisited without touching the callers, is not in this document and not in the
tree. The interface is over a record whose fields are #33's, and writing those
fields here would land another issue's deliverable under this number. So #34 stays
open on that clause, with this document as its first two.
