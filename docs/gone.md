# When what a record points at is gone

This is the decision issue #39 asks for. A record names one item and a set of
accounts, and every one of those can vanish underneath it while the record stays
exactly as it was written. Each case gets an answer here rather than arriving as a
null reference in whichever routine meets it first.

This page was written before anything here could ask the server a question, and it
said so. That half has changed for the item and has not changed for the accounts,
so the two are now in different states and the sections below say which is which.

The guest route asks the library whether the item is still there, and refuses when
it is not:

    git grep -n 'TheServerStillHoldsTheItem' origin/master -- Jellyfin.Plugin.ShareLinks/

What no route does is find out on its own. Nothing tells this plugin that an item
was removed, so the answer is derived when a guest presents a token and at no other
moment. Row T23 of `docs/threat-model.md` is where that sits.

Two commands this page rested on have been corrected rather than deleted, because
what they returned is the argument for the sections below. It said the decision
reads the record and nothing else, with `ILibraryManager` and `IUserManager`
appearing nowhere in the plugin, and gave that second command's exit status as 1.
It stopped being 1 when the create route began asking whether an item exists,
before this page was touched, so a page saying it was measured was reporting a
reading that had gone stale. The command and what it returns today:

    git grep -nE 'ILibraryManager|IUserManager' origin/master -- Jellyfin.Plugin.ShareLinks/ ; echo "exit=$?"

`IUserManager` is what is still absent on the resolution path, and it is what the
accounts section below still waits for.

## The item is gone

The share refuses. It is not deleted, and it does not repair itself.

That is built. `ShareRefusal.ItemGone` is the reason, and it is decided in
`ShareResolution` beside every other reason rather than at the route, so a second
guest route would inherit it instead of having to remember it:

    git grep -n 'ItemGone' origin/master -- Jellyfin.Plugin.ShareLinks/

The question is asked last, after everything the record and the caller can settle
between them. Two reasons, and the second is the one worth writing down. It is the
only question in the decision that costs a call into the server, so asking it
earlier would make a token naming a live share measurably more expensive than one
naming nothing, and #26 is the rule that those two may not be told apart. And a
caller who was refused anyway has no business making the server look anything up:
an uninvited caller learning that the item behind somebody else's share was removed
is a fact about the library handed to somebody outside it. Both halves are asserted
in `GuestRouteTests.ACallerRefusedBeforeTheItemQuestionDoesNotMakeTheServerLookAnythingUp`.

What it costs is one library lookup on a guest request that had otherwise touched
only this plugin's own store, paid on the requests that were about to succeed. That
is a real change in what the guest path reaches and it has not been measured on a
running server.

The lookup answers whether the item exists and never whether this caller may see
it. Those are different questions with different answers, and one reason carrying
both would make a permissions problem read as a deleted item, or the reverse.

A guest sees the one refusal every other failure gives. Which refusal a caller met
is what #26 exists to keep out of the answer, and "the item you were sent was
removed" is a fact about the library told to somebody outside it. An operator sees
the specific reason, in the administrator view, where the audience is the person who
owns the library.

The record survives the item. A scan that removed an item can be followed by one that
puts it back, and a record that deleted itself on the first scan cannot come back
with it. Keeping it also keeps who was invited to what, which is the audit question
#31 weighs, and tidying the view is not a reason to answer that question by accident.

## The item comes back under a new identifier

Nothing re-binds. The share stays refused and an operator who still wants it makes a
new one.

Re-binding would mean matching on something other than the identifier, by path or by
provider id, and both are guesses. The cost of a wrong guess is the worst outcome
this feature has: a share that is live and points at a different item from the one
the operator meant, which is a disclosure rather than an inconvenience. A dead share
that says it is dead costs an operator one minute.

The case is also not one this plugin can see. From here an item that came back under
a new identifier and an item that was deleted are the same observation, which is why
this is written as a limit rather than as a behaviour. Whether the server keeps an
item's identifier across a rescan was not measured, and the answer above does not
depend on it: no re-binding is the answer in both directions.

## The invited accounts are gone

What a request does when the account it names was disabled or deleted is already
answered, in `docs/invitation-and-expiry.md`, and this document does not answer it a
second time. The decision takes the account the server identified, an account that
cannot sign in is one the server identifies nobody as, and the request is refused for
carrying no caller.

What this document adds is the state rather than the answer. A share whose whole
invited set is gone can never resolve again for anybody, and the administrator view
says so rather than showing it as live. A share that still has one invited account
left is live and is not touched by this.

Deleted and disabled are two states in that view and not one. Disabling is
reversible, and an account that is enabled again resolves its share again; deletion
is terminal, because the record names an identifier and an account created afterwards
with the same name is a different account. Rendering both as not working makes an
operator delete an account to fix something a toggle would have fixed.

## What this does not settle

Where the item is asked about was the open question here and is answered: in the
resolution, with its own reason. What is left is the operator's half of the accounts
section above.

The administrator listing says which records still resolve, and it says it from the
record alone:

    git grep -n 'public static ShareSummary Of' origin/master -- Jellyfin.Plugin.ShareLinks/ShareSummary.cs

So it separates live from expired and from revoked, and a share whose invited
accounts are all gone is still shown as live, because nothing on that route asks the
server about an account. `ShareState` says in its own words that it has three values
and no fourth, so making the listing say it is not a field to add but an argument to
have, and it is the one this page's accounts section is waiting on rather than a
gap somebody can fill quietly.

Nothing is written to a record when its target goes. Nothing tells this plugin that
an item was removed or that an account was deleted, so a stored deadness would be a
value that goes stale silently, and every state above is derived when something asks.
The item is derived on the guest request and nowhere else, which means an operator
reading the listing learns nothing about it until somebody presents the token.

Nothing here was measured against a running server.
