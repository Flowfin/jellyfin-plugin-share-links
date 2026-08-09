# What a guest may do with the shared item

This is the list issue #57 asks for. Being allowed to watch is not one permission,
and an operator handing somebody a link is entitled to know what else they handed
over.

The plugin creates the guest account itself, so every switch below is one this
plugin sets rather than one an operator is asked to remember. The defaults are the
narrow ones, and where the narrow answer is not available the reason is written
next to it rather than left out.

## The capabilities the issue names

| Capability                         | Default | How it is set                               |
| ---------------------------------- | ------- | ------------------------------------------- |
| Play the shared item               | allowed | `EnableMediaPlayback` true                  |
| Resume where they stopped          | allowed | no account switch exists                    |
| Seek within the item               | allowed | no account switch exists                    |
| Mark it watched                    | allowed | no account switch exists                    |
| Rate it                            | allowed | no account switch exists                    |
| Download it                        | refused | `EnableContentDownloading` false            |
| Cast to another device             | refused | `EnableSharedDeviceControl` false           |
| Remote control another session     | refused | `EnableRemoteControlOfOtherUsers` false     |
| Join a synchronised playback group | refused | `SyncPlayAccess` set to its narrowest value |

Downloading is refused because the shared item leaves the server permanently and
no expiry undoes that. It is not a setting in this version.

Four of the nine have no switch. Resume, seek, watched state and rating are
writes to the server's own user data, and the account policy offers nothing that
turns them off. Refusing them would mean this plugin standing in the path of every
playback request and every user-data write, which is a larger surface than the
thing being refused is worth. They are allowed, and what that means for who can
see a guest's viewing is `docs/playback-visibility.md`.

## The switches the issue does not name and this plugin still has to set

Two of them stay allowed, and one of those is the only place in this document
where the narrow answer is the wrong one.

| Switch                           | Value  | Why                                                                                                                                                                                                |
| -------------------------------- | ------ | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `EnableVideoPlaybackTranscoding` | `true` | A bitrate ceiling below what direct play needs forces a transcode, so an account that may not transcode turns every capped share into the failure #63 is about and the guest sees a broken player. |
| `EnableAudioPlaybackTranscoding` | `true` | Same, for audio.                                                                                                                                                                                   |
| `EnablePlaybackRemuxing`         | `true` | Same, for the cheaper case where only the container has to change.                                                                                                                                 |
| `EnableRemoteAccess`             | `true` | A link that only works inside the operator's own network is not a share.                                                                                                                           |

Everything else is off, one row each rather than a sentence listing them, because
a value written next to its own name is a value a test can read back.

| Switch                       | Value   | Why                                                                                                                                                                            |
| ---------------------------- | ------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| `IsAdministrator`            | `false` | A guest administers nothing. This is the one whose absence would undo every other row.                                                                                         |
| `EnableContentDeletion`      | `false` | Deleting from the library is not part of being lent one item.                                                                                                                  |
| `EnableMediaConversion`      | `false` | Converting produces a file, which is downloading by another route.                                                                                                             |
| `EnableSyncTranscoding`      | `false` | Same, for the offline sync path.                                                                                                                                               |
| `EnableLiveTvAccess`         | `false` | Live television is not the item the share names.                                                                                                                               |
| `EnableLiveTvManagement`     | `false` | Same, and it changes the server's own recordings.                                                                                                                              |
| `EnableCollectionManagement` | `false` | A collection is a change to what everybody else sees.                                                                                                                          |
| `EnableSubtitleManagement`   | `false` | Same, and it writes files next to somebody's media.                                                                                                                            |
| `EnableLyricManagement`      | `false` | Same.                                                                                                                                                                          |
| `EnableUserPreferenceAccess` | `false` | The account is the plugin's rather than the guest's, and a guest editing its preferences is a guest editing something an operator will not think to check when the share ends. |
| `EnablePublicSharing`        | `false` | The server's own sharing feature, which would let a guest hand on what this plugin handed them, outside anything this plugin can revoke.                                       |
| `IsHidden`                   | `true`  | A guest account on the sign-in list tells everybody who visits the server who has been invited to it, which is a disclosure the share itself never made.                       |

## Where the switches are set

In `GuestPolicy`, one routine rather than a policy assembled wherever an account
happens to be created. A policy built property by property does not complain about
the property nobody set, so a second creation path would differ from the first in
exactly the switches nobody thought about. The route that creates the account and
hands the policy to the server is #67.

Every switch is written there, including the ones whose value matches the server's
default. A default is the server's decision and it can move between server lines,
which would otherwise widen a guest on an upgrade nobody connected to this plugin.

Two fields are deliberately left alone. `MaxActiveSessions` is bounded rather than
left at its default; how far is #56. `RemoteClientBitrateLimit` is the
account-level ceiling and belongs to #61 and #62, which decide where the cap is
enforced and what its bounds are. Setting either here would decide a number those
issues own.

## What is checked, and what is not

The names are checked. `GuestCapabilityTests` asserts that every switch this
document names is still a property of the server's account policy with the type it
expects, and that the narrowest synchronised playback value still exists by name.
A name is a claim about another artefact, and a server line that renames or drops
a switch would otherwise leave this document listing settings nobody sets.

The values are checked too, and this document is what they are checked against.
`GuestPolicyTests` reads every row above out of this file and asserts that the
policy `GuestPolicy` builds holds that value, so a row changed here without the
routine changing reds the suite, and so does the reverse:

    dotnet test --filter "FullyQualifiedName~GuestPolicyTests"

The reverse direction has one bound worth stating. It is measured by comparing the
policy this plugin builds against a fresh one from the server, so a switch this
plugin sets to the value the server already had is invisible to it. Such a switch
is set for the reason above, and it would not be refused if it were missing from
this document.

That bound was measured rather than supposed. Each of the twenty assignments was
deleted in turn and the suite run: eleven of them redden a test and nine do not,
and the nine are the ones whose value the server's default on this line already
holds. Deleting any of those nine changes nothing any test here can see, which is
the honest state of that half rather than a gap somebody forgot to close. They are
written anyway, because the argument for writing every switch is about a default
that moves under a plugin nobody touched, and a suite cannot show a move that has
not happened.

The nine are `EnableMediaPlayback`, `EnableVideoPlaybackTranscoding`,
`EnableAudioPlaybackTranscoding`, `EnablePlaybackRemuxing`, `EnableRemoteAccess`,
`EnableContentDeletion`, `EnableCollectionManagement`, `EnableSubtitleManagement`
and `EnableLyricManagement`.

What no test in this repository can show is that a switch, once set, is honoured.
The server is what enforces a policy; nothing here re-checks a capability when a
request arrives, and a server that ignored its own policy would pass every test
here. That is the residual, and it is the reason the rows above are values this
plugin asks for rather than behaviour it guarantees.

## What this does not settle

Which items the account can see at all is confinement, which is #52, and it is a
different question from what may be done with the one item a share names.

Whether a guest's playback progress is visible to the operator, and what reaches
the server's activity log, is `docs/playback-visibility.md`. This document says the
writes happen; it does not say who reads them.
