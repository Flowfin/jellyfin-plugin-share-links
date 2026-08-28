# What one operator action can create, and what the store can grow into

This is the decision issue #29 asks for. Every route that creates a record is a
route that fills a disk, and an administrator route is not exempt: an account can
be compromised, and a script with a loop in it does not know it is misbehaving.

Four bounds. Three of them refuse a create, one of them deletes. The values are in
`docs/configuration.md`, which is the table the suite compares against the class,
so the numbers below are the argument and that table is the authority.

`ShareBoundsTests` is where every statement on this page is checked, and each
section below names the test for its own claim. Two of them cover the page rather
than a bound: `ShareBoundsTests.EveryBoundHasADefaultOnTheConfiguration` asks that
each of the four has a default on the configuration class at all, and
`ShareBoundsTests.TheDocumentStatesTheDefaultsTheCodeHolds` reads this file and
refuses it for spelling a number the code no longer holds. That second one is why
the numbers below are written as words.

## A ceiling on live shares

A share is live when it is neither revoked nor past its expiry. The default is one
hundred.

`ShareBoundsTests.TheBoundaryInstantIsNotLive` fixes which side of the expiry
instant is live, and `ShareBoundsTests.ARecordThatNoLongerAnswersDoesNotHoldAPlace`
that a revoked or expired record frees its place.
`ShareBoundsTests.ACreatePastTheServerCeilingIsRefusedAndTheStoreDoesNotGrow` is
the ceiling itself, and it asserts the second half of its own name: a refused
create leaves the store the size it was.

The number is a starting value with a reason rather than a principle. This plugin
hands one item to one invited guest, so a hundred live at once is already an
operator who shares constantly, and a loop reaches it in about as long as a
hundred file writes take. Revoking a share or letting one expire frees a place
immediately; deleting the record is retention's job below and does not have to
happen first.

## A ceiling on live shares for one item

The default is ten.

Several live shares on one item is the ordinary case, because a film lent to four
people is four shares and not one share with four guests on it. What this bound
stops is a loop pointed at a single item consuming the whole server ceiling, which
would leave the server refusing every other share for a reason an operator could
not see from the item they were looking at.

`ShareBoundsTests.ACreatePastTheItemCeilingIsRefusedAndTheStoreDoesNotGrow` drives
it with the server ceiling set out of the way, so what refuses is this bound and
not the one above it.

## A ceiling on the lifetime a link may be given

The default is thirty days, and the reasoning is `docs/expiry.md`'s rather than a
second opinion about it: a share is for watching one thing, a month absorbs a
holiday and a forgotten link, and past that the link is doing the thing expiry
exists to stop.

It is checked when a share is created and never when one is resolved, which is
`ShareBoundsTests.ACreatePastTheLifetimeCeilingIsRefusedAndTheStoreDoesNotGrow`.
Lowering the setting does not shorten a link an operator has already handed out,
because a configuration edit silently expiring live links is worse than a long
link.

## Retention, and how an operator empties the store

A record that has stopped working is kept ninety days and then deleted. The
retention length is a setting rather than a constant, which is the part that is
decided; ninety days is a starting value.

Kept rather than pruned at once, because the record is what answers the question
an operator asks after the fact, which is who was invited to what and when it
stopped. What that record holds about a guest, and why ninety days rather than
forever, is `docs/personal-data.md`.

The clock starts at the instant the share stopped answering, not at the instant it
was created, and where a share was revoked before its expiry that is the
revocation. A share revoked after it had already expired stopped working when it
expired, and dating retention from the revocation would keep it for the window
twice over. `ShareBoundsTests.RetentionKeepsWhatStoppedInsideTheWindowAndDropsWhatDidNot`
is the window, taken at both edges of it, and
`ShareBoundsTests.RetentionDatesFromWhenTheShareStoppedAndNotFromTheLaterRevocation`
is the paragraph above.

Setting the retention to zero deletes a share at the first write after it stops
working, which is
`ShareBoundsTests.ARetentionOfZeroEmptiesWhatHasStoppedWorkingAtTheNextWrite`.
That is how an operator empties the store of what has expired, and it is
the only lever for it in this version; a button that sweeps on demand needs the
configuration page in #70.

## Where the bounds are enforced

In the store, in `ShareStore.AddAsync`, which sweeps what retention no longer
keeps and then refuses a create that would pass a ceiling. That order is
`ShareBoundsTests.TheSweepRunsBeforeTheCeilingIsCounted`, and it matters in the
direction an operator meets: a create refused by a ceiling counted before the
sweep is a refusal against records that are no longer kept. Not only in the route
that creates a share, because a ceiling enforced at the route is a ceiling that
holds for the callers somebody remembered, and the caller nobody thought about is
the one this is written against.

A refusal names the setting as well as the number, which is
`ShareBoundsTests.TheRefusalNamesTheNumberAsWellAsTheSetting`. An operator meeting
a ceiling has to find the line to change, and a sentence about too many shares
does not say which of three was met.

`ShareBounds` also refuses to be built from a value outside what the setting
admits, so a configuration file hand-edited to a ceiling of zero refuses to serve
rather than serving under a rule nobody wrote. That refusal happens when the
bounds are read rather than when the file is saved, which is a later moment than
the operator typing it; refusal on save is #71.
`ShareBoundsTests.AValueOutsideWhatTheSettingAdmitsIsRefusedByName` drives it
once per setting and asserts that the refusal carries that setting's own name.

## What this does not bound

The file, outright. The live ceiling bounds how many records answer at any
instant, and retention bounds how long a record that has stopped answering is
kept. What the file can actually reach is those two together with the rate an
operator creates and revokes at, and nothing here bounds that rate: a script that
creates a share and revokes it, in a loop, frees a live place every time and
leaves a record behind for the whole retention window. Refusing that needs a rate
limit, which is not one of the four bounds this issue names.

The sweep's timing. Retention runs on the way to a write rather than on a timer,
so a server nobody creates a share on never sweeps and holds what it held. That is
the direction that matters for the ceiling, because a share cannot be created
without a sweep happening first, and it is the wrong direction for prompt
deletion. The timer belongs with a scheduled task and there is none in this tree.
`docs/expiry.md` leaned on the sweep to bound its backwards-clock residual and
this paragraph was the size of that lean. It does not any more: the clock that
page is judged against is clamped so that it never steps backwards on a running
server, and the sweep is a second answer there rather than the bound. What the
lean is still the size of is the restart case that clamp does not reach.

`ShareStore.MutateAsync` is the general seam and is not bounded. Nothing refuses a
future caller that appends a record through it, and the invariant lint reads the
text of the tree rather than call graphs, so this one is held by the review.
