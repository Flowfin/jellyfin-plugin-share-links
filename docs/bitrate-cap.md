# Where the bitrate ceiling is enforced

This is the comparison issue #61 asks for, the choice it asks to be made, and the
list of what the choice does not constrain.

Everything below about the server was read out of the packages this plugin
compiles against, at the version `Directory.Build.props` pins:

    grep -n 'JellyfinVersion' Directory.Build.props
    15:        <JellyfinVersion>10.11.11</JellyfinVersion>

Nothing re-extracts the pasted output on this page. Other pages under `docs/` are
opened and read by the suite; the only mention of this one anywhere in the test
project is inside a comment:

    grep -rn 'bitrate-cap.md' Jellyfin.Plugin.ShareLinks.Tests/*.cs
    Jellyfin.Plugin.ShareLinks.Tests/GuestConfinementFilterTests.cs:254:    /// lowered, which is the interception leg of <c>docs/bitrate-cap.md</c>.

So a paste below goes stale with nothing saying so, and the way one is found is
somebody running the command again. That has happened more than once here, and
each instance is recorded where it sits rather than counted in one place, because
a count of them would be the next thing on this page to stop being true. Run the
commands rather than reading them.

## What is already fixed

The per-share ceiling is on the record, and it is carried and shown rather than
acted on:

    git grep -n 'MaxBitrateBitsPerSecond' -- Jellyfin.Plugin.ShareLinks/
    Jellyfin.Plugin.ShareLinks/BitrateCap.cs:34:/// <see cref="ShareRecord.MaxBitrateBitsPerSecond"/> already takes, and a second
    Jellyfin.Plugin.ShareLinks/Configuration/configPage.html:272:                        cell(row, share.MaxBitrateBitsPerSecond);
    Jellyfin.Plugin.ShareLinks/EffectiveBitrate.cs:11:/// is <see cref="ShareRecord.MaxBitrateBitsPerSecond"/>; the invited account
    Jellyfin.Plugin.ShareLinks/GuestConfinement.cs:213:                || record.MaxBitrateBitsPerSecond is not { } cap)
    Jellyfin.Plugin.ShareLinks/ShareCreation.cs:170:            MaxBitrateBitsPerSecond = request.MaxBitrateMbps is null
    Jellyfin.Plugin.ShareLinks/ShareRecord.cs:237:    public long? MaxBitrateBitsPerSecond { get; init; }
    Jellyfin.Plugin.ShareLinks/ShareRecord.cs:293:            MaxBitrateBitsPerSecond = record.MaxBitrateBitsPerSecond,
    Jellyfin.Plugin.ShareLinks/ShareStoreExtensions.cs:317:        MaxBitrateBitsPerSecond = record.MaxBitrateBitsPerSecond,
    Jellyfin.Plugin.ShareLinks/ShareSummary.cs:104:    public long? MaxBitrateBitsPerSecond { get; init; }
    Jellyfin.Plugin.ShareLinks/ShareSummary.cs:111:    /// <see cref="MaxBitrateBitsPerSecond"/> is what the operator typed onto this
    Jellyfin.Plugin.ShareLinks/ShareSummary.cs:155:            MaxBitrateBitsPerSecond = record.MaxBitrateBitsPerSecond,

A field, copies from one record to another, a listing row, a cell on the page, and
the two places the enforcement reads it. So the storage side is settled, and where
that number becomes a stream that obeys it is what the rest of this page decides.

**The paste above did not reproduce, and it is re-run rather than repaired by
hand.** It carried eight lines and the command returns eleven, because
`ShareCreation` and `GuestConfinement` gained hits after it was written and the
line numbers moved under it. It was found by running it while writing the section
below, which needed the same command and could not cite a stale copy of it. The
reference is dropped from the command with it: the paste is now what the command
returns at the commit that carries this page, so a reader running it in a checkout
of that commit gets these lines rather than whichever ones the remote has moved
on to.

Transcoding stays on for a guest, and that is a decision rather than an oversight:

    git grep -n 'EnableVideoPlaybackTranscoding' -- Jellyfin.Plugin.ShareLinks/GuestPolicy.cs
    Jellyfin.Plugin.ShareLinks/GuestPolicy.cs:253:        policy.EnableVideoPlaybackTranscoding = true;

**This paste did not reproduce either, and it is a second instance of what the
paragraph above records rather than the same one.** It named line 201 and the
command returns 253. The switch is still set and still set once, so the sentence
it stands under is unchanged and only the evidence moved. That is the direction
worth naming, because a reader who meets a stale line number cannot tell it from
a claim that has stopped being true.

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
asked for, and it is invisible to an operator reading the share view, which the section below
is what repaired for the chosen pair. Under the
chosen pair the failure is a route somebody forgot to stand in front of, which is
findable by enumerating the routes and is the same class of work the confinement
filter already owes.

## What the operator surface says about it

The share view carries two numbers per row and they are not the same number. The
ceiling column is the share's own, which is what an operator typed onto it. The
in-force column is what a guest of it would actually be held to, one line per
invited account, with the ceiling that produced it named beside the value.

Both, rather than the second alone. An operator who can only see the effective
number cannot tell a share whose own ceiling is doing nothing from one whose
ceiling is the one holding, and the repair for those two is in different places.

One line per account rather than one number per share, because the account's own
remote client limit is the one input of the three that is not a property of the
share. Two guests on one share have two answers, and a single number would be
wrong for one of them without saying which.

The answer is `GuestConfinement.Decide`'s, so the surface cannot disagree with the
filter about what would be applied. That has a consequence worth stating: the
share's own ceiling in that answer is the tightest across every live record naming
the account for the item, so a second share nobody was looking at is part of the
row. A per-record comparison would miss exactly that, which is this issue's own
failure one level down.

It is read at the instant the listing was read, in the same way the state column
is. The filter reads the same three values again per request, so a value somebody
moves in between is a disagreement between the page and the server rather than a
fault in either. A surface that showed nothing until it could promise everything
would show nothing.

The switch on the account stays where the server put it, and this plugin does not
write it:

    git grep -n 'RemoteClientBitrateLimit' -- Jellyfin.Plugin.ShareLinks/
    Jellyfin.Plugin.ShareLinks/GuestPolicy.cs:36:/// <c>RemoteClientBitrateLimit</c> is bounded by #61 and #62; setting it here
    Jellyfin.Plugin.ShareLinks/ServerCeilings.cs:40:        return accounts.GetUserById(account)?.RemoteClientBitrateLimit is { } limit
    Jellyfin.Plugin.ShareLinks/ServerCeilings.cs:54:        return EffectiveBitrate.FromServerValue(configuration.Configuration.RemoteClientBitrateLimit);

**This paste carried one line and the command returns three.** The sentence above
it survives the re-run and is not softened for it. Both hits that arrived read
the field and neither writes it, in `ServerCeilings`, which the filter and the
administrator listing both call so that the surface describing the ceiling and
the surface applying it cannot disagree:

    git grep -n 'ServerCeilings\.' -- Jellyfin.Plugin.ShareLinks/
    Jellyfin.Plugin.ShareLinks/GuestConfinementFilter.cs:231:        => ServerCeilings.OfAccount(_userManager, account);
    Jellyfin.Plugin.ShareLinks/GuestConfinementFilter.cs:234:        => ServerCeilings.OfServer(_serverConfiguration);
    Jellyfin.Plugin.ShareLinks/ShareLinksAdminController.cs:650:                account => ServerCeilings.OfAccount(_userManager, account),
    Jellyfin.Plugin.ShareLinks/ShareLinksAdminController.cs:651:                ServerCeilings.OfServer(_serverConfiguration),
    Jellyfin.Plugin.ShareLinks/ShareLinksGuestController.cs:235:            ServerCeilings.OfAccount(_userManager, caller),
    Jellyfin.Plugin.ShareLinks/ShareLinksGuestController.cs:236:            ServerCeilings.OfServer(_serverConfiguration),

So what had drifted was the evidence, and the claim it was standing under is
re-read against the new output rather than assumed to have travelled with it.

**The paste carried four lines when it was written and this change is what added
the fifth and sixth**, so it is re-run in the same act rather than left for the
next reader to find. Both new hits are the guest route asking the same two
questions the filter and the listing already ask, through the same routine, which
is the sentence above holding rather than being weakened: there is still one place
where "zero means no ceiling" is decided, and there are now three callers of it.

The account's own limit and the server configuration's are still read rather than
ignored. They are the second and third inputs to `EffectiveBitrate.Lowest`, and
the ceiling this plugin reports and refuses against is the lowest of the three, so
an operator who has set either of them is not overridden by a share that asks for
more.

## What a guest is told when the cap cannot be met, and the exception it spends

A guest holding a valid share, for whom nothing can be served under the ceiling in
force, is refused with a sentence saying so. That is an exception to #26, it was
granted on #63 on 2026-08-24, and it is written down here rather than only in the
issue because #26 is the rule everything else on this page defers to.

The decision, in the words it was taken in: the #26 rule protects against
unauthenticated probing; a signed-in guest holding a valid share is already
inside, so telling them leaks nothing outward, and refusals to everybody
unauthenticated stay byte-identical. The same decision is what permits this plugin
to read an item's playable versions at all, paid on the share surface.

**It is held to one caller and one condition.** The caller is one the resolution
has already accepted, which means the server signed them in, the token named a
live record, and the record names their account. The condition is that the
ceiling in force cannot be met for the item, which is
`CapReach.NothingCanBeServed` and nothing else. Every other caller and every other
outcome meets the bare refusal this route has always given.

**What holds it there is a test rather than this paragraph.**
`GuestRouteTests.EveryOtherRefusalOnThisRouteIsUnchangedByTheCapCondition` drives
the eight other ways of getting nothing with the condition armed on the same
store, compares what each writes against the literal a refusal wrote before this
change, and then reaches the condition on that same store so the comparison
cannot pass by nothing having been armed.

**The message names the condition and nothing else.** Not the ceiling, not what
the item can be played at, not which of the three ceilings was the one holding,
and not who made the share. It is a constant rather than a sentence assembled at
the refusal, so nothing a caller sent can come back to them in it, and
`WhatTheGuestIsToldCarriesNoNumberAndNoIdentifier` refuses a number arriving in
it later.

**The status is its own rather than a body hung off the bare refusal.** Two
answers that differ only in whether they carry a body are two answers a reader has
to compare byte by byte to tell apart, and the point of holding this to one
condition is that it is obvious which one somebody met. It is also not a "not
found": the share is there and the item is there, and what cannot be honoured is
the pair of them.

**Where the lookup is paid.** On the surface that opens a share, which happens
once, and never in `GuestConfinementFilter`, which stands in front of every stream
request a guest makes. A library call per segment is a cost with no ceiling on it.
A share carrying no ceiling in force asks the server nothing at all, and
`AShareWithNoCeilingAsksTheServerNothingAboutTheItemsVersions` proves that with a
strict double rather than asserting it.

**What this does not do.** It does not tell an operator anything; that is #286,
and it is the half that stops a share nothing can be served under being made in
silence. It does not change the request-path filter, which still answers a bare
refusal to a stream request above the ceiling. And it says nothing about what a
server does afterwards: what is asserted is that this plugin does not send the
guest on to the item, and no test here may reach a transcoder.

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

Whether the reported ceiling reaches a client. Still not measured, and narrower
than it was. The harness in #237 asks a real server for playback information as
the guest, with a ceiling above the one in force, and the server answers with the
source rather than a transcode plan, because the request carries no device
profile. An answer with no plan in it holds no lowered ceiling to read, so what
that run shows on this leg is that the request is answered and nothing more. A
client is what sends a profile, and no client was involved.

Anything after the stream has started. A cap decides what is served, not what
happens to it afterwards, and `docs/threat-model.md` already accepts that a guest
entitled to watch can hand the result to somebody else.

An operator who edits the guest account by hand. Under this choice the plugin
writes no bitrate value onto the account, so an operator who sets one there is
adding a fourth ceiling rather than overwriting the plugin's. It is read as one of
the three inputs above and it can only lower the result, never raise it.

THIS PARAGRAPH SAID NOTHING HERE WAS MEASURED AGAINST A RUNNING SERVER, AND THAT
IS NO LONGER TRUE OF THE REFUSAL LEG. The harness #237 built runs against a real
Jellyfin with the packaged plugin installed, and on a share capped at the lowest
ceiling this plugin accepts, carrying an item the server reports as above it:

    stream asking for 8000000, the ceiling being 200000 -> 404
    stream asking for 100000 -> 200

So the refusal reaches a request the server would otherwise have served, and a
request inside the ceiling is not caught by it. That is the leg this page said had
to be driven. It was found by building the harness rather than by re-reading this
page, and the sentence is corrected rather than deleted because what it claimed is
still true of everything below it.

The names the two legs rest on are in the packages this plugin compiles against,
which is the reading this paragraph carried before and which the run above does
not replace:

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

**THE PLAYBACK HALF OF THAT IS SETTLED AND THIS SECTION SAID BOTH HALVES WERE
ELSEWHERE.** The refusal a guest meets is #284 and is built; the section above on
what a guest is told is where it is argued, along with the exception to #26 it
spends. What is still not settled here is the operator half, which is #286: a
share whose ceiling nothing can be served under is still made in silence, and the
share view still says nothing about it.

Both mechanisms are built, on the one request-path surface #239 created. The
interception lowers the ceiling a playback information request asked for, and the
refusal turns away a request for bytes above the ceiling in force:

    git grep -n 'ReportingACeiling\|ServingAStream' -- Jellyfin.Plugin.ShareLinks/ConfinedRoutes.cs

The third clause of #61 is met, and by two things rather than one.
`AStreamRequestAboveTheCeilingIsRefused` drives the impolite path against the
filter, which is the half this page said had to be driven, and the boundary either
side of every ceiling is walked one bit at a time under #65. The harness in #237
then drives the same path against a real server, where the request above the
ceiling is refused and the one inside it is served.

What neither shows is the bytes of a stream. Showing those needs a player, and
`docs/refused-tests.md` refuses that test by name. What is claimed here is that a
request above the cap is refused before the server serves anything, on a server
rather than only against a double, which is what the clause asks and is not the
same sentence as a measurement of what came out.

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
