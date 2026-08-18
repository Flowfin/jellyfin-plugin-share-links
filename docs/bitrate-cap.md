# Where the bitrate ceiling is enforced

This is the comparison issue #61 asks for, the choice it asks to be made, and the
list of what the choice does not constrain.

Everything below about the server was read out of the packages this plugin
compiles against, at the version `Directory.Build.props` pins:

    grep -n 'JellyfinVersion' Directory.Build.props
    15:        <JellyfinVersion>10.11.11</JellyfinVersion>

## What is already fixed

The per-share ceiling is on the record, and it is carried and shown rather than
acted on:

    git grep -n 'MaxBitrateBitsPerSecond' origin/master -- Jellyfin.Plugin.ShareLinks/
    origin/master:Jellyfin.Plugin.ShareLinks/BitrateCap.cs:34:/// <see cref="ShareRecord.MaxBitrateBitsPerSecond"/> already takes, and a second
    origin/master:Jellyfin.Plugin.ShareLinks/Configuration/configPage.html:162:                        cell(row, share.MaxBitrateBitsPerSecond);
    origin/master:Jellyfin.Plugin.ShareLinks/EffectiveBitrate.cs:11:/// is <see cref="ShareRecord.MaxBitrateBitsPerSecond"/>; the invited account
    origin/master:Jellyfin.Plugin.ShareLinks/ShareRecord.cs:237:    public long? MaxBitrateBitsPerSecond { get; init; }
    origin/master:Jellyfin.Plugin.ShareLinks/ShareRecord.cs:293:            MaxBitrateBitsPerSecond = record.MaxBitrateBitsPerSecond,
    origin/master:Jellyfin.Plugin.ShareLinks/ShareStoreExtensions.cs:226:        MaxBitrateBitsPerSecond = record.MaxBitrateBitsPerSecond,
    origin/master:Jellyfin.Plugin.ShareLinks/ShareSummary.cs:104:    public long? MaxBitrateBitsPerSecond { get; init; }
    origin/master:Jellyfin.Plugin.ShareLinks/ShareSummary.cs:128:            MaxBitrateBitsPerSecond = record.MaxBitrateBitsPerSecond,

A field, two copies from one record to another, a listing row and a cell on the
page. So the storage side is settled and the open question is where that number
becomes a stream that obeys it.

Transcoding stays on for a guest, and that is a decision rather than an oversight:

    git grep -n 'EnableVideoPlaybackTranscoding' origin/master -- Jellyfin.Plugin.ShareLinks/GuestPolicy.cs
    origin/master:Jellyfin.Plugin.ShareLinks/GuestPolicy.cs:201:        policy.EnableVideoPlaybackTranscoding = true;

A ceiling below what direct play needs forces a transcode, so an account that may
not transcode turns every capped share into a broken player. That is what makes
the ordinary case a lower-quality stream rather than a refusal, and it is why the
refusal below is a second leg and not the whole answer.

## The three options

### Set the ceiling on the invited account

The ceiling is a switch on the account policy this plugin already writes, and the
switch is guarded rather than assumed: `GuestCapabilityTests` asserts the server
still carries it under that name and with that type, so a server line that renamed
or dropped it would red the suite instead of leaving a document describing a
setting nobody sets.

    git grep -n 'RemoteClientBitrateLimit' origin/master -- Jellyfin.Plugin.ShareLinks.Tests/
    origin/master:Jellyfin.Plugin.ShareLinks.Tests/EffectiveBitrateTests.cs:108:        Assert.Equal(0, new UserPolicy().RemoteClientBitrateLimit);
    origin/master:Jellyfin.Plugin.ShareLinks.Tests/EffectiveBitrateTests.cs:109:        Assert.Equal(0, new ServerConfiguration().RemoteClientBitrateLimit);
    origin/master:Jellyfin.Plugin.ShareLinks.Tests/GuestCapabilityTests.cs:58:        { "RemoteClientBitrateLimit", "Int32" }

It costs one value on an object the plugin already builds, and what it buys is
that the plugin is not in the request path: the server applies the ceiling
wherever it decides a stream, so there is no route here to miss.

What it costs is that the ceiling belongs to an account rather than to a share.
One account holding two shares with different caps has one switch to hold both,
so the number written has to be derived from the set of live shares naming the
account rather than taken from the share being created. That derivation is a
second thing that can be wrong: it has to be rewritten every time that set
changes, and the set changes on expiry, which is an instant nothing calls the
plugin at. A share that expires with nothing else moving leaves the account
carrying a ceiling that no live share asks for.

### Intercept the playback information request

The plugin sits in front of the request that asks what may be played and reports
the share's own ceiling for a guest session.

Exact per share, with no derived value to keep in step and nothing to clean up
when a share stops being live. A well-behaved client reads the ceiling it is given
and asks for a stream inside it, which is the ordinary case and the one that ends
in a lower-quality stream rather than in an error.

What it costs is a second surface where a missed route is the defect, which is the
cost decision 3 of #94 already accepted for confinement. Here it lands differently
rather than identically: a missed route in a confinement filter is a hole somebody
has to find and use, and a missed route here is a stream that quietly plays above
the cap and is indistinguishable from one that obeyed it.

It also trusts the client to act on what it was told. A client that never reads
the reported ceiling, or that has the stream address already, is not constrained
by this leg at all.

### Refuse a playback request above the cap

The request that asks for a stream above the share's ceiling is refused.

Alone this is weak, for the reason the issue gives and for a second one: a point
that can only refuse cannot produce the lower-quality stream that keeping guest
transcoding on exists to produce, so alone it contradicts a decision already
written down in `docs/guest-capabilities.md`.

Behind the interception it is neither of those things. The polite path has already
been given a ceiling it can meet, so the ordinary request arrives inside the cap
and transcodes; what reaches the refusal is a request that asked for more than it
was told it could have.

## The choice

Both mechanisms, not one. The playback information for a guest session reports the
share's cap as the ceiling, and a playback request above the cap is refused.

Each of the two has a hole the other closes. The interception alone trusts the
client's honesty. The refusal alone cannot produce the lower-quality stream and is
the leg the issue itself names as weak. Together the ordinary client is told a
number it can meet and the client that never asks politely meets a refusal.

The account-level route is not taken. Its collision is answerable, by writing the
lowest of the ceilings of the live shares naming the account, and the answer is
what makes it worse rather than what rescues it: it turns the switch into a
derived value with no event behind one of its inputs. Expiry is the input with no
event. Nothing calls this plugin at an expiry instant, so the cleanup would be a
second failure path, running on creation and revocation and silently not running
on the instant that actually ends a share.

This is not a preference between a simpler and a stricter answer. Under the
account route the failure is quiet, in the direction of serving more than was
asked for, and it is invisible to an operator reading the share view. Under the
chosen pair the failure is a route somebody forgot to stand in front of, which is
findable by enumerating the routes and is the same class of work the confinement
filter already owes.

The switch on the account stays where the server put it, and this plugin does not
write it:

    git grep -n 'RemoteClientBitrateLimit' origin/master -- Jellyfin.Plugin.ShareLinks/
    origin/master:Jellyfin.Plugin.ShareLinks/GuestPolicy.cs:35:/// <c>RemoteClientBitrateLimit</c> is bounded by #61 and #62; setting it here

The account's own limit and the server configuration's are still read rather than
ignored. They are the second and third inputs to `EffectiveBitrate.Lowest`, and
the ceiling this plugin reports and refuses against is the lowest of the three, so
an operator who has set either of them is not overridden by a share that asks for
more.

## What this does not constrain

A client that has the stream address already. It is constrained by the refusal leg
and by nothing else, because it never asks what it may play. The third clause of
#61, a test asserting that a request above the cap does not produce a stream above
the cap, therefore has to drive that path and not only the polite one, or it
proves the leg that was never in doubt.

Every playback route this plugin does not stand in front of. This is the accepted
cost above rather than a hedge: the set of routes a client can request bytes on is
the thing that has to be enumerated and closed, and until it is, this choice is a
ceiling on the routes that were thought of. No such enumeration exists in this
repository today, and the reflection test that proves the plugin's own action set
closed, `RoutePolicy`, judges what this plugin exposes rather than what the server
does.

Whether the reported ceiling reaches a client on the operator's own network. Not
measured. Measuring it needs a running server rather than a package, and the
manual check #65 asks for is where it is answered.

Anything after the stream has started. A cap decides what is served, not what
happens to it afterwards, and `docs/threat-model.md` already accepts that a guest
entitled to watch can hand the result to somebody else.

An operator who edits the guest account by hand. Under this choice the plugin
writes no bitrate value onto the account, so an operator who sets one there is
adding a fourth ceiling rather than overwriting the plugin's. It is read as one of
the three inputs above and it can only lower the result, never raise it.

Nothing here was measured against a running server. The reported ceiling the
interception would set is a name that exists in the packages this plugin compiles
against, and the surface the refusal would sit behind is another:

    grep -ac 'MaxStreamingBitrate' ~/.nuget/packages/jellyfin.model/10.11.11/lib/net9.0/MediaBrowser.Model.dll
    1
    grep -ac 'IMediaSourceManager' ~/.nuget/packages/jellyfin.controller/10.11.11/lib/net9.0/MediaBrowser.Controller.dll
    1

That is a grep over an assembly rather than reflection over its types, so a hit
says the name is in the file and a miss says it is not. Neither is a statement
about what a member does, and this page makes none.

## What this does not settle

The bounds an operator may set the cap within, and its defaults, are #62. No
number is picked here. They are picked there, in `BitrateCap`, and
`docs/configuration.md` carries the row: megabits per second written, bits per
second kept, at least 0.1 and at most 1000, and no value meaning no ceiling.

The arithmetic that takes the lowest of the caps that apply, and says which one
applied, is #64. This decides where the result is reported and refused against,
not how it is computed.

What happens when the cap cannot be honoured is #63, and its answer is a refusal
at playback with a warning at creation. Under this choice that condition is
reached when even the lowest playable version is above the cap, because the
ordinary case is a transcode down to the reported ceiling.

The third clause of #61 is not met by this document. Nothing in the plugin touches
either mechanism:

    git grep -nE 'MaxStreamingBitrate|PlaybackInfo|IMediaSourceManager' origin/master -- Jellyfin.Plugin.ShareLinks/ ; echo "exit=$?"
    exit=1

Nothing in the tree enforces a bitrate ceiling today. This is the decision the
enforcement is built against.

## What this page said before

Until this revision this page chose the account switch, and #61 is where the other
answer was recorded. The two sat beside each other, and this page was the one that
was wrong: a document describing an enforcement point that was not the one chosen
is worse than no document, because #63 and #65 are both written against whatever
this page says.

Nothing had to be unwound, and that is measured rather than assumed. The command
above returns nothing for either mechanism, and the one hit for the account switch
is a remark recording that the field is left alone. So the cost of the disagreement
was this page and the two places that point at it, and no enforcement had been
built under the answer that was replaced.
