# When a share stops working

This is the decision issue #45 asks for. Expiry sounds simple and is not, so each
awkward part is answered here rather than discovered by the first routine that
meets it.

Nothing in the tree reads a clock yet:

    git grep -n 'TimeProvider' origin/master -- Jellyfin.Plugin.ShareLinks/ Jellyfin.Plugin.ShareLinks.Tests/ ; echo "exit=$?"
    exit=1

and reading the machine clock directly is refused, so the routines these rules
bind will take the clock as a seam whether or not they remember to:

    bash .github/scripts/enforce-greppable-invariants.sh .github/invariant-fixtures/clock-comes-from-the-seam/violation ; echo "exit=$?"
    exit=1

    INVARIANT_SKIP=clock-comes-from-the-seam bash .github/scripts/enforce-greppable-invariants.sh .github/invariant-fixtures/clock-comes-from-the-seam/violation ; echo "exit=$?"
    exit=0

    bash .github/scripts/enforce-greppable-invariants.sh .github/invariant-fixtures/clock-comes-from-the-seam/near-miss ; echo "exit=$?"
    exit=0

## An absolute instant, in UTC

A share carries the instant it stops working, written when it is created. It is
not a duration that starts at first use.

A duration from first use has one attraction, that a guest who opens the link late
still gets the time they were promised, and two defects that outweigh it. A link
nobody opens never expires, so the operator cannot answer the only question they
will be asked, which is when this stops working. And first use is not a single
event: an invited guest opens the link on a phone and again on a television, which
is the ordinary case #25 is about, so a duration from first use has to pick one of
those and defend the choice.

UTC, because the alternative is a stored instant whose meaning changes when the
operator's offset does. What the operator types is converted at the edge, in the
route that creates the share.

## The boundary instant belongs to the refusal

A share is live strictly before its instant. At the instant, and after it, the
answer is refused.

Half-open is not a coin toss. It makes the sentence an operator reads, "this
expires at 12:00", true rather than nearly true, and it gives the boundary test
one answer instead of two: one tick before is live, the instant itself is refused,
one tick after is refused.

## The ceiling, and where the number lives

The create route refuses a lifetime longer than a configured ceiling. The ceiling
is a setting rather than a constant, and its default is thirty days.

Thirty days is a starting value with a reason, not a principle. A share is for
watching one thing; a month absorbs a holiday, a trip and a forgotten link,
and past that the link is doing something expiry exists to stop. The setting is
the part that is decided; the number is what an operator changes when their
deployment says otherwise. Where that setting is declared and what validates it is
#71.

The ceiling is checked when a share is created and not when it is resolved. A
record already holding a longer lifetime, written before an operator lowered the
ceiling, keeps the instant it was given. Re-deciding an existing share's expiry
from a setting that moved would mean a configuration edit silently shortening
links an operator has already handed out.

## Nothing extends a link

There is no route that moves the expiry of an existing record, and no route that
extends one.

Extending a link is issuing a link, and it should look like it: create a new share,
which mints a new token, and revoke the old one if it should stop early. The
reason is not tidiness. A leaked copy of a link is indistinguishable from the copy
the guest holds, so extending the share extends the leaked copy with it, and the
operator has no way to tell which copies are still in play. Issuing a new one
gives them a boundary they chose.

## A clock that moves backwards

Server clocks step. An NTP correction, a virtual machine resuming from a snapshot,
or an operator fixing a wrong time zone all move the clock, and one of those
directions is backwards.

Revocation is not affected. A revoked share is refused because the record says
revoked, not because of any comparison against a clock, so no clock movement
revives it. That is the property revocation has to keep and it is why revocation
is a recorded state rather than an expiry set to now (#46).

Expiry is affected, and the honest statement is that a backwards step can make an
expired share live again. What bounds it is the sweep: once the sweep has removed
an expired record, no clock movement brings it back, because there is nothing left
to compare. Between the instant and the sweep, a step larger than the elapsed time
makes the share live again for the difference.

That residual is accepted, and the alternative was weighed. Persisting a flag when
a share is first seen expired would close it, at the price of a store write on a
read path, and it would still be wrong for a share that expired while the server
was down. The bound worth keeping small is the sweep interval, which is where this
residual actually lives.

## What this does not settle

The sweep itself, how often it runs and what it deletes, is the retention rule in
#29 rather than this document. What this fixes is that the sweep is what bounds
the backwards-clock case, so a sweep that never runs makes that residual
unbounded.

The tests these rules exist for are #45's clauses and #79's: one tick before, the
instant, one tick after, the create route refusing a lifetime past the ceiling,
and no route moving an expiry. The first three are `ClockBoundaryTests` and the
fourth is
`ShareCreationTests.ALifetimePastTheCeilingIsRefusedBeforeAnAccountIsMade`, which
refuses through the same `ShareBounds` the store enforces rather than through a
copy of the number at the route. The last one is still a statement about a set of
routes rather than a test, and #47 is where a statement of that shape is
collected.
