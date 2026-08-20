# The plugin catalogue checklist

This is the answer issue #92 asks for. Getting a plugin into the official
Jellyfin catalogue has requirements that are not about the code, and this page
works through them one at a time from the sources that state them rather than
from memory of what a plugin usually looks like.

One decision changes how the list reads, so it comes first. Decision 9 in #94 is
settled: this plugin is distributed through this repository's own manifest and no
submission to the official catalogue is made. That does not make the list
academic. Some of these requirements are about the plugin and bind whoever is
serving it, and those are ticked here. The rest are addressed to a plugin asking
the catalogue for something, and this plugin is not asking, which is a refusal
with a reason rather than a requirement quietly dropped.

## Where the requirements come from

Three sources, and each row below says which one it came from.

The manifest format and the fact that a repository can be hosted anywhere is the
announcement that introduced third party repositories:

    curl -sSI https://jellyfin.org/posts/plugin-updates/ | head -1
    HTTP/1.1 200 OK

The shape a catalogue actually serves is the published manifest itself, which is
worth more than the example in that post because it is what the server reads
today:

    curl -sSL https://repo.jellyfin.org/files/plugin/manifest.json | grep -oE '^ {8}"[a-zA-Z]+":' | sort -u
            "category":
            "description":
            "guid":
            "imageUrl":
            "name":
            "overview":
            "owner":
            "versions":

    curl -sSL https://repo.jellyfin.org/files/plugin/manifest.json | grep -oE '^ {16}"[a-zA-Z]+":' | sort -u
                    "changelog":
                    "checksum":
                    "sourceUrl":
                    "targetAbi":
                    "timestamp":
                    "version":

The licence condition is in the plugin template, which is the document the
project points a new plugin author at:

    curl -sSL https://raw.githubusercontent.com/jellyfin/jellyfin-plugin-template/master/README.md | grep -n 'permissive open-source license'
    415:Please note that this also means making "proprietary", source-unavailable, or otherwise "hidden" plugins for public consumption is not permitted. To build a Jellyfin plugin for distribution to others, it must be under the GPLv3 or a permissive open-source license that can be linked against the GPLv3.

## A licence the catalogue accepts

Ticked. GPL-3.0, from the template and unchanged.

    head -2 LICENSE
                        GNU GENERAL PUBLIC LICENSE
                           Version 3, 29 June 2007

The template's sentence quoted above is the whole of the condition, and GPLv3 is
the licence it names first.

## Source somebody can review

Ticked, and it is the same sentence's other half. The template refuses
proprietary, source-unavailable and otherwise hidden plugins. This repository is
public, the tree is the whole of the plugin, and the build that makes the package
is in `.github/workflows`.

## A unique identifier, the same in the manifest and in the plugin

Ticked. The announcement makes the point in those words: the identifier has to be
unique both in the manifest and in the plugin itself, or it collides with
somebody else's.

    grep -n '^guid:' build.yaml
    3:guid: "a3703f07-f83d-49a0-a09f-50b890a2baac"
    grep -n 'Guid.Parse' Jellyfin.Plugin.ShareLinks/Plugin.cs
    32:    public override Guid Id => Guid.Parse("a3703f07-f83d-49a0-a09f-50b890a2baac");

The two agreeing is not left to care. `PluginIdentityTests` reads both and reds
when they diverge, and it was the reason that test exists.

Unique against what the catalogue already serves, measured rather than assumed:

    curl -sSL https://repo.jellyfin.org/files/plugin/manifest.json | grep -c 'a3703f07-f83d-49a0-a09f-50b890a2baac'
    0

That is a measurement of one catalogue on one day, not a proof of global
uniqueness. The identifier was drawn as a version 4 identifier, which is where
the uniqueness actually comes from.

## A category the catalogue carries

Ticked. `build.yaml` says `Administration`, which is one of the eight the served
manifest uses:

    curl -sSL https://repo.jellyfin.org/files/plugin/manifest.json | grep -oE '"category": "[^"]*"' | sort -u
    "category": "Administration"
    "category": "Anime"
    "category": "Books"
    "category": "General"
    "category": "LiveTV"
    "category": "MoviesAndShows"
    "category": "Music"
    "category": "Subtitles"

The reason it is that category rather than `General` is written where the field
is, in `build.yaml`: the only person who opens this plugin's page is the
operator, and what they do there is administer access to the library.

## A name, an overview, a description and an owner

Ticked. All four are in `build.yaml`, and none of them is the template's text:

    grep -cE '^(name|owner|overview|description):' build.yaml
    4
    grep -nE '^(name|overview):' build.yaml
    2:name: "Share Links"
    19:overview: "Share a single library item with invited guests of your server, through a link that expires and can be revoked."

`PackagingMetadataTests` is what holds this one. It carries the template's own
placeholder strings and refuses each of them, so the template text coming back is
a red suite rather than a catalogue entry describing a different plugin.

## An image for the catalogue tile

Not required, and measured rather than assumed. The field exists in the served
manifest, and it is not carried by every entry:

    curl -sSL https://repo.jellyfin.org/files/plugin/manifest.json | grep -c '"imageUrl"'
    23
    curl -sSL https://repo.jellyfin.org/files/plugin/manifest.json | grep -c '"guid"'
    34

Twenty three occurrences of the field against thirty four entries, so a manifest
without one is a manifest the catalogue serves. There is none in `build.yaml`
today, which #136 names, and an entry without one is a tile with no picture
rather than an entry that is refused.

## A targetAbi that matches a supported server

Ticked. `build.yaml` claims `10.11.9.0`, and 10.11 is the line the project is
releasing:

    grep -n '^targetAbi:' build.yaml
    13:targetAbi: "10.11.9.0"
    curl -sSL https://api.github.com/repos/jellyfin/jellyfin/releases/latest | grep -oE '"tag_name": "[^"]*"'
    "tag_name": "v10.11.11"

The claim is not only a number in a file. The `abi-floor` job builds the plugin
against that floor as well as against the newer server package the tree compiles
with, so an API this plugin calls that does not exist at 10.11.9.0 is a red build
rather than a runtime failure on somebody's server.

## A manifest in the expected shape

Refused, and the reason is decision 9. This plugin is not asking the catalogue to
serve it, so the shape of what the catalogue serves is not a condition on it.

The same property still matters for this repository's own manifest, and it is
owed there rather than here. #90 is the issue that generates that manifest and
keeps it honest, and the two field lists at the top of this page are what it has
to produce. #89 decides the release process it is generated by.

## An artefact naming convention

Refused, for the same reason and with the same successor. What the catalogue
serves is a name derived from the plugin and the version:

    curl -sSL https://repo.jellyfin.org/files/plugin/manifest.json | grep -oE '"sourceUrl": "[^"]*"' | head -3
    "sourceUrl": "https://repo.jellyfin.org/files/plugin/bookshelf/bookshelf_13.0.0.0.zip"
    "sourceUrl": "https://repo.jellyfin.org/files/plugin/bookshelf/bookshelf_12.0.0.0.zip"
    "sourceUrl": "https://repo.jellyfin.org/files/plugin/bookshelf/bookshelf_11.0.0.0.zip"

That is a convention of the host serving those files. Nothing published by this
repository is served from there, so the convention this repository's artefacts
follow is the packaging tool's own, and it is #89 and #90 that fix it.

## A checksum for each published version

Refused here and owed there, the third of the same shape. Every version the
catalogue serves carries one, and every one of them is thirty two hexadecimal
characters:

    curl -sSL https://repo.jellyfin.org/files/plugin/manifest.json | grep -oE '"checksum": "[0-9a-f]{32}"' | wc -l
    277
    curl -sSL https://repo.jellyfin.org/files/plugin/manifest.json | grep -c '"checksum"'
    277

Nothing is published from here yet, so there is no artefact for a checksum to be
of. #90 is where the checksum and the artefact are made to agree, and #136 is
where the first artefact exists at all.

## Membership of the official catalogue

Refused, and this is the row the decision is actually about. Membership is not a
form somebody fills in. The set of plugins the official catalogue builds from is
derived from the repositories in the project's own organisation whose names begin
with a fixed prefix:

    curl -sSL https://raw.githubusercontent.com/jellyfin/jellyfin-meta-plugins/master/update_submodules.py | grep -nE 'orgs/jellyfin/repos|startswith'
    43:PAGINATION_URL = "https://api.github.com/orgs/jellyfin/repos?sort=created&per_page={per}&page={page}"
    59:        if _name.startswith("jellyfin-plugin-"):
    75:    if not repo.startswith("jellyfin-plugin-"):

So joining the catalogue means the repository moving into that organisation,
which is a change of who owns and maintains it rather than a metadata field. That
is the requirement this plugin declines, and declining it is what decision 9
says.

## The submission

Not made, and the reason is recorded here rather than left as an absence.

The plugin is distributed through this repository's own manifest. A user adds
that manifest as a repository in the dashboard and installs from it, which is a
supported route rather than a workaround: the announcement quoted at the top says
in those words that a third party repository is a JSON manifest at any location
pointing at binaries at any location, and that no particular hosting is required.

What is given up by not submitting is a review by somebody outside this
repository. That is a real loss and it is not made up for elsewhere, which is why
this page works the requirements from their sources instead of treating the
checklist as a rehearsal for a reviewer who is now never going to read it.

## What this page does not claim

No submission was attempted and none of this was checked against a catalogue
reviewer, so every row above is this repository reading a published requirement
and answering it.

Nothing here was measured against a running server. Whether the package installs
at the claimed floor is #136's condition and is not asserted here.

The three refusals about a manifest, an artefact name and a checksum are
refusals of a catalogue requirement, not statements that this repository owes
none of those things. It owes all three to its own manifest, and #89, #90 and
#136 are where they are met.
