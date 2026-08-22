# The tests this repository refuses to write

## Why there is a list at all

`docs/testing.md` states the six clauses every test here runs under. Some obvious
tests break one of them, and this page is where each of those is refused by name
with the thing that stands in its place written beside it (#75).

The pairing is the whole point. A refusal with a replacement beside it is a
decision somebody took and can be argued with. A refusal on its own is a gap, and
the two are indistinguishable to a reader counting tests.

A replacement reads landed or owed, in the same sense `docs/parity-ledger.md`
uses. Landed names the test that carries it, and `RefusedTestsTests` resolves that
name against the compiled test assembly, so a replacement renamed away reds the
suite instead of going quiet. Owed names the issue that owes it and is checked no
further than the issue reference, which is what stops this page reading as a suite
that covers more than it does.

## The refusals

### A test that starts a real server and drives a browser

**Why it is refused.** It needs a display or a headless browser stack, and it
needs a network to reach the server it started. That is two clauses of the rule at
once, and the browser stack is a third, since driving one means invoking a binary
this repository does not control.

**Replacement, landed.** `GuestRouteTests`, `DecisionTableOnTheWireTests`. The
first names the action surface the assembly exposes and asserts what it admits.
The second drives the whole product of the decision's inputs through the route and
compares what reaches the caller as bytes, which is the part a browser would have
been watching.

**Replacement, owed.** #83. One run against a clean server, by hand, recorded with
what was done at each step. The route tests judge what the plugin answers; whether
an operator following the guide arrives at a working share is an observation and
not an assertion, and no run on a machine without a server supplies it.

### A test that exercises a real transcode

**Why it is refused.** It needs a media file and a transcoder binary, and the rule
admits neither. A test that shells out to a transcoder is testing whatever that
binary is on the day it runs.

**Replacement, landed.** `EffectiveBitrateTests`. The ceiling arithmetic is a
routine that takes three numbers and returns the lowest with the name of the one
that applied, so the behaviour worth asserting is reachable without anything
playing.

### A test of a real reverse proxy in front of the server

**Why it is refused.** It needs certificates in the machine's trust store, which
is the clause that fails on every machine except the one the certificate was
installed on.

**Replacement, landed.** `ShareLinkBuilderTests`. What a proxy would be exercising
is whether a request can talk the plugin into building a link to somewhere else,
and that is a unit question: `AForgedHostDoesNotReachTheLink` and
`AForgedHostCarryingAPortAndAPathDoesNotReachTheLinkEither` present forged host,
port and path and require the configured address to win.

### A test that a link works when opened from a phone

**Why it is refused.** It needs a phone.

**Replacement, landed.** `GuestSessionCeilingTests`, and the statement that this
plugin does not distinguish a device at all. #75 names the replacement here as a
session-level test carrying a device identifier, and that test may not be written:
nothing about a device or a session is an input to the decision, and
`TokenReuseTests.NoDeviceOrSessionReachesTheDecision` is the guard that refuses one
appearing. So the replacement is the session ceiling that a guest's account
actually carries, plus the absence of the concept, and the wording in the issue is
what is wrong rather than the suite.

### A demonstration of a confinement mechanism against a real library

**Why it is refused.** Watching either candidate confine an account needs a running
server with a library on it and an account making requests against it, which is two
clauses of the rule at once: the suite here runs with no server and with no network,
and `.github/workflows/headless.yml` proves both rather than asserting them.

**Replacement, landed.** `GuestConfinementTests`. It is a narrower thing than a
demonstration and the difference matters. What it asserts is that the members the
comparison rests on are still on the server this plugin compiles against, so a
server line that renames or drops one reds the suite instead of leaving
`docs/guest-confinement.md` describing a mechanism nobody can use. It asserts
nothing about how either mechanism behaves.

**Replacement, landed.** `GuestConfinementFilterTests`. The filter #239 built is
driven directly with a request context, and each of #44's five widening attempts is
a row named after the relationship it attacks. What that is not is a demonstration:
it shows what this plugin decides, and it shows nothing about a server applying the
decision, which still needs a running one.

**Replacement, owed.** #237. The harness that brings a real Jellyfin up is where a
run could watch the filter turn a widening away on a server rather than in a test,
and `docs/guest-confinement.md` says in its own section that no such run has been
made.

### A test that revocation stops a segment request already in flight

**Why it is refused.** The open handle belongs to the server, so reaching it means
a running server, a media file and a transcoder. All three are outside the rule
(#55).

**Replacement, landed.** `RevokingSignsOutTheGuestsThisPluginMadeForTheShareAndNobodyElse`,
`AGuestWhoStillHoldsAnotherLiveShareIsNotSignedOut`, `TheSignOutSparesNoToken`,
`EveryAccountNamedIsAskedAboutAndNothingElseIs`. The reachable statement is that
revoking a share asks the session manager to revoke the tokens of the accounts
belonging to that share and asks nothing about any other account, against a fake
(#55).

The second half of what an earlier reading proposed here turned out to be false
rather than owed, and saying so is the point of this line. A segment request does
not pass through the resolution that refuses a revoked record: the guest route
redirects to the item's own address, so the server serves the segments and this
plugin is not in that path. What refuses the next request is the server finding
the token revoked, which is why the sessions are ended rather than why a route
refuses. `docs/revocation.md` writes that chain out and says which part of it this
repository measured.

### A test that plays something end to end and watches the cap reach the stream

**Why it is refused.** Same three, for the same reason as the transcode line: a
real file, a real transcoder and a real client.

**Replacement, owed.** #65. One check by hand, recorded with what was played and
what the server reported, and with whether the ceiling reached a client on the
operator's own network. The seam below it is `EffectiveBitrateTests` above; what
the manual check covers is the step from the number this plugin computes to the
stream a guest receives, and where that step is enforced is #61.

## What is checked, and what is not

`RefusedTestsTests` reads this file. It requires every refusal to say why it is
refused, requires every refusal to carry at least one replacement, resolves every
name on a landed replacement against a type or a method in the test assembly, and
requires every owed replacement to name an issue.

What it does not judge is whether a replacement is a good one. That a route-level
test stands in adequately for a browser is an argument, and the review is where a
bad substitution is caught. Three of the lines above still carry an owed
replacement, and a green suite says nothing about any of them beyond the issue
numbers being there. Two of the three wait on one run against a real server, by
hand, which no machine without one supplies. The third waits on code nobody has
written, so it is a promise about a mechanism rather than about a machine.

Nothing refuses a refusal that was never written down. A test somebody declined to
write and never brought here is invisible to this page and to the test that reads
it.
