# The security posture, and what is not defended

This is the page issue #84 asks for. It states the posture of the plugin in one
place and, at the end, the part that earns the rest: what this plugin does not
defend against.

Three neighbouring files answer different questions and a reader who finds one
first will take it for this one. `SECURITY.md` is how a vulnerability is reported
and what to expect after reporting it. `docs/threat-model.md` is the table of
threats, each with the control that answers it and the proof that control has,
and it is where each of the claims below was first argued. This page is those
arguments read together by somebody deciding whether to hand a link to a guest,
and it repeats no argument those pages already make.

## How to read a claim on this page

Every claim under the four sections that follow carries one of three words, and
the word is about this repository's test suite rather than about how sure
anybody is.

**Held.** A test in this suite fails when the thing behind the claim is removed,
and the test is named on the claim.

**Held in part.** A test holds some of what the claim says and the claim says
which part is left over.

**Not held by a test here.** No test in this suite holds it. The claim says what
does instead, or that nothing does. A claim in this state may still name a test,
where a test asserts a neighbouring fact or asserts the absence itself, and the
word is what a reader should take from the line rather than the backtick.

`SecurityPageTests` refuses a claim carrying none of the three, a claim carrying
more than one, and a **Held.** or **Held in part.** claim naming no test that
exists in the test assembly. It refuses a residual carrying any of the three or
naming a test at all, because a residual with a proof is a control that has
moved section.

## What a leaked link is worth

`docs/leaked-link.md` is the argument. A link is text, text travels, and the
design assumes it has already been read by a preview fetcher, a mail scanner, a
proxy log and whatever backup touched any of those.

### The text on its own opens nothing

**Held.** A share names accounts and the caller's identity comes out of the
server's request context rather than out of the link, so the two answers a
holder of the text can get are the two refusals. The same token resolving for the
account the record names is what keeps that from being a route that refuses
everybody:

```
git grep -n 'public void ACallerTheShareNamesIsSentToTheItem' -- Jellyfin.Plugin.ShareLinks.Tests/GuestRouteTests.cs
```

`ACallerTheShareNamesIsSentToTheItem`,
`ACallerTheShareDoesNotNameGetsNothing`,
`AValidUnexpiredTokenFromACallerTheServerHasNotIdentifiedGetsNothing`.

### Every refusal is the same bytes

**Held.** Whatever the reason, the caller gets the same answer, so asking cannot
be used to tell a live share from a token that names nothing.
`EveryRefusalIsTheSameBytes` reads the seven ways of getting nothing back off
what the response writes, and
`EveryReasonTheDecisionCanGiveReachesTheCallerAsTheSameBytes` drives every reason
the decision can produce through the route.

That two refusals carry the same bytes is asserted. That they take the same time
is not measured anywhere, and no claim about it is made here.

### The link carries the token and nothing else

**Held in part.** No item identifier, no account name, no expiry and no share
number travel in the text, so a holder of it learns only that somebody made a
share on this server. What is held by a test is the other end of the same
property, that a store file on disk cannot be turned back into a link:
`AStoreFileOnDiskCannotBeUsedToReconstructALink`. That the link is built from the
token alone is a property of one routine and is read rather than driven.

### The link is built from a base this plugin holds

**Held.** Nothing the request supplies reaches the address a share is handed out
under, so a forged host header does not produce a link pointing somewhere else:
`AForgedHostDoesNotReachTheLink`,
`AForgedHostCarryingAPortAndAPathDoesNotReachTheLinkEither`.

### Where a caller who is not signed in ends up

**Not held by a test here.** The guest route answers only callers the server has
already identified, so an unauthenticated request is refused in front of the
action by the server's own middleware and this plugin never sees it. No test in
this suite may start a server to watch that happen, which `docs/testing.md`
fixes. What a guest meets today is the server's refusal, followed by opening the
link again after signing in. #68 is where that is written down and what it is
still open on.

## What a share token can never reach

`docs/negative-capabilities.md` is the list, and #47 is where its wording is set
and where the lines that still owe a test are owed. The headings below are that
document's headings, and `SecurityPageTests` compares the two sets in both
directions so that a line reworded in one page and not the other reddens the
suite rather than leaving two lists that disagree.

### Any item other than the one named on the record

**Not held by a test here.** The mechanism is chosen and not built.
`docs/guest-confinement.md` chooses an authorization filter of this plugin's own
under #52 and says plainly that the filter does not exist, and the suite asserts
that absence rather than leaving it to be discovered: `ThisPluginCarriesNoFilterYet`.

### Any listing, search or collection that would enumerate other items

**Not held by a test here.** The same absence and the same filter. These are the
five widening attempts #44 names, and none of them is a request this plugin sees
until something confines the account.

### Any administrator route of this plugin

**Held.** Every controller action in the compiled assembly is classified and one
that is anonymous or unlabelled is refused, and both administrator actions are
reached only under the server's own elevation policy:
`EveryControllerActionThisPluginExposesCarriesAnExplicitPolicy`,
`EveryAdministratorActionIsReachedOnlyUnderTheServersOwnElevationPolicy`.

### Any administrator route of the server

**Not held by a test here.** The refusal belongs to the server's authorization
over its own routes and nothing in this repository judges another assembly. What
this plugin contributes is an account that is not an administrator, and that
value is asserted against the row `docs/guest-capabilities.md` decides:
`TheGuestGetsTheValueTheDocumentDecided`. That is a statement about what is asked
for and not about what is enforced.

### The creation, editing or revocation of any share

**Held in part.** Listing and revoking sit on a controller carrying the server's
elevation policy, and the verdict on both is asserted by
`EveryAdministratorActionIsReachedOnlyUnderTheServersOwnElevationPolicy`. There
is no create route in the tree, so the line is not held across the whole of what
it names, and #67 is where the rest of it arrives.

### Any other user's data, sessions or playback state

**Held in part.** Reaching another session, controlling another person's client
and joining a synchronised playback group are refused on the account this plugin
asks for: `TheGuestGetsTheValueTheDocumentDecided`,
`TheGuestCannotJoinASynchronisedPlaybackGroup`. Reaching another account's data
is confinement rather than a switch, so that half of the line sits behind #52
with the first two lines above.

### The plugin configuration

**Not held by a test here.** The plugin's configuration is served by the server
on its own route, behind the server's elevation, and this repository judges
neither. What is held is the same account switch as the server-administrator
line, and the same bound applies to it.

### The download route

**Held.** Downloading is refused for every guest this plugin makes, together with
the two routes that produce a file under another name, and all three values are
asserted against the rows `docs/guest-capabilities.md` decides:
`TheGuestGetsTheValueTheDocumentDecided`. There is no per-share download option
for the line to admit.

### Any route belonging to another plugin

**Not held by a test here.** There is no other plugin in the suite to point at,
and adding one would test an installation rather than this code. The refusal
belongs to the server's authorization over the other plugin's routes.
`docs/limits.md` carries it as a limit an operator meets.

### No route can move the expiry of an existing record

**Held.** Every routine in the compiled plugin that takes a record and answers
with one is driven twice, with a neighbouring instant on each side of the
record's own, and one whose answer expires at anything but the instant it was
given is refused: `NoRoutineInThisPluginMovesTheExpiryOfARecordItWasGiven`.

## What is logged, and what is never logged

`docs/logging.md` is the policy. The two failures it separates are a raw token in
a log file, which turns a leaked log into a working link, and a line naming a
guest and a title, which is a record of who watched what.

### The never list

**Held.** These never appear in a log line, at any level, in any form, including
inside an exception message, a stack trace this plugin writes, or a URL it logs.
The five leads below are compared against the policy's own list in both
directions, so a line reworded in one page and not the other reddens the suite.

- The raw token, whole or in part. Truncation is not a way to log one, because a
  prefix is a partial credential and it narrows a search.
- The keyed hash secret from #28, and anything from which it could be
  reconstructed.
- The keyed hash of a token. It cannot be turned back into a link, and it is the
  store's lookup key, so a log holding it lets a leaked log be joined to a leaked
  store.
- The title of the item a share names. It is the other half of who watched what,
  and a log file is copied by backup tooling and read by whoever can read the
  server's disk.
- The credential this plugin mints for an account it creates. It exists for the
  length of one call and is shown once in the answer to a create, and a prefix of
  it is a partial credential for the same reason a prefix of a token is.

`NoLineCarriesTheRawToken`, `NoLineCarriesTheStoredHash`, `NoLineNamesAnAccount`
assert three of them as absences, and `TheFieldsALineCarriesAreTheOnesThePolicyAllows`
is the half that does not need the forbidden thing to be named first: an item
title added to a line reddens a whitelist without anybody having to think of
titles.

### How a share is named in a line

**Held.** By the first eight characters of its record identifier, through the one
routine that makes that name, and a refusal names no share at all:
`AShareIsNamedByItsPrefixAndNeverInFull`, `ARefusalCarriesNoShare`.

### What refuses a logging call that carries a token

**Not held by a test here.** The greppable invariant lint refuses a logging call
that takes a token or a secret as a value, and it is proved to bite in
`.github/workflows/invariants.yml` rather than in the suite:

```
bash .github/scripts/enforce-greppable-invariants.sh .github/invariant-fixtures/token-not-logged/violation ; echo "exit=$?"
```

What it reads is source text. A token reaching a logging call under a name
carrying none of the words the pattern looks for walks through it, which is why
the policy also asks for the driven tests named above rather than resting on the
lint.

## What revoking a share stops

`docs/revocation.md` is the argument, and #55 is where the half about sessions is
owed.

### The record stops the link on the next presentation

**Held.** Revocation is stamped onto the record and the resolution decision reads
it on every request, so a token that has been working stops:
`ATokenThatHasWorkedStopsWorkingOnceTheShareIsRevoked`,
`RevokingStopsTheShareAndRecordsWhoPressedItAndWhy`.

### The sessions this plugin made for the share are ended

**Held in part.** What is measured is which accounts the route asks the server
about and that it asks about no others:
`RevokingSignsOutTheGuestsThisPluginMadeForTheShareAndNobodyElse`,
`EveryAccountNamedIsAskedAboutAndNothingElseIs`. Everything past the ask is the
server's. That the ask ends the sessions, that the server then refuses the next
segment, and what a client puts on the screen are statements about a running
server, and no run against one was made.

### An account this plugin did not create is left alone

**Held.** An invited account somebody else made keeps its session, and a guest
who still holds another live share keeps watching it:
`AnInvitedAccountThisPluginDidNotMakeIsLeftAlone`,
`AGuestWhoStillHoldsAnotherLiveShareIsNotSignedOut`.

### A stream already in flight

**Not held by a test here.** Nothing in this plugin stands in the playback path,
and the guest route answers by redirecting to the item's own address rather than
by proxying it. A client that already holds a segment address is refused by the
server the next time it presents the revoked token, and not by anything here.
`docs/refused-tests.md` is where the test that would watch the handle itself is
refused with the reason.

## What is not defended

These are not controls that failed. Each one was considered and left, and each is
the boundary of what any design here could defend. `docs/threat-model.md` is
where they were first argued and this section states them rather than repeating
the argument.

### A guest can pass on what they are entitled to

An invited guest can hand their own sign-in to somebody else. The share controls
who may start a session and nothing here tells one person holding an account
from another.

### Media that has been delivered has been delivered

A client that has already received media data has that data, and no expiry, no
revocation and no token model undoes it. Revocation stops the next request and
not the bytes already on the other machine.

### The operator is trusted completely

An operator with an administrator account can read every file the server can
read, including the store and the key, and can create a share for anything. A
control against the operator would be a control against the person the plugin is
built for.

### Anybody who can read the server's files as the server has both

The key and the media sit on the same disk under the same account. The keyed hash
buys exactly one thing, which is that a copy of the store alone is not enough.

### Transport security belongs to the deployment

If a link travels over plain HTTP the token is on the wire, and nothing in this
plugin is between it and whoever is listening. The plugin does not check that it
is served over TLS and does not refuse to run when it is not.

### The server's own authorization layer is trusted

Who the caller is comes out of the server's request context. If that layer is
wrong, this plugin is wrong with it, and nothing above repairs that.

### Reuse is not observable

A token works as often as it is presented until it expires or is revoked. No
address is compared and no line separates two callers, so an invited account that
passes its own sign-in on is a case nothing here detects.

### The channels a link travels through

Nothing was measured against any chat client, mail scanner or proxy. The design
assumes all three read the link and that none of them gain anything by it, and
that assumption rests on the section at the top of this page rather than on any
of them having been observed.

## What is checked on this page, and what is not

`SecurityPageTests` reads this file. It requires every claim to carry exactly one
of the three status words, requires a claim that says it is held to name a test
that exists in the test assembly, requires every residual above to carry no
status word and name no test, and compares two lists against the documents that
own them: the ten lines of `docs/negative-capabilities.md` and the never list of
`docs/logging.md`, in both directions.

What it cannot judge is whether a claim is true, whether the test named is the
test that would catch the failure, or whether a control is missing from this page
altogether. A claim carrying a status word and a real test name is a claim that
was written carefully, not one that was verified, and the review is where that is
caught.

Nothing compares the residual section against the accepted section of
`docs/threat-model.md`. A residual added there and not here is caught by a reader
or not at all.

Nothing on this page is enforced by being written here. Every control named above
is held by the test named beside it, in the file that test lives in, and this page
is a reading of them rather than a second place they are decided.
