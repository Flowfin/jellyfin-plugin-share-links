# Limits and the known awkward cases

This is the collection issue #86 asks for. Everything here is behaviour that reads
as a defect until somebody explains it, gathered in one place so an operator meets
it before it surprises them rather than afterwards.

Every entry is an answer and not an open question. Where a limit was decided in its
own document, that document is the authority and is named. This page is where the
limits are read together, by somebody who does not know which milestone decided
which one, and it repeats no argument those pages already make.

Each entry carries the issue the limit comes from and a line beginning **What an
operator does.** A limit with no such line is one somebody is expected to work out
for themselves, which is the failure this page exists against, so `LimitsTests`
refuses one.

## The state of the plugin

### No version has been published

Nothing is released and no manifest is served, so there is nothing to install from
a repository URL. A package built from the tree carries `0.0.0.0`, which
`docs/versioning.md` reserves for exactly that case. The release process is #89,
the manifest is #90, the first tag is #136.

**What an operator does.** Waits. A build from the tree loads and names itself, and
it is not a copy anybody should hand a guest a link from.

### The feature is not finished, and what is missing is not obvious from the outside

The tree holds the share record, the store, the token model, the resolution
decision and the guest route. It holds no administrator route, so nothing creates,
lists or revokes a share except a test, and no page shows one. Those are #67 and
#70.

**What an operator does.** Reads this page as the design and the open issues as the
state. Nothing installed today shares anything.

### Nothing creates, changes or removes a guest account

`docs/guest-accounts.md` decides under #51 that this plugin creates the account
with the invitation and removes it when the last record naming it goes, and
`docs/guest-capabilities.md` lists under #57 the switches it sets on that account.
No code in this tree does either yet.

**What an operator does.** Reads every capability in those two pages as a decision
rather than as a setting in force, and does not assume an account a record names
has been narrowed by anything.

### Nothing confines a guest to the shared item

`docs/guest-confinement.md` chooses, under #52, an authorization filter of this
plugin's own over the account's allowed tags, and says plainly that the filter does
not exist. Until it does, an account invited to a share reaches whatever the server
already lets it reach.

**What an operator does.** Does not use a share as the confinement. An account that
should see one item is one the operator narrows themselves, through the server's
own library permissions, until that filter lands.

### Another plugin's routes are outside anything this plugin can refuse

#47's list of what a token can never reach ends on a line this plugin cannot hold:
what a guest's account may reach on a server whose other plugins this one does not
know about. It is not a test that can be written here, because there is no other
plugin in the suite to point at and adding one would test an installation rather
than this code. #47 names it as a documented statement with the reason it has none,
and this is that statement.

**What an operator does.** Reads a share as a bound on what this plugin serves and
not as a bound on the server. Where another plugin exposes something a guest
account should not reach, that plugin's own permissions are what keep them out.

### Nothing enforces a bitrate ceiling

The record carries a ceiling and `EffectiveBitrate` takes the lowest of the three
that can apply, but nothing writes a number onto an account or in front of a
stream. `docs/bitrate-cap.md` is the decision under #61 that the enforcement will
be built against, and #64 is the arithmetic that is already there.

**What an operator does.** Bounds the uplink outside this plugin where that cost
matters, and reads a ceiling on a share as a number nothing yet acts on.

## The item a share names

### A share whose item is gone still resolves

Nothing asks the server whether the item is still there, so a share whose item a
library scan removed sends the guest to an address naming nothing. `docs/gone.md`
is #39's decision, and the answer there is a refusal once the lookups exist.

**What an operator does.** Revokes the share when the item goes. The plugin does
not notice on its own.

### An item that comes back is not re-bound

An item restored under a new identifier is a different item to a record naming the
old one, and nothing matches it back by path or by provider id. That is deliberate
in #39: a wrong guess is a live share pointing at something the operator never
meant, which is a disclosure rather than an inconvenience.

**What an operator does.** Creates a new share. The old one stays refused.

### A disabled account and a deleted account are not the same

Both stop a share resolving, because the decision takes the account the server
identified and neither can sign in. Only one is reversible: an account enabled
again resolves its share again, while an account created later with the same name
is a different account with a different identifier, so the invitation does not
transfer to it. `docs/invitation-and-expiry.md` is #54's row by row answer.

**What an operator does.** Disables rather than deletes, where the intent is to
pause somebody.

## The link

### A guest who is not signed in has to open the link twice

The route answers only callers the server has already identified, so a guest
following a link before signing in meets the server's own refusal, signs in, and
reaches the item by opening the link a second time. Making that one round trip
needs something in the web client, and decision 4 of #94 is that nothing is added
to the web client. #68 is where the remaining choice sits, and `docs/leaked-link.md`
is where the shape of the link itself is argued.

**What an operator does.** Tells the guest to sign in first, or to open the link
again afterwards.

### Every refusal looks identical

A token naming no share, a share the caller is not invited to, a revoked share, an
expired share and a caller the server has not identified all produce the same
bytes. That is #26's property and it is deliberate, because a caller who could tell
them apart could ask this server which of their tokens is real.

**What an operator does.** Does not diagnose from what the guest sees. The specific
reason belongs in the administrator view, #67 and #70, and in the log lines
`docs/logging.md` commits to under #27.

### Nothing extends a link

An expiry is an instant on the record and no route moves it, which `docs/expiry.md`
argues under #45. Extending is issuing.

**What an operator does.** Creates a new share for somebody who should have access
again, and accepts that the old link stays dead.

### A link built with no public address is built from what the caller claimed

`PublicBaseUrl` is the address a link is built on, and #49 is why it exists. Left
empty, the link falls back to what the request said the host was, which is text a
caller supplies, so a forged header produces a link pointing somewhere else. Set to
something that is not an absolute `http` or `https` URL, no link is built at all
and the request is not used instead.

**What an operator does.** Sets `PublicBaseUrl` before creating a share.
`docs/configuration.md` carries the row and the bounds.

### A clock that moves backwards brings expired shares back

Expiry is compared against the clock on every request, so a backwards step larger
than the time since a share expired makes that share live again. #45 accepts it
rather than calling it impossible, and the bound is the sweep: a record retention
has already removed does not come back, and a revocation is a recorded state that
no clock movement undoes. #79 is where both halves are asserted.

**What an operator does.** Revokes rather than waiting on an expiry wherever it
matters, because a revocation survives a clock that an expiry does not.

## The guest

### The session ceiling belongs to the account, not to the share

`GuestMaxActiveSessions` is written onto the account, so a guest invited to two
shares carries one allowance across both rather than one each. Inviting the same
person twice does not give them a second set of devices, which is #56's answer in
`docs/guest-capabilities.md`.

**What an operator does.** Sizes the number for the person rather than for the
share.

### A switch this plugin sets is not one anything here proves is honoured

The server enforces an account policy. Nothing in this plugin re-checks a
capability when a request arrives, and no test here can watch a server obey one, so
the rows #57 records are values this plugin asks for.

**What an operator does.** Verifies anything that matters on their own server
rather than reading the list as a guarantee.

### Sharing with an account somebody already uses is not offered

This plugin writes a policy onto an account it created and onto no other, so it
never narrows an existing account for a share and puts it back afterwards. #58
argues that in `docs/account-restoration.md`, and `ShareRecord.WasCreatedByThisPlugin`
from #144 is the gate.

**What an operator does.** Lets the plugin make the guest account. An account
belonging to a person who uses it for something else is not a share target.

### An operator can see what a guest watched

Playback progress is recorded against the guest's account like anybody else's, so
the live session, the item afterwards and the server's activity log all show it.
No setting in this plugin changes that, which is #59's answer.

**What an operator does.** Tells the guest, where that matters.
`docs/playback-visibility.md` lists which surface shows what.

### Revoking does not stop a stream that is already playing

Revocation is read when a request is made. A session already playing is not a
request, and stopping one is #55.

**What an operator does.** Revokes the share and then stops the session from the
server's own dashboard, where the stream has to end now.

## Files, backups and the key

### Restoring a backup brings revoked shares back to life

Revocation is a field on the record, so a share revoked after a backup was taken
comes back without that field and a link somebody was told to stop using works
again. A share deleted after the backup comes back too. Nothing in the plugin can
notice, because a restored store is a valid store. #40 is where this is worked
through.

**What an operator does.** Revokes again whatever was revoked between the backup
and the restore. Where that list is not known, rotates the key, which stops every
link at once and answers with how many live shares it stopped. Both routes are in
`docs/backup-restore.md`.

### The store and the key are one unit

Every hash in the store was computed under the key in the key file. Restore one
without the other and every token in that store is refused exactly as a token
naming nothing is refused. The plugin never replaces a key file it managed to read,
which is #28's rule, so the records sit intact waiting for the key that matches
them.

**What an operator does.** Backs up and restores both files together. Where the
matching key is genuinely gone the shares have to be created again, and the records
still say who was invited to what.

### Disabling the plugin stops nothing, and uninstalling leaves the data

#38 is what each of the two actually does, in `docs/plugin-lifecycle.md`, and what
is left on disk afterwards.

**What an operator does.** Revokes the shares that should stop rather than
disabling the plugin and assuming they have, and removes the plugin's data folder
by hand where the data should go with it.

### A server nobody creates shares on never sweeps

Retention runs on the way to a write rather than on a timer, so records that have
stopped working are deleted when the next share is created and not before. Nothing
bounds the rate either: a script creating and revoking in a loop frees a live place
every time and leaves a record behind for the whole retention window. Both are
#29's, in `docs/bounds.md`.

**What an operator does.** Reads `ExpiredShareRetentionDays` as a ceiling on a busy
server rather than as a promise on a quiet one.

### Removing a share does not reach a copy somebody else made

The store rewrites the whole file and the write is a rename over the destination,
so a removed record leaves no earlier version of the file behind. What that does
not reach is a backup taken while the share was live, and this plugin deletes
nothing outside its own file. #31 is where what is held about a guest, and for how
long, is written down in `docs/personal-data.md`.

**What an operator does.** Treats their own backups as the second place a guest's
data lives, and prunes them on whatever schedule they already keep.

### A share created before an upgrade can carry less than a share created after it

The record is versioned and a field added later is absent from a record written
earlier. Silence is read the safe way each time, which for #144's provenance field
means an older record claims to have created none of its invited accounts, so the
plugin will not remove an account it cannot prove it made. #37 is the migration
path that carries records forward.

**What an operator does.** Expects guest accounts invited before an upgrade to
outlive their shares, and removes those by hand.

### What permissions these files carry on Windows was never measured

The plugin can set a POSIX mode on a file it creates. What a Windows server gives
it by inheritance was not measured and no claim about it is made in either
direction. #28 and #34 both record the measurement as owed.

**What an operator does.** Checks the permissions on the plugin's data folder
themselves, on Windows. `docs/share-key.md` and `docs/share-store.md` are where it
is written down.

## What the design does not defend against

### A guest who may watch can pass on what they watched

They can hand their own credentials to somebody else, or point a camera at the
screen, and no token model prevents either. Reuse is not observable here either:
presenting a token again is allowed and nothing compares addresses, which is #25's
residual.

**What an operator does.** Shares with people they would lend the media to anyway,
and bounds the blast radius with the session ceiling rather than with detection.

### The operator is trusted completely

They can read every file the server can read, including the store and the key, and
can create a share for anything. So can anybody able to read the server's
filesystem as the server's own user. The keyed hash buys one thing, which #23
states: a copy of the store alone is not enough.

**What an operator does.** Treats the plugin's data folder as key material.

### Transport security belongs to the deployment

If the link travels over plain HTTP the token is on the wire. This plugin does not
check that it is served over TLS and does not refuse to run when it is not, which
#23 accepts rather than overlooks.

**What an operator does.** Serves the server over HTTPS before handing anybody a
link. `docs/threat-model.md` holds the rest of what is accepted and why.

## A limit this plugin does not have

### The cast and crew defect under a tag-restricted account is not inherited

Issue #86 asks for this one by name and the answer is that it is not a limit here.
Confining a guest through the account's `AllowedTags` inherits
jellyfin/jellyfin#14926, where a tag-restricted user loses the cast and crew
section entirely. That mechanism was compared and refused under #52, for reasons of
its own, in `docs/guest-confinement.md`.

**What an operator does.** Nothing. It is written down so a reader who has met the
upstream defect elsewhere does not go looking for it here.

## Scripting against the routes

### The route shape promises nothing yet

`docs/api.md` describes every route the plugin serves and #72 is explicit that it
promises no stability: no version has been published, so nothing here has ever been
depended on by an installed copy, and a change to a path, an input or an answer
appears in the release it lands in.

**What an operator does.** Scripts against these routes at their own risk until
there is a release to record a change against.

## The server line, and the bound on every claim above

### The package declares 10.11 and nothing else

`build.yaml` offers `10.11.0.0` as the line it was built for, and whether a
particular server accepts it is the server's decision rather than this plugin's.
Carrying 12.0 beside it is #181.

**What an operator does.** Installs on 10.11. A server on another line is outside
what this package claims.

### Anything about a running server on this page is argued rather than measured

The suite needs no server, no network, no display, no elevated rights and no media
file, and `.github/workflows/headless.yml` proves that rather than asserting it, so
every statement here about what a server does with a policy, a ceiling or a request
is read out of the API this plugin compiles against. #74 is where that rule and its
proof live.

**What an operator does.** Confirms on their own server anything they are relying
on. `docs/testing.md` is the rule and the two clauses no run of it can prove.

## What this page draws no limit from

These documents under `docs/` contribute no entry, and they are listed so that a
document arriving with a limit nobody collected is a red suite rather than a silent
gap.

- `docs/catalogue-checklist.md`, the catalogue's requirements against this
  repository, whose refusals are decisions rather than limits an operator meets.
- `docs/parity-ledger.md`, the gate compared against the sibling repository's.
- `docs/refused-tests.md`, the tests this repository declines to write and what
  stands in for each, which is a bound on the suite rather than on what an
  operator can do. Where a refusal there does reach an operator it is already an
  entry above, under what revoking does not stop and under what was never
  measured.
- `docs/RELEASING.md`, how a release is cut.
- `docs/limits.md`, this page.

## What is checked, and what is not

`LimitsTests` reads this file. It requires every entry to carry an operator line
and an issue reference, requires every `docs/` path named here to be a file that
exists, and requires every document under `docs/` to be named somewhere here,
whether by an entry or in the list above. So a document added with a limit nobody
collected reds the suite, and so does a path this page names after the file behind
it is renamed.

What it does not read is issues. The second clause of #86 asks that every limit
named in an earlier milestone's issue appears here, and the substitution this page
makes is that those issues' answers landed as documents, each naming its issue.
That substitution is a claim and not a measurement. It holds for a limit that was
written down; a limit named in an issue and never written into any document is
outside what the test can see, and the review is where that is caught.
