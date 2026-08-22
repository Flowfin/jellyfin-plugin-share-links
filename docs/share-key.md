# The key the stored hashes are computed with

The store holds a keyed hash of each token rather than the token itself, which is
`docs/share-store.md` and #43. That means there is a key, and a key has a
lifecycle nobody thinks about until it is missing. This is the answer #28 asks
for.

## Where it lives

One file of its own, under the plugin's data folder, beside the store and not
inside it.

The name is `share-key` and no absolute path is written here. The folder is the
server's to decide, and a path written down that is wrong on somebody's install
is worse than the property it stands for, which is that everything this plugin
keeps is inside one folder and nothing of it is anywhere else. That property is
`docs/plugin-lifecycle.md`'s and this file does not weaken it.

Three places it is deliberately not.

Not the configuration file. That is the file an operator is invited to edit by
hand, the file the configuration page rewrites in full, and the file that ends up
in support pastes. A key there is a key in every copy of all of that, and one
careless save away from being replaced.

Not the store. A store and a key in one file is a single copy that resolves every
share in it, which is the whole reason the store holds hashes.

Not memory only. A key regenerated at every start invalidates every live share at
every restart, which is expiry by accident.

## Permissions

On a platform with POSIX modes the file is set to owner read and write, `0600`,
by the plugin at the moment it writes it, rather than left to whatever the
process umask happens to be.

**On Windows nothing is set and nothing is claimed.** The file inherits the data
folder's access control, and what that comes to on a Windows server was not
measured. `docs/share-store.md` deferred exactly that measurement to this page and
it is still owed. Measuring it needs a file created by the same call on that
platform and the resulting access control read back, and no test in this
repository may reach a running server, which is `docs/testing.md`. No claim is
made in either direction, and this plugin does not promise a permission it has not
seen.

## First run, and every run after it

The first read writes a key. There is nothing to lose at that point, because a
key that has never existed has hashed nothing.

Every later failure fails closed. A file that is there and cannot be read, or that
does not hold a key of the width this plugin writes, produces a refusal and not a
fresh key.

That is the part worth being explicit about, because the tempting alternative is
worse than it looks. A plugin that quietly writes a new key when it cannot read
the old one has invalidated every live share on the server. The outcome is safe.
The silence is not: the operator has links in the world that have stopped working
and nothing has told them why. So the key is not replaced, resolution refuses, and
the refusal names the path.

What the guest sees is nothing, and nothing different from any other refusal. A
caller is never told that the server has a key problem, which is #26.

## Rotation

Rotation writes a new key over the old one, and it is destructive by design: every
hash in the store was computed under the key that has just been replaced, so every
link that was handed out has stopped working.

It is therefore not a maintenance step with no consequence, and the call says so
rather than returning nothing. `ShareKeyFile.Rotate` answers with how many live
shares it just stopped, taken at the moment the key changed, because that is the
number the operator needs and the only moment it means anything.

Rotation is what to reach for when the key itself may have leaked: a copy of the
data folder in the wrong hands, a backup restored somewhere it should not have
been, or a support bundle that turned out to include it. Revoking one share is #46
and is the control for a link that went to the wrong person.

Where an operator presses it is `POST /ShareLinks/Key/Rotate`, and where they are
shown the number is the configuration page, which says how many shares are live
before the press and how many stopped after it (#243). `docs/api.md` is where the
route's answers are written out.

A rotation is two writes and they are made in one order. Every live record is
stopped first, in one act, and the key is replaced after that. The other order
would leave a store full of records that read live and resolve for nobody, which
is the reading `ShareState` exists to prevent, and a failure between the two
writes would make it permanent. Stopping the records first fails the other way:
the shares are stopped, which is what the operator asked for, and the key that
may have leaked is still on disk. That state has a name of its own,
`SharesStoppedKeyKept`, and pressing rotate again retries the write that failed.

Stopping the records is not decoration on top of replacing the key. Which guest
has nothing left to watch is answered from whether a live record still names
them, so a rotation that changed no record would end no session and disable no
account, and it would behave differently from revoking those same shares one at a
time. It does neither: the guests of every stopped share are signed out and
disabled, exactly as `docs/revocation.md` describes for one share.

What the two writes do not cover is a share created between them. It is issued
under the old key, is not among the records the rotation stopped, and stops
resolving anyway when the key lands, so it is the one record a rotation can leave
reading live and resolving for nobody. The store and the key file are two things
and no lock spans them. This is stated rather than defended against.

## What this does not settle

What a restored backup does when the store and the key have moved apart is
`docs/backup-restore.md`. This page fixes that a key is never silently replaced,
which is the half that page takes from here; the operator guidance across a
restore is there.

Nothing here is measured against a running server. The permissions statement above
is the one measurement this page owes and does not make, and it says so where a
reader meets it rather than in a footnote.
