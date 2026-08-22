# How a guest is confined to the shared item

This is the comparison issue #52 asks for. A share names one item, and the scope
has to hold on the server rather than in a client this plugin does not control.
The choice at the end is decision 3 of #94 and it was taken before this page was
written; what this page adds is the argument for it, the cost it accepts, and the
list of what it does not cover.

`docs/guest-capabilities.md` is the neighbouring question and a different one. It
decides what a guest may do with the one item a share names. This decides which
items the account can reach at all.

## What confinement has to hold against

Not a browser. The account belongs to a person who has the credentials, so the
request that tests confinement is the one made by hand:

- the parent of the shared item, asked for by its own identifier
- a sibling in the same folder
- the collection or library the item sits in
- a search whose results would include the item's neighbours
- the item's children, where the shared item is a season or a series

Those are #44's five widening attempts, and any mechanism chosen here has to
answer all five to the same account that legitimately reaches the item itself.

## Candidate one: the account's allowed tags

`UserPolicy.AllowedTags` is a list of strings on the account, and
`BaseItem.IsVisibleViaTags` is what reads it. Tag the shared item, allow that tag
and nothing else, and the account sees that item across the whole API without this
plugin sitting in any request path.

What it costs.

**It writes into library metadata.** The tag is a property of the item, not of the
share, so confining a guest means editing the operator's library. A metadata
refresh, an NFO writer or another plugin may touch the same field, and the tag
outlives the share unless something removes it.

**The tag is visible to everybody who can see the item.** It says, on the item,
that the item is shared. That is a disclosure the share itself never made, and it
is made to accounts that have nothing to do with the share.

**A tag is not an item.** Confinement is to a name rather than to an identifier,
so any other item carrying the same tag is also reachable. Keeping the name unique
per item is this plugin's problem to solve in a field anybody may edit.

**Two shares at once is where it breaks.** Tags accumulate on the account, so a
guest invited to two shares carries two allowed tags, which is correct. The
failure is on the other side: the tag belongs to the item, so two live shares
naming one item share one tag, and the first revocation either removes a tag the
second share still needs or leaves a tag the first share should have taken away.
Neither is a bug that can be fixed inside the mechanism, because the mechanism has
nowhere to record which share put the tag there.

**It inherits an upstream defect.** #52 records that a tag-restricted account
loses the cast and crew section entirely, as jellyfin/jellyfin#14926. That is
carried here as the issue records it. It was not reproduced in this repository and
no claim about its current state upstream is made.

## Candidate two: an authorization filter of this plugin's own

A filter on the request pipeline, which reads the store and decides whether the
account asking may reach the item asked for.

What it costs.

**Route coverage has to be complete.** The plugin is in front of every request it
sees, and a route it does not see is a hole rather than a refusal. That set is the
server's, and it grows when the server grows, so this cost is paid continuously
rather than once.

**It is in the request path.** Every request an invited guest makes passes through
this plugin's code, so a fault here is a fault the guest meets rather than a
permission that quietly stays narrow.

What it does better than the tag.

**Library metadata is untouched.** Nothing is written to an item, so nothing has
to be cleaned up, nothing is disclosed to other accounts, and no refresh can
undo the confinement.

**Two shares at once is answered by the store.** The filter reads the records, so
a guest invited to two shares reaches exactly the two items the two live records
name. Revoking one takes effect on the next request, because the answer is
computed per request from the records rather than held as state on the account,
and there is nothing left behind to clean up. That is the same property #46 asks
of revocation, reached for free rather than implemented twice.

**Confinement is to an identifier.** The record names an item by its identifier,
which nothing else carries and no editor can duplicate.

## The mechanism the issue does not name

`UserPolicy.EnabledFolders`, with `EnableAllFolders` off, confines an account to a
set of libraries. Both are on the account rather than on the item, so neither has
the tag's metadata cost.

It is not a candidate for this issue, because a library is not an item and a share
names an item. It is worth writing down anyway: it narrows what a missed route can
reach without answering the question this page is about, and something that
reduces the cost of the chosen candidate's known weakness should not have to be
rediscovered. Whether it is used alongside the filter is not decided here.

## The choice

The plugin's own authorization filter, which is decision 3 of #94.

The metadata cost decides it. Confining a guest by writing into the operator's
library is a side effect on data this plugin does not own, it is visible to
accounts outside the share, and it is undone by ordinary library maintenance
nobody connects to this plugin. Against that, the filter's cost is route coverage,
which is work this repository can hold with a check rather than a risk it has to
accept.

The two-shares case decides it a second time. It is the question #52 asks last,
and the tag mechanism has no place to record which share tagged an item, so the
answer under it is wrong in one direction or the other. Under the filter the
question does not arise, because nothing is stored on the account at all.

## What the choice does not cover

**A route the filter does not sit on.** This is the accepted cost and it is the
defect class to hunt for. `RoutePolicy` in the test project reads this plugin's
own routes; the routes the filter has to cover are the server's, and nothing in
this repository enumerates those.

**What the account may do with what it reaches.** The filter decides reachability
and nothing else. Playing, downloading, casting and the rest are the account
policy, which is `docs/guest-capabilities.md`.

**Data that has already been delivered.** A client that has received media has it,
and no confinement decided here takes it back. `docs/threat-model.md` is where
that sits as a residual.

**Anything the server answers before this plugin is reached.** A request refused
by the server's own authentication never arrives, and a request the server answers
outside the pipeline this filter is on is not covered by it.

**A route the filter never sees at all.** The section below enumerates the routes
it does sit on and says what that enumeration cannot promise.

## The filter, and the routes it sits on

Built under #239. `GuestConfinementFilter` is one authorization filter, registered
once on the server's request pipeline, carrying both this page's question and the
bitrate ceiling of `docs/bitrate-cap.md`. One registration and not two, which is
the decision recorded on #44: two surfaces that answer from the store at different
moments are two answers that can disagree.

What it decides is `GuestConfinement.Decide`, which takes records and an account
and returns a verdict and a ceiling. That routine is reachable with no request at
all, which is what lets every case below be driven under `docs/testing.md`'s rule.

### The routes it judges

Two families, kept apart because they are answered differently. The list is in
`ConfinedRoutes` and is reproduced by running:

```
git grep -n 'NamingOneItem\|Enumerating' -- Jellyfin.Plugin.ShareLinks/ConfinedRoutes.cs
```

A route that **names one item** is checked: the identifier in the path is compared
against the items the live records naming this account name. Everything under such
a path is judged against the same item, so an image, a segment or a playback
request inherits the answer given to the item itself.

A route that **lists, searches or browses** names no item to compare, so a guest
of this plugin is refused it outright. That is where three of #44's five widenings
live: the collection, the search, the neighbours. The alternative would be this
plugin filtering the server's own answer, which is a much larger surface and a
much easier one to get subtly wrong.

### What the enumeration does not cover

**A route nobody added.** The server's route table is not in the packages this
plugin compiles against, so no reading of this tree derives the list. It is
maintained by hand, it is not complete, and a path it does not reach is NOT JUDGED
rather than permitted. Those are separate answers in the code and separate answers
in the suite, so a reader counting what the filter refuses cannot read a hole as
something that was looked at.

**A series and its episodes.** A series is in the enumerating family, so sharing
one does not let a guest list its episodes. That follows from the record naming
one item and from #44's fifth widening being exactly that walk, and it means what
this plugin shares usefully today is an item a client plays directly.

**Whether the server applies the filter at all.** It is added to the options the
server builds its pipeline from. Whether that pipeline is built after plugin
services are registered is the server's own order, it is not in the packages this
plugin compiles against, and no test here reaches a server. No claim is made in
either direction.

**An account this plugin did not create.** It is not confined, deliberately.
Confining an account somebody already uses would take their own library away from
them because an operator shared one item with them, and membership is read from
`PluginCreatedUserIds` for that reason.

## What was measured here and what was not

Measured. The names this page rests on are properties and methods of the server
this plugin compiles against, and `GuestConfinementTests` asserts each one still
exists, so a server line that renames or drops one reds the suite rather than
leaving this page describing a mechanism nobody can use.

MEASURED SINCE #237, AND THIS PARAGRAPH SAID NO RUN SHOWED A SERVER APPLYING THE
FILTER. One does. The job brings up a real Jellyfin with the packaged plugin
installed, a library carrying two items, and a share naming one of them, and asks
as the invited guest:

    the item the share names -> 200
    the other item in the same library -> 404
    a listing that would enumerate the library -> 404

So the server does run the filter, and the filter refuses the widening on a server
rather than in a test. The `200` carries as much as the two refusals: a
confinement that also refused the item the share names would be a share that does
not work, and nothing in this repository could tell the two apart.

Not measured. What the server would do on a route `ConfinedRoutes` does not name.
The run walks three paths inside the list, and a path outside it is not judged by
this plugin at all, which is the accepted cost written under the choice above
rather than something the run narrows. The suite's own side of this is unchanged:
`GuestConfinementFilterTests` drives the filter's decisions directly, and
`docs/testing.md` fixes that the suite needs no server, no network and no media
file, which `.github/workflows/headless.yml` proves rather than asserts.

The other candidate was never run against a server and is not going to be: it is
the one this page did not choose.

The upstream defect named under the first candidate is carried from #52 and was
not reproduced.
