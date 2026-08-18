# How invitation and expiry interact

A request succeeds when the record names the caller and the share has not reached
its instant. Two conditions, and both have to hold. Neither is inferred from the
other, and this page is the combinations where they disagree.

The two are separate clocks in the sense that matters: one moves when an operator
edits a record, the other moves on its own. `docs/expiry.md` fixes the instant and
its boundary, `docs/guest-accounts.md` fixes what an invited account is and what
happens to it when a share ends, and neither of them says that one condition
follows from the other. This is where that non-inference is written down and
where a test reads it.

## The rule

Both conditions are checked on every request, in the one resolution routine
(#48), and a request that fails either is refused. There is no branch that
succeeds without both.

Withdrawing an invitation takes effect on the next request, in the same way
revocation does. Nothing is swept, nothing is scheduled and no session is
consulted: the next request reads the record as it now is. The property that
makes that true is that the record is read per request rather than held, which is
`ShareResolution`'s own division of labour.

## The combinations

| What happens                                        | Before it          | After it                                            |
| --------------------------------------------------- | ------------------ | --------------------------------------------------- |
| The invitation is withdrawn while the share is live | The share resolves | Refused: the record does not name the caller        |
| The share expires while the guest is still invited  | The share resolves | Refused: the share has reached its instant          |
| The guest account is disabled by the operator       | The share resolves | Refused: the server identifies nobody               |
| The guest account is deleted                        | The share resolves | Refused: the server identifies nobody               |
| The guest is invited again after the share expired  | Refused: expired   | Refused: expired. The invitation does not revive it |

Each row is a test in `InvitationAndExpiryTests`, named for the row.

## What each row rests on

**The invitation is withdrawn.** The account comes out of the record's invited
list. The next request is refused because the record no longer names the caller,
with no sweep having run and nothing else changed. This is the row the issue's
rule is about: an invitation is a fact about a record, so withdrawing it is an
edit to that record and takes effect when the record is next read.

**The share expires.** The record is untouched and the clock crosses the instant.
The caller is still named, and it is still refused. Expiry is compared against the
clock every request, so nothing has to happen for it to take effect.

**The account is disabled, and the account is deleted.** Both reach this plugin
the same way, and the way is worth stating exactly, because half of it is not
ours. The decision takes the account the server identified. An account that
cannot sign in is an account the server identifies nobody as, so the request
arrives with no caller and is refused for that reason.

That the server refuses to authenticate a disabled or deleted account is the
server's behaviour and is not proven by anything in this repository. What is
proven here is the half that is ours: the plugin refuses when the server
identifies nobody, and the record is not edited by either event. Disabling is not
withdrawing an invitation, so an account that is enabled again resolves the share
again, and that is a test rather than a sentence.

Deletion has a second half. The record keeps an identifier, and the identifier
names nothing after the account is gone. An account created afterwards with the
same name is a different account with a different identifier, so the invitation
does not transfer to it. That is a property of naming accounts by identifier
rather than by name, and it is asserted rather than assumed.

**The guest is invited again after the share expired.** Adding the account back
to an expired record changes nothing, because expiry is on the record and is
judged against the clock. `docs/expiry.md` is explicit that nothing extends a
link: extending is issuing, so the answer to somebody who should get access again
is a new share with a new token, not an edit to a record whose link has already
been handed out and possibly copied.

## What this does not settle

Nothing here disables, deletes or restores an account. The lifecycle those rows
describe is `docs/guest-accounts.md`'s decision, and no code in this tree carries
it out:

    git grep -n 'IUserManager' -- Jellyfin.Plugin.ShareLinks Jellyfin.Plugin.ShareLinks.Tests ; echo "exit=$?"
    exit=1

So the two account rows are answered at the decision, which is where a request
meets them, and not at the point where an account changes. When the create and
removal paths exist they arrive against these rows rather than deciding them
again.

Nothing here was measured against a running server. Every row is the resolution
routine's answer to inputs a route would hand it, and what a route hands it is
#68.

A session already playing when one of these events happens is not this page's
subject. A request is what these rows are about. What a revocation does to a
session the server has already signed in is #55, in `docs/revocation.md`, and
these rows are unchanged by it.
