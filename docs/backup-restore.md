# What a restored backup does to live shares

An operator restores a backup and the plugin's files go back in time. Nothing
else does. The clock keeps its place, the accounts on the server keep theirs, and
the links that were handed out are still in whatever chat window or mailbox they
were sent to. Everything awkward about a restore comes from that one asymmetry.

Two files of this plugin's own are involved, both under the plugin data folder
that `docs/share-store.md` decides on. The store holds the records. The key file
holds the key every token hash in that store was computed under, and
`docs/share-key.md` is where its lifecycle is fixed. They are one unit. A backup
that takes one without the other, or a restore that returns one without the
other, is the second case below.

## An old store restored under the current key

The records come back exactly as they were when the backup was taken, and their
hashes were computed under a key that has not moved, so the tokens that were live
then are live again.

That includes the ones the operator thought were gone. Revocation is a field on
the record, so a share revoked after the backup was taken comes back without that
field, and the link somebody was told to stop using works again. Deletion is the
same story with the record itself: a share removed after the backup is in the
restored file.

Nothing in the plugin can notice this. The restored store is a valid store, every
record in it is well formed, and there is no second place that remembers what was
revoked, because remembering it is what the store was for. The clock is the only
thing that still bites, and it bites only on expiry.

What an operator does about it, in the order that costs least:

- Revoke again anything that was revoked between the backup and the restore. This
  is exact if that list is known.
- Where the list is not known, rotate the key. Every stored hash was computed
  under the old key, so every link stops at once, including the ones that should
  have kept working. The rotation call answers with how many live shares it
  stopped, which is the number to tell people.

Rotating is the blunt answer and it is the one that needs no records of what
happened. Choosing it means reissuing the shares that were meant to survive.

## A current store read under an older key

The other half of the same asymmetry: the key file comes back from a backup, or
is restored on its own, and the store is the one from now.

Every hash in that store was computed under the key that has just been replaced,
so nothing in it matches anything. Presenting a perfectly good token gets the
refusal a token naming no share at all gets, and the two are the same answer for
`docs/leaked-link.md`'s reason: a caller who could tell them apart could ask this
server which of their tokens is real.

The plugin does not repair this and does not try. It never replaces a key file it
managed to read, so the store is not rewritten, nothing is re-hashed, and the
records sit there intact waiting for the key that matches them. A plugin that
minted a fresh key here would have destroyed the only thing that could still have
resolved them.

What an operator does about it is restore the key file from the same backup as
the store. If the matching key is genuinely gone, the shares in that store cannot
be recovered and have to be created again; the records still say who was invited
to what, so the list survives even when the links do not.

## A store restored after the shares in it have expired

Expiry is stored as an instant rather than as a duration or a countdown, so
nothing about a restore moves it. `docs/expiry.md` is where that is argued.

A share that had already expired when the backup was taken is still expired after
the restore, and a share that was live then but whose instant has since passed is
expired too. This is the one of the three cases where going back in time changes
nothing, and it is worth stating because the other two make it easy to assume
otherwise.

## What is checked

`BackupRestoreTests` covers all three cases against a real store and a real key
file in a directory it creates and removes.

The refusal in the second case is the one with a guard behind it rather than a
consequence of a shape. `AStoreRestoredWithoutItsKeyResolvesNothing` and
`AKeyThatDoesNotMatchTheStoreIsNotDistinguishableFromATokenNamingNothing` both
red when the lookup is changed to hand back a record it did not match.

The first case has no guard, because there is nothing to guard against: a
restored store is a valid store. The test there asserts what happens rather than
that something is refused, which is the honest shape for a case whose answer is
operator guidance.

## What this page does not cover

What a backup taken while the plugin was disabled contains, and whether every
removal path leaves the folder behind, is `docs/plugin-lifecycle.md` and the
issues it names.

Nothing here was measured against a server backup tool. The cases are reasoned
from what the two files hold and are tested by moving those files, which is what
a restore does to them and is not the same as watching a real restore run.
