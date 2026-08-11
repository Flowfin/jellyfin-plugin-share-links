# Where the bitrate ceiling is enforced

This is the comparison issue #61 asks for, the choice it asks to be made, and the
list of what the choice does not constrain.

Everything below about the server was read out of the packages this plugin
compiles against, at the version `Directory.Build.props` pins:

    grep -n 'JellyfinVersion' Directory.Build.props
    15:        <JellyfinVersion>10.11.11</JellyfinVersion>

## What is already fixed

The per-share ceiling is on the record, and nothing reads it:

    git grep -n 'MaxBitrateBitsPerSecond' -- Jellyfin.Plugin.ShareLinks/
    Jellyfin.Plugin.ShareLinks/ShareRecord.cs:237:    public long? MaxBitrateBitsPerSecond { get; init; }
    Jellyfin.Plugin.ShareLinks/ShareRecord.cs:293:            MaxBitrateBitsPerSecond = record.MaxBitrateBitsPerSecond,
    Jellyfin.Plugin.ShareLinks/ShareStoreExtensions.cs:199:        MaxBitrateBitsPerSecond = record.MaxBitrateBitsPerSecond,

Two of those three carry the value from one record to another. So the storage side
is settled and the open question is where that number becomes a stream that obeys
it.

Transcoding stays on for a guest, and that is a decision rather than an oversight:

    git grep -n 'EnableVideoPlaybackTranscoding' -- Jellyfin.Plugin.ShareLinks/GuestPolicy.cs
    Jellyfin.Plugin.ShareLinks/GuestPolicy.cs:74:        policy.EnableVideoPlaybackTranscoding = true;

A ceiling below what direct play needs forces a transcode, so an account that may
not transcode turns every capped share into a broken player. An enforcement point
that can only refuse contradicts that, and it is most of what rules out the third
option below.

## The three options

### Set the ceiling on the invited account

The ceiling is a switch on the account policy this plugin already writes, and it
is guarded rather than assumed: `GuestCapabilityTests` asserts the server still
carries it under that name and with that type, so a server line that renamed or
dropped it would red the suite instead of leaving a document describing a setting
nobody sets.

    git grep -n 'RemoteClientBitrateLimit' -- Jellyfin.Plugin.ShareLinks.Tests/
    Jellyfin.Plugin.ShareLinks.Tests/GuestCapabilityTests.cs:58:        { "RemoteClientBitrateLimit", "Int32" }

`GuestPolicy` sets every other switch and deliberately leaves this one, because
the number belongs to this issue and to #62:

    git grep -n 'RemoteClientBitrateLimit' -- Jellyfin.Plugin.ShareLinks/
    Jellyfin.Plugin.ShareLinks/GuestPolicy.cs:34:/// <c>RemoteClientBitrateLimit</c> by #61 and #62; setting either here would

So this option costs one value on an object the plugin already builds, written
through the same interface the account lifecycle in `docs/guest-accounts.md`
already goes through. What it buys is that the plugin is not in the request path:
the server applies the ceiling wherever it decides a stream, so there is no route
here to miss. What it costs is that the ceiling belongs to an account rather than
to a share, and the issue is right that this collides when one account holds two
shares with different caps.

### Intercept the playback information request

The plugin sits in front of the request that asks what may be played and lowers
the ceiling reported for a guest session to the share's own.

Exact per share, with no collision. The cost is the one decision 3 of #94 accepted
for confinement, and here it lands worse rather than the same. A missed route in a
confinement filter is a hole somebody has to find and use; a missed route here is
a stream that quietly plays above the cap and is indistinguishable from one that
obeyed it. It also puts a second thing in the request path for a number that the
first option can hand the server before the request arrives.

### Refuse a playback request above the cap

Ruled out. The issue already says why it is weak alone: a client that never asks
politely is not constrained by a refusal on the polite path. The second reason is
above. Refusing cannot produce the lower-quality stream that keeping transcoding
on exists to produce, so this contradicts a decision already written down in
`docs/guest-capabilities.md` rather than merely being insufficient.

## The choice

The ceiling is enforced on the invited account, and this plugin supplies the
number rather than standing in the request path.

The collision the first option has is answered without deciding anything about
whether an account belongs to a guest or to a share. The number written to the
account is the lowest of the ceilings of the live shares naming it, which is the
arithmetic #64 owns, so an account holding two shares gets the stricter of the two
and neither share is served above its own cap.

That is a real cost and it is named rather than hidden. A guest with two shares is
capped on both by the tighter one, and the looser share is quieter than its
operator asked for. It errs in the direction that cannot serve more than was
asked for, which is the direction to err in, and an operator who needs the two
caps kept apart makes a second guest.

The number is recomputed when the set of live shares naming an account changes,
which is creation and revocation, and not on a timer. A share reaching its expiry
instant therefore leaves the account's ceiling where it was until something else
moves it. That is the same shape as the sweep in `docs/expiry.md` and it is
bounded the same way.

This does not move the ceiling into `GuestPolicy`. That routine writes the
switches a guest gets once, and this value changes over the life of an account as
shares come and go, so it is written beside the policy rather than inside the
routine that fixes it.

## What this does not constrain

The setting is named for remote clients, and whether it reaches a client on the
operator's own network was not measured. Measuring it needs a running server
rather than a package, and a name is not evidence. An operator who expects the cap
to hold for a guest inside their network has to establish that themselves. What
would measure it is the manual check #65 asks for, and it belongs on that check's
list.

A client that never asks politely. The ceiling is applied where the server decides
a stream, and whether it reaches every path a client can request bytes on was not
measured here either. This is the residual the third option was rejected for, and
choosing a different option does not remove it: it moves it from the plugin to the
server.

An operator who edits the account afterwards. The plugin writes the number, does
not hold it, and does not watch it. An operator raising the account's limit by
hand raises it for every live share on that account until the next creation or
revocation rewrites it.

Anything after the stream has started. A cap decides what is served, not what
happens to it afterwards, and `docs/threat-model.md` already accepts that a guest
entitled to watch can hand the result to somebody else.

A share that names no ceiling. The record holds a nullable value and the account
switch is not nullable, so what the plugin writes when no live share names a
ceiling is a value meaning no limit rather than an absence. Which value that is
was not measured, and getting it wrong writes a ceiling of nothing where none was
meant. #62 has to settle it against the server rather than against this document.

## What this does not settle

The bounds an operator may set the cap within, and its defaults, are #62. No
number is picked here. They are picked there, in `BitrateCap`, and
`docs/configuration.md` carries the row: megabits per second written, bits per
second kept, at least 0.1 and at most 1000, and no value meaning no ceiling.

The arithmetic that takes the lowest of the caps that apply, and says which one
applied, is #64. This decides where the result is written and not how it is
computed.

What happens when the cap cannot be honoured is #63. Under this choice that
condition is reached only when even the lowest playable version is above the cap,
because the ordinary case produces a transcode rather than a refusal, which is
narrower than it would have been under the third option.

The third clause of #61, a test asserting that a request above the cap does not
produce a stream above the cap, is not met by this document. The seam it tests at
is #64's routine and the write to the account, and nothing in the plugin touches
either:

    git grep -n 'IUserManager' -- Jellyfin.Plugin.ShareLinks/ Jellyfin.Plugin.ShareLinks.Tests/ ; echo "exit=$?"
    exit=1

Nothing in the tree enforces a bitrate ceiling today. This is the decision the
enforcement is built against.
