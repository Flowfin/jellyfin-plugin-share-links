# What a guest may do with the shared item

This is the list issue #57 asks for. Being allowed to watch is not one permission,
and an operator handing somebody a link is entitled to know what else they handed
over.

The plugin creates the guest account itself, so every switch below is one this
plugin sets rather than one an operator is asked to remember. The defaults are the
narrow ones, and where the narrow answer is not available the reason is written
next to it rather than left out.

## The capabilities the issue names

| Capability | Default | How it is set |
| --- | --- | --- |
| Play the shared item | allowed | `EnableMediaPlayback` true |
| Resume where they stopped | allowed | no account switch exists |
| Seek within the item | allowed | no account switch exists |
| Mark it watched | allowed | no account switch exists |
| Rate it | allowed | no account switch exists |
| Download it | refused | `EnableContentDownloading` false |
| Cast to another device | refused | `EnableSharedDeviceControl` false |
| Remote control another session | refused | `EnableRemoteControlOfOtherUsers` false |
| Join a synchronised playback group | refused | `SyncPlayAccess` set to its narrowest value |

Downloading is refused because the shared item leaves the server permanently and
no expiry undoes that. It is not a setting in this version.

Four of the nine have no switch. Resume, seek, watched state and rating are
writes to the server's own user data, and the account policy offers nothing that
turns them off. Refusing them would mean this plugin standing in the path of every
playback request and every user-data write, which is a larger surface than the
thing being refused is worth. They are allowed, and what that means for who can
see a guest's viewing is #59.

## The switches the issue does not name and this plugin still has to set

Transcoding stays allowed: `EnableVideoPlaybackTranscoding`,
`EnableAudioPlaybackTranscoding` and `EnablePlaybackRemuxing` are true. This is the
one place where the narrow answer is the wrong one. A bitrate ceiling below what
direct play needs forces a transcode, so an account that may not transcode turns
every capped share into the failure #63 is about, and the guest sees a broken
player instead of a lower-quality stream.

Remote access stays allowed: `EnableRemoteAccess` is true. A link that only works
inside the operator's own network is not a share.

Everything else is off. `EnableContentDeletion`, `EnableMediaConversion`,
`EnableSyncTranscoding`, `EnableLiveTvAccess`, `EnableLiveTvManagement`,
`EnableCollectionManagement`, `EnableSubtitleManagement`, `EnableLyricManagement`,
`EnableUserPreferenceAccess` and `EnablePublicSharing` are all false, and
`IsAdministrator` is false.

`IsHidden` is true. A guest account that appears on the server's sign-in list
tells everyone who visits that server who has been invited to it, which is a
disclosure the share itself never made.

`MaxActiveSessions` is bounded rather than left at its default; how far is #56.
`RemoteClientBitrateLimit` is the account-level ceiling and belongs to #61 and
#62, which decide where the cap is enforced and what its bounds are.

## What is checked, and what is not

The names above are checked. `GuestCapabilityTests` asserts that every switch this
document names is still a property of the server's account policy with the type it
expects, and that the narrowest synchronised playback value still exists by name:

    dotnet test --filter "FullyQualifiedName~GuestCapabilityTests"

That guard exists because a name is a claim about another artefact. A server line
that renames or drops a switch would otherwise leave this document listing
settings nobody sets, and a policy object assembled property by property does not
complain about the one that is missing.

The values are not checked, and that is the clause of #57 that stays open. The
test it asks for asserts that a guest is refused each of the refused capabilities,
which needs a guest account to refuse, which is the account creation path in #51.
Until that lands, this document is a decision with its names verified and its
behaviour unverified, and the difference is deliberate.

Nothing refuses a capability added to this table without being added to the test.
The two lists move together by hand.

## What this does not settle

Which items the account can see at all is confinement, which is #52, and it is a
different question from what may be done with the one item a share names.

Whether a guest's playback progress is visible to the operator, and what reaches
the server's activity log, is #59. This document says the writes happen; it does
not say who reads them.
