# What this plugin holds about an invited guest

This is the list issue #31 asks for. An operator running a server for people they
know is still holding data about those people, and the honest thing is to say what
it is and how it goes away rather than to be quiet about it because the deployment
is small.

Two lists are deliberately not this one. What the server records about a guest
watching something is `docs/playback-visibility.md`, and it is the server's to keep
rather than this plugin's. What this plugin writes into a log line is
`docs/logging.md`. This page is what the plugin itself stores.

## The record is the whole of it

One file of share records under the plugin's data folder is the only place this
plugin writes anything about a person. Where that file is and why is
`docs/share-store.md`.

Every field of a record is below, whether it identifies a person or not, because a
list of only the identifying ones is a list a reader has to trust rather than check.
The retention answer is per field for the same reason, even where several fields
share one.

| Field                     | Identifies a person                                                                 | How long it is kept                                     |
| ------------------------- | ----------------------------------------------------------------------------------- | ------------------------------------------------------- |
| `SchemaVersion`           | No. The shape of the record.                                                        | With the record.                                        |
| `Id`                      | No. The name a share is known by.                                                   | With the record.                                        |
| `ItemId`                  | Only beside the invited accounts, where it becomes what a named person watched.     | With the record.                                        |
| `InvitedUserIds`          | Yes. Server account identifiers of the people the link is for.                      | With the record.                                        |
| `PluginCreatedUserIds`    | Yes. The subset of those accounts this plugin made.                                 | With the record, and the account it names goes with it. |
| `CreatedByUserId`         | Yes. The operator who made the share.                                               | With the record.                                        |
| `CreatedAt`               | Only beside the invited accounts, where it becomes when they were invited.          | With the record.                                        |
| `ExpiresAt`               | No on its own. It is what starts the retention clock.                               | With the record.                                        |
| `RevokedAt`               | No on its own. Beside the invited accounts it is when their access was withdrawn.   | With the record.                                        |
| `RevocationReason`        | Possibly. Free text an operator writes, which can name anybody they choose to name. | With the record.                                        |
| `RevokedByUserId`         | Yes. The operator who revoked the share.                                            | With the record.                                        |
| `MaxBitrateBitsPerSecond` | No. A ceiling on a stream.                                                          | With the record.                                        |
| `TokenHash`               | No. A keyed hash of a value that was never about a person.                          | With the record.                                        |

Nothing here is a name, an address, a mail address or anything derived from a
password. The identifying fields are pointers into the server's own user table, so
this plugin holds a reference to a person rather than a second copy of their
details, and a copy of this store on its own does not say who anybody is. That is a
property worth keeping when the record shape next changes.

`RevocationReason` is the one field whose content nothing bounds. It is free text
for one operator to read later, it is never shown to a guest, and an operator who
writes a person's name into it has put that name in the store. It is listed as
possibly identifying rather than as not identifying for exactly that reason.

## The guest account is held too

The plugin creates the accounts it invites, which is decision 2 in #94 and is
written up in `docs/guest-accounts.md`. So there is a second thing in existence
because of a share: an account on the server, with whatever the operator or the
plugin put in its name.

That account is the server's data rather than this plugin's file, and its lifecycle
is `docs/guest-accounts.md` rather than this page. The one sentence that belongs
here is the join between them: the account is disabled when the last share naming
it ends, and deleted when the last record naming it is deleted, so its retention is
the retention below and not a separate answer.

## How long a record lives

An expired share record is kept ninety days and then deleted. The length is a
setting rather than a constant, which is decision 8 in #94, and ninety days is the
starting value rather than a principle. `docs/logging.md` already commits the
administrator audit trail to inheriting the same rule rather than having one of its
own, so there is one answer in this tree and not two.

What that means per case.

Revoked. The record stays, with `RevokedAt`, `RevocationReason` and
`RevokedByUserId` set. Revocation is immediate for access and is not a deletion: an
operator who revokes a share still needs to be able to say what they revoked, when,
and who pressed it. The retention clock runs from the end of the share either way.

Expired. The record stays until the sweep deletes it, ninety days after it ended.
Expiry is an instant rather than an event, which is `docs/expiry.md`, so nothing
happens to the file at that moment; what removes the record is the sweep.

The guest account deleted from the server. The record still names the identifier,
because the record is what says a share was made, and an identifier for an account
that no longer exists names nobody. What a share does when the account it names is
gone is #39.

The plugin uninstalled. The data folder goes, and every record with it, which is
`docs/plugin-lifecycle.md`. That is the one case where the retention rule is not
what removes the data.

## What removal actually means

Removing a share means the record is not in the file afterwards. The store rewrites
the whole file rather than editing it in place, and the write is a rename over the
destination, so there is no earlier version of the file left behind for the removed
record to survive in. A test asserts the file holds no identifier of the removed
share's guest afterwards, byte for byte, rather than asserting that a list in memory
no longer contains it.

What that does not reach is a copy somebody else made. A backup taken while the
share was live still holds it, and what restoring such a backup does to shares that
have since been revoked or expired is #40. This plugin deletes nothing outside its
own file.

## What is checked, and what is not

`PersonalDataTests` reads this page. The field table is compared against
`ShareRecord` in both directions, so a field added to the record without a row here
reds the suite, and so does a row for a field the record no longer carries. The
second direction is the one that survives a rename, and it is the direction a
hand-written list of personal data usually loses first.

Every row is required to carry a retention answer, because a row with an empty
answer is what the second clause of #31 is against and it is also what a
half-finished edit leaves behind.

The removal claim is executed rather than described. A store is written with two
shares, one of them naming a guest, that share is removed, and the file is then read
as text and asserted to hold neither the guest's identifier nor the share's.

What is not checked is the retention sweep, because there is no sweep. Ninety days
is a decision here and a number nothing yet reads; the setting it becomes is #71 and
the sweep that acts on it is #29. Until those land, this page states a rule the tree
does not enforce, and the guard above proves the list rather than the deletion.

The account half is not checked either. Nothing in this tree creates or deletes a
server account, so the join sentence above is a statement about
`docs/guest-accounts.md` and the routes that will implement it, not about code that
runs today.
