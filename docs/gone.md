# When what a record points at is gone

This is the decision issue #39 asks for. A record names one item and a set of
accounts, and every one of those can vanish underneath it while the record stays
exactly as it was written. Each case gets an answer here rather than arriving as a
null reference in whichever routine meets it first.

What the tree does today is the failure this document is written against. The
decision reads the record and nothing else:

    git grep -n 'ItemId' origin/master -- Jellyfin.Plugin.ShareLinks/ShareResolution.cs ; echo "exit=$?"
    exit=1

    git grep -nE 'ILibraryManager|IUserManager' origin/master -- Jellyfin.Plugin.ShareLinks/ ; echo "exit=$?"
    exit=1

Nothing asks the server whether the item still exists, so a share whose item a
library scan removed still resolves, and the guest is sent to an address that names
nothing:

    git grep -n 'return Redirect(TheItemsAddress' origin/master -- Jellyfin.Plugin.ShareLinks/ShareLinksGuestController.cs
    origin/master:Jellyfin.Plugin.ShareLinks/ShareLinksGuestController.cs:128:        return Redirect(TheItemsAddress(share.ItemId));

That is row T23 of `docs/threat-model.md`, and this document is the half of it that
can be settled before the lookups exist.

## The item is gone

The share refuses. It is not deleted, and it does not repair itself.

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

Where the item is asked about, and what asking costs on a path that otherwise only
reads its own store, belongs with the routes. The administrator view is #67 and #70;
whether the resolution itself asks the server about the item, or whether a refusal
for a missing item is derived where the view is built, is a shape question for those
issues rather than one this document takes from the side.

Nothing is written to a record when its target goes. Nothing tells this plugin that
an item was removed or that an account was deleted, so a stored deadness would be a
value that goes stale silently, and every state above is derived when something asks.

The words an operator reads have to agree with the refusal codes `docs/logging.md`
commits to, which are a fixed code rather than a sentence assembled from the request.
The set those codes come from is `ShareRefusal`, and it carries no member for a
missing item today.

The tests #39's second clause asks for, a missing item, a deleted account and a
disabled account, need a server interface this plugin references nowhere, which is
the second command at the top of this page. This document is the decision. The guards
arrive with the lookups, and until they do a share whose item is gone still resolves.

Nothing here was measured against a running server.
