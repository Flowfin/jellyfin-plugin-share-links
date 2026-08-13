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
- The title of the item a share names. A title is not a credential and it is the
  other half of who watched what, which is the second failure this document
  opens with. The operator sees titles in the share view, which is state behind
  the elevation policy; a log file is copied by backup tooling and read by
  whoever can read the server's disk.

Nothing derived from a password is on this list, because there is nothing to put
there: an invited guest signs in to the server, and the server owns that
exchange. This plugin never sees a password.

The never list is not a level. Debug is not an exception to it, because a debug
line is a line in the same file.

## How a share is named in a line

By the first eight characters of its record identifier. `ShareLog.Name` is the
one routine that makes that name and every line goes through it.

The record carries an identifier that is neither the token nor derived from it;
#33 is where that field is defined, and it exists so that a share has a name
outside the link. Support is a conversation about one share, and the identifier is
what an operator revokes by and what an administrator view lists, so a line
carrying a prefix of it can be matched to the thing an operator can act on: the
prefix is written the same way the view writes the whole, so searching for one
finds the other.

An earlier version of this document argued for the whole identifier, on the
ground that a prefix costs the line the one thing it is for when two identifiers
share one. That cost is real and it is small enough to pay here. Eight
hexadecimal characters are thirty-two bits, and the ceiling on records the store
will hold is a setting whose default is a hundred live shares plus ninety days of
expired ones:

    grep -n 'DefaultMaxLiveShares =\|DefaultExpiredShareRetentionDays =' Jellyfin.Plugin.ShareLinks/ShareBounds.cs
    50:    public const int DefaultMaxLiveShares = 100;
    65:    public const int DefaultExpiredShareRetentionDays = 90;

At a thousand records the chance that any two of them share a prefix is about
`1000 * 1000 / (2 * 2**32)`, which is one in eight and a half thousand. That is
an arithmetic result rather than a measurement, and the operator who meets the
one case has the whole identifier in the share view to tell the two apart. What
the prefix buys is that a log line is not a second copy of the identifier an
operator revokes by, which is the direction this document errs in everywhere
else.

Where a token did not resolve, the line says that a token did not resolve and the
reason, and it names no share at all. That is narrower than "the identifier where
one was found", which is what this section used to promise, and the reason is in
the decision rather than in the logging: `ShareResolutionResult` carries a share
or a refusal and never both, so on the refusing path there is no record to take
an identifier from even where the token matched one. Getting the identifier back
would mean the decision handing out the record it refused, and a caller holding a
record it was refused is the shape #26 exists against. What it costs an operator
is that a refused token cannot be tied to a share from the log alone; the share
view is where that question is answered.

The line never carries the token to make up for any of this.

## What is recorded, and where

Information, the level an operator reads without asking for it:

- A share is created. The share name, the item identifier, the expiry instant,
  and how many accounts are invited.
- A revocation is asked for. The share name, and which of three things the store
  did: the share was revoked, it had already stopped, or no record carries that
  identifier. Revocation is idempotent (#46) and the second press is not an
  error, so it is a line rather than a silence.
- A share resolves. The share name.
- A share refuses. The reason, as the fixed code the decision produced rather
  than a sentence assembled from the request, and no share name, for the reason
  the section above gives.

The invited accounts are not named in any of those lines. The association between
a person and a title is what the administrator view holds and what
`docs/personal-data.md` accounts
for; a log line repeating it makes a second copy of it with a different lifetime
and a different reader.

Debug, which an operator turns on to answer a question and turns off again: counts,
timings, store file operations and the decisions inside resolution. The never list
still holds.

Warning and above: the states an operator has to act on. A store write that
failed, a key that could not be read, a record that could not be parsed. These
carry paths and reasons, never contents.

A store the guest route could not read is one of those, and it is written as a
warning rather than as one more refused token. The caller is told the same
nothing either way, which is #26; an operator reading a log needs the two apart,
because a store nobody can read has stopped every share on the server.

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
that no emitted line contains the raw token.

`Jellyfin.Plugin.ShareLinks.Tests/LoggingTests.cs` is that test. It drives the
four paths through the routines that perform them, and it asserts the never list
twice over. As an absence: the token, the keyed hash and every account
identifier appear in no line. And as a whitelist over the placeholder names a
line may carry, which is the half that does not need the forbidden thing to be
named first. An item title added to a line reddens the whitelist without anybody
having to think of titles, which is what makes that clause of the never list
enforced rather than reviewed.

The whitelist is not the whole of this document either. Which level a line is
written at is asserted only for the unreadable store; whether a value allowed by
name carries something it should not, a share name that is really a token
renamed, is read by a person, in review.

## What this does not settle

The server's own activity log, and whether a guest's playback appears in it, is
`docs/playback-visibility.md`. This document is about the lines this plugin
writes.

Log files belong to the server. Their location, their rotation and how long they
survive are the server's, and this plugin deletes none of them, so the retention
sentence above is about the plugin's audit trail and not about a log file.
