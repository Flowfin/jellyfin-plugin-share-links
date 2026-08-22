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

A line is not evidence. Some of the lines below have a test that fails when the
thing holding them is removed and some do not, so every line opens with its own
verdict rather than leaving the reader to infer one from its presence. A list
whose lines all look alike reads as coverage, and part of this list is not
coverage.

Three verdicts and no fourth. **Held** means a test named in the line fails when
the thing behind it is removed. **Held in part** means one half of the line has
such a test and the other half does not, and the line says which is which. **Not
held** means no test here can hold it, and the line says whose refusal it is
instead. The split is derived rather than written down, because a count written
into the document it is a count of goes stale in the direction that flatters it:

    grep -A2 '^### ' docs/negative-capabilities.md | grep -oE '^(Held in part|Held|Not held)' | sort | uniq -c

`NegativeCapabilityTests` reads those verdicts and refuses a line that carries
none of them.

Where a line is held by a switch on the guest account rather than by a refusal in
this plugin, that is said in the line. The distinction matters and it is the same
one `docs/guest-capabilities.md` closes on: this plugin asks for a narrow account
and the server is what honours the answer. Nothing in this repository re-checks a
capability when a request arrives, and a server that ignored its own policy would
pass every test here.

## The list

### Any item other than the one named on the record

Held in part. On the routes this plugin stands in front of, and the part that is
not held is named at the end of this line. The filter
`docs/guest-confinement.md` chose under #52 is built (#239), and each of #44's
five widening attempts is a row of a theory named after the relationship it
attacks:

```
git grep -n 'public async Task EachOfTheFiveWideningsIsRefused' -- Jellyfin.Plugin.ShareLinks.Tests/GuestConfinementFilterTests.cs
git grep -n 'public async Task TheSharedItemIsReachedByTheAccountTheShareNames' -- Jellyfin.Plugin.ShareLinks.Tests/GuestConfinementFilterTests.cs
```

The second is what stops the first from being satisfied by a filter that refuses
everything.

What this does not hold is a route nobody added to `ConfinedRoutes`. That set
belongs to the server, the server's route table is not in the packages this
plugin compiles against, and the list is maintained by hand. Not judged is a
separate answer from reached, and the suite says so:

```
git grep -n 'public async Task APathTheListDoesNotReachIsNotJudged' -- Jellyfin.Plugin.ShareLinks.Tests/GuestConfinementFilterTests.cs
```

Whether the server applies the filter to any request at all is a registration on
the server's own pipeline. There is no server here and no claim about it is made
in either direction.

### Any listing, search or collection that would enumerate other items

Held in part. On the routes this plugin stands in front of, by the same filter
and the same test, which is named here as well rather than referred to, so that a
rename reaches both lines:

```
git grep -n 'public async Task EachOfTheFiveWideningsIsRefused' -- Jellyfin.Plugin.ShareLinks.Tests/GuestConfinementFilterTests.cs
```

A route that lists, searches or browses names no item to compare, so a guest of
this plugin is refused it rather than having the answer filtered, and three of
#44's five widenings are exactly those routes. The bound is the one above: a
listing route nobody added to the list is one this plugin does not see.

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

Held. Creating, listing, revoking and rotating all sit on one controller carrying
the server's elevation policy, and the test under the administrator line above
asserts the verdict on every action of it. It is named here as well rather than
referred to, so that a rename reaches both lines. This line said the create route
was absent until #67 landed it:

```
git grep -n 'public void EveryAdministratorActionIsReachedOnlyUnderTheServersOwnElevationPolicy' -- Jellyfin.Plugin.ShareLinks.Tests/AdministratorRouteTests.cs
git grep -n 'HttpPost("Shares")' -- Jellyfin.Plugin.ShareLinks/ShareLinksAdminController.cs
```

The set that test compares against is written out rather than counted, so a fifth
action added without an attribute of its own reds the suite instead of joining a
line that says it is held.

### Any other user's data, sessions or playback state

Held in part. Reaching another session or another person's is refused on the
account, and those three switches are asserted against the document that decides
them:

```
git grep -n 'EnableSharedDeviceControl\|EnableRemoteControlOfOtherUsers\|SyncPlayAccess' -- Jellyfin.Plugin.ShareLinks/GuestPolicy.cs
```

Another account's data is confinement rather than a switch, and that half is the
filter's since #239, on the routes its list names:

```
git grep -n 'public async Task EachOfTheFiveWideningsIsRefused' -- Jellyfin.Plugin.ShareLinks.Tests/GuestConfinementFilterTests.cs
```

In part rather than held, because the three switches above are asked for rather
than enforced here, and because the filter reaches only the routes its own list
names.

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
git grep -n 'public void TheGuestGetsTheValueTheDocumentDecided' -- Jellyfin.Plugin.ShareLinks.Tests/GuestPolicyTests.cs
```

`docs/guest-capabilities.md` is where each of the three is argued, and the second
command names the theory that drives every switch that document decides against
the policy this plugin writes, so a value changed here without the document
changing reds the suite. If a per-share download option is ever offered, this line
becomes the one #47 wrote and needs what that document decides.

### Any route belonging to another plugin

Not held, and no test here can hold it. There is no other plugin in the suite to
point at, and adding one would test an installation rather than this code. The
refusal belongs to the server's authorization over the other plugin's routes, and
this is written down rather than left as a line quietly without a test.
`docs/limits.md` already carries it as a limit an operator meets.

### No route can move the expiry of an existing record

Held, for the routines that make a record out of a record. Collected here from
#45, whose last clause is this sentence, because a statement about every route
belongs on this list rather than alone in an issue about boundaries.

`ExpiryPolicy` reads
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

This section carried a count of held and unheld lines until the count went stale.
It said five were held and four were not, with two of the four waiting on the
confinement filter and one on the create route; both of those landed, both lines
above were rewritten to say so, and the count under them was not. A hand count in
the document it is a count of is the failure mode this whole page exists against,
so it is derived now rather than written:

    grep -A2 '^### ' docs/negative-capabilities.md | grep -oE '^(Held in part|Held|Not held)' | sort | uniq -c

What a line's verdict means is fixed under `## How to read a line` above, and
`NegativeCapabilityTests` refuses a line that carries none of the three, refuses a
held line that names no test, and resolves every test a held line names against
the compiled test assembly. So a replacement renamed away reds the suite instead
of leaving this page naming it, which is the same guard `docs/refused-tests.md`
carries and for the same reason.

What no run judges is the part that matters most and it is unchanged. Whether the
test a line names actually holds the sentence above it is an argument, and the
review is where a wrong pairing is caught. Whether the list is complete is the
same kind of question, and nothing here can answer it: a capability nobody wrote
down is invisible to this page and to the test that reads it.

#47 is where the lines with no test are owed. Each of the three is a refusal
belonging to the server's own authorization over its own routes or to an assembly
this repository does not compile against, so each is written down with that
reason rather than left as a line quietly without a test.
