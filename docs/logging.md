# What is logged, and what is never logged

This is the policy issue #27 asks for. It is written before the routines it binds,
because a log line is decided at the moment somebody adds one, and the moment
somebody adds one is a bad time to be deciding.

Two failures are being guarded against and they are different. A raw token in a
log file turns a leaked log into a working link, which is a credential leak. A
line naming a guest and a title is a record of who watched what, which is not a
credential and is still an exposure, held by an operator running a server for
people they know.

## The never list

These never appear in a log line, at any level, in any form, including inside an
exception message, a stack trace this plugin writes, or a URL it logs.

- The raw token, whole or in part. A prefix of a token is a partial credential
  and it narrows a search, so truncation is not a way to log one.
- The keyed hash secret from #28, and anything from which it could be
  reconstructed.
- The keyed hash of a token. It is not a link and cannot be turned into one, but
  it is the store's lookup key, so a log holding it lets a leaked log be joined
  to a leaked store.

Nothing derived from a password is on this list, because there is nothing to put
there: an invited guest signs in to the server, and the server owns that
exchange. This plugin never sees a password.

The never list is not a level. Debug is not an exception to it, because a debug
line is a line in the same file.

## How a share is named in a line

By its record identifier, whole.

The record carries an identifier that is neither the token nor derived from it;
#33 is where that field is defined, and it exists so that a share has a name
outside the link. Support is a conversation about one share, and the identifier is
what an operator revokes by and what an administrator view lists, so a line
carrying it can be matched to the thing an operator can act on.

#27 proposed a short prefix of it instead. This says the whole identifier, for two
reasons. It is not usable: it makes no link, and possessing it grants nothing that
the administrator view does not already grant to somebody who can reach it. And a
prefix costs the line the one thing it is for, because two identifiers sharing a
prefix make a support conversation ambiguous exactly when there are enough shares
for the conversation to be hard.

Where a token did not resolve to any share there is no identifier, and the line
says that a token did not resolve and nothing else. It does not carry the token to
make up for it.

## What is recorded, and where

Information, the level an operator reads without asking for it:

- A share is created. The share identifier, the item identifier, the expiry
  instant, and how many accounts are invited.
- A share is revoked. The share identifier, and whether it was already revoked,
  since revocation is idempotent (#46) and the second one is not an error.
- A share resolves. The share identifier.
- A share refuses. The share identifier where one was found, and the reason as a
  fixed code rather than a sentence assembled from the request.

The invited accounts are not named in any of those lines. The association between
a person and a title is what the administrator view holds and what #31 accounts
for; a log line repeating it makes a second copy of it with a different lifetime
and a different reader.

Debug, which an operator turns on to answer a question and turns off again: counts,
timings, store file operations and the decisions inside resolution. The never list
still holds.

Warning and above: the states an operator has to act on. A store write that
failed, a key that could not be read, a record that could not be parsed. These
carry paths and reasons, never contents.

## The audit trail is a log too

The administrator-visible trail holds the same create, revoke and expiry events,
keyed by share identifier. It never holds a token, and it inherits the retention
rule for expired records rather than having one of its own: expired records are
kept ninety days and then deleted, and the retention length is a setting.

## What refuses a violation of this

The greppable invariant lint refuses a logging call that takes a token or a secret
as a value, and it bites for the reason it names:

    bash .github/scripts/enforce-greppable-invariants.sh .github/invariant-fixtures/token-not-logged/violation ; echo "exit=$?"
    exit=1

    INVARIANT_SKIP=token-not-logged bash .github/scripts/enforce-greppable-invariants.sh .github/invariant-fixtures/token-not-logged/violation ; echo "exit=$?"
    exit=0

    bash .github/scripts/enforce-greppable-invariants.sh .github/invariant-fixtures/token-not-logged/near-miss ; echo "exit=$?"
    exit=0

The violation is refused; with that one invariant removed the same bytes pass, so
the refusal is that invariant's rather than a neighbour's; and a line that talks
about tokens without carrying one is not refused.

What it reads is source text. A token that reaches a logging call under a name
carrying none of the words the pattern looks for walks straight through it. That
is not a hole to be closed by widening the pattern, which would start refusing
names that carry nothing; it is the reason #27 also asks for a test that drives
the create, resolve, refuse and revoke paths with a capturing logger and asserts
that no emitted line contains the raw token. That test is not written, because
those four paths do not exist yet. Until it is, the never list is enforced against
the ordinary spelling and not against a renamed value.

Nothing enforces the rest of this document. Which level a line is written at, what
fields it carries and whether the invited accounts stayed out of it are read by a
person, in review.

## What this does not settle

The server's own activity log, and whether a guest's playback appears in it, is
#59. This document is about the lines this plugin writes.

Log files belong to the server. Their location, their rotation and how long they
survive are the server's, and this plugin deletes none of them, so the retention
sentence above is about the plugin's audit trail and not about a log file.
