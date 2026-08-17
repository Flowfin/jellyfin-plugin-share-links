# What a share token can never reach

This is the list #47 asks for. Each line is a flat statement about what presenting
a share token can never get to, whoever presents it, and each line says what holds
it today or that nothing does.

Two neighbouring documents answer different questions, and a reader who finds one
of them first will take it for this one. `docs/guest-capabilities.md` decides what
an invited guest may do with the single item a share names, switch by switch, and
that is #57. `docs/guest-confinement.md` chooses how the account is held to that
item at all, and that is #52. This page is about what the token reaches, which is
a statement about routes rather than about capabilities or about visibility.

## How to read a line

A line is not evidence. Five of the nine below have a test that fails when the
thing holding them is removed, and the rest do not, so the status is written into
the line rather than left to be inferred from its presence. A list whose lines all
look alike reads as coverage, and most of this list is not coverage yet.

Where a line is held by a switch on the guest account rather than by a refusal in
this plugin, that is said in the line. The distinction matters and it is the same
one `docs/guest-capabilities.md` closes on: this plugin asks for a narrow account
and the server is what honours the answer. Nothing in this repository re-checks a
capability when a request arrives, and a server that ignored its own policy would
pass every test here.

## The list

### Any item other than the one named on the record

Nothing holds this. The mechanism is chosen and not built, and the suite asserts
the absence rather than leaving it to be discovered:

```
git grep -n 'public void ThisPluginCarriesNoFilterYet' -- Jellyfin.Plugin.ShareLinks.Tests/GuestConfinementTests.cs
```

`docs/guest-confinement.md` is the choice under #52, and the filter it names is
what this line waits for.

### Any listing, search or collection that would enumerate other items

Nothing holds this, for the same reason and behind the same filter. These are
#44's five widening attempts, and `docs/guest-confinement.md` lists them as what
any mechanism chosen there has to answer.

### Any administrator route of this plugin

Held. `RoutePolicy` walks the compiled assembly, classifies every controller
action and refuses one that is anonymous or unlabelled, and two tests read the
result rather than the source:

```
git grep -n 'public void EveryControllerActionThisPluginExposesCarriesAnExplicitPolicy' -- Jellyfin.Plugin.ShareLinks.Tests/RoutePolicyTests.cs
git grep -n 'public void EveryAdministratorActionIsReachedOnlyUnderTheServersOwnElevationPolicy' -- Jellyfin.Plugin.ShareLinks.Tests/AdministratorRouteTests.cs
```

Both administrator actions are reached only under the server's own elevation
policy, spelled with the server's constant rather than a copy of the text it
holds, and exactly one action in the assembly is reached by a caller the server
has merely signed in.

### Any administrator route of the server

Not held here, and it cannot be. The refusal belongs to the server's own
authorization over its own routes, and what this plugin contributes is an account
that is not an administrator:

```
git grep -n 'policy.IsAdministrator = false' -- Jellyfin.Plugin.ShareLinks/GuestPolicy.cs
```

That value is asserted against the row `docs/guest-capabilities.md` decides, by
`TheGuestGetsTheValueTheDocumentDecided` in `GuestPolicyTests`. It is a statement
about what is asked for and not about what is enforced, and nothing in this
repository judges another assembly.

### The creation, editing or revocation of any share

Held for the routes that exist. Listing and revoking sit on a controller carrying
the server's elevation policy, and the test named under the administrator line
above asserts the verdict on both. There is no create route in the tree, so the
line is not yet held across the whole of what it names. #67 is where it arrives.

### Any other user's data, sessions or playback state

Held in part. Reaching another session or another person's is refused on the
account, and those three switches are asserted against the document that decides
them:

```
git grep -n 'EnableSharedDeviceControl\|EnableRemoteControlOfOtherUsers\|SyncPlayAccess' -- Jellyfin.Plugin.ShareLinks/GuestPolicy.cs
```

Another account's data is confinement rather than a switch, so that half of the
line sits behind #52 with the first two lines above.

### The plugin configuration

Not held here. The plugin's configuration is served by the server on its own
route, behind the server's elevation, and this repository judges neither. What is
held is the same account switch as the server-administrator line, and the same
bound applies to it.

### The download route

Held, and the line is narrower in the tree than in #47's wording. #47 says the
download route where downloading is not part of the share, which admits a share
that permits it. No such option exists: downloading is refused for every guest
this plugin makes, together with the two routes that produce a file by another
name.

```
git grep -n 'EnableContentDownloading\|EnableMediaConversion\|EnableSyncTranscoding' -- Jellyfin.Plugin.ShareLinks/GuestPolicy.cs
```

`docs/guest-capabilities.md` is where each of the three is argued. If a per-share
download option is ever offered, this line becomes the one #47 wrote and needs
what that document decides.

### Any route belonging to another plugin

Not held, and no test here can hold it. There is no other plugin in the suite to
point at, and adding one would test an installation rather than this code. The
refusal belongs to the server's authorization over the other plugin's routes, and
this is written down rather than left as a line quietly without a test.
`docs/limits.md` already carries it as a limit an operator meets.

### No route can move the expiry of an existing record

Collected here from #45, whose last clause is this sentence, because a statement
about every route belongs on this list rather than alone in an issue about
boundaries.

Held, for the routines that make a record out of a record. `ExpiryPolicy` reads
the compiled plugin for every routine that takes a record and answers with one,
drives each of them twice with a neighbouring instant on each side of the
record's own, and refuses one whose answer expires at anything but the instant it
was given:

```
git grep -n 'public void NoRoutineInThisPluginMovesTheExpiryOfARecordItWasGiven' -- Jellyfin.Plugin.ShareLinks.Tests/ExpiryPolicyTests.cs
```

The structural half is still true and is no longer what the line rests on. The
instant is init-only, so moving it means writing a new record, and all three
assignments, two writing a record and one building a listing row, are a copy:

```
git grep -n 'public required DateTimeOffset ExpiresAt' -- Jellyfin.Plugin.ShareLinks/ShareRecord.cs
git grep -n 'ExpiresAt = ' -- Jellyfin.Plugin.ShareLinks/
```

What the guard adds to that is the routine nobody has written yet. A fourth
writer taking a new instant compiles, passes every other test in the suite, and
is refused only here.

Two bounds, and both stay where they are. The subject is a routine that takes a
record; a routine rebuilding a record out of something other than a record, a
request body or the store's file, is outside it and is not judged. And a routine
is judged on the inputs it was driven with, which is why it is driven on both
sides of the instant rather than once: an extend-but-never-shorten rule would
pass a single run.

`docs/expiry.md` is the decision that nothing extends a link.

## What is checked, and what is not

Five lines are held by a test that fails when the thing behind it is removed: this
plugin's administrator routes, the creation and revocation routes that exist, the
three switches about other people's sessions, the download route, and the expiry
instant. Those tests are named in the lines above and run in the ordinary suite.

Four are not. Two wait on the confinement filter in #52, one waits on the create
route in #67 to finish the set it names, and one is a statement about the server's
own authorization that nothing here can judge.

Nothing reads this page. `LimitsTests` requires every document under `docs/` to be
accounted for in `docs/limits.md`, so this page arriving uncollected would red the
suite, and that is the whole of what any run judges about it. Whether a line here
still matches the test it names, and whether the list is complete, is what the
review is for. #47 is where
the remaining lines are owed and it stays open until each one has a test that
fails when its check is removed.
