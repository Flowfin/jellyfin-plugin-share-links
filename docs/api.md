# The routes this plugin serves

This is the answer issue #72 asks for. Somebody will script against these routes,
and it is better that they script against something described than against
something they inferred from a browser's network tab.

Every route the plugin exposes is listed here. That is not a promise a reader has
to take on trust: the suite reads the compiled assembly, lists what it finds, and
compares that list with the table below, so a route added without a row here reds
the run, and a row here naming a route that does not exist reds it too.

    grep -n 'EveryRouteInTheAssemblyIsDescribedHereAndNothingElseIs' Jellyfin.Plugin.ShareLinks.Tests/ApiSurfaceTests.cs

## What stability this promises

None yet, and that is a statement rather than a hedge. No version has been
published, so nothing here has ever been depended on by an installed copy.

**The shape may change, and the version it changed in will be recorded.**
`docs/versioning.md` is where a version number gets its meaning, and a change to
a route's path, its inputs or the answers below is a change that appears in the
release it lands in. Until the first release there is nothing to record against,
which is what makes this the moment to script against these routes at your own
risk and not the moment to build on them.

## The routes

| Method | Path                                  | Reached by                          |
| ------ | ------------------------------------- | ----------------------------------- |
| GET    | `/ShareLinks/Guest/{token}`           | any caller the server has signed in |
| POST   | `/ShareLinks/Shares`                  | an administrator                    |
| GET    | `/ShareLinks/Shares`                  | an administrator                    |
| POST   | `/ShareLinks/Shares/{shareId}/Revoke` | an administrator                    |
| POST   | `/ShareLinks/Key/Rotate`              | an administrator                    |

Four routes. Creating a share also creates the accounts it is for, which is
decision 2 of #94 and the lifecycle in `docs/guest-accounts.md`, so the create
below changes something outside this plugin's own store and the section on it
says what that costs.

### GET /ShareLinks/Guest/{token}

The route a share link points at.

**Input.** One path segment, the token out of the link. Nothing else: no query
parameter, no header of this plugin's own, no body. A request that carries no
token does not match this route at all, which is `docs/leaked-link.md`'s reason
for putting the token in the path rather than in a query string.

**Who may call it.** A caller the server has already identified, under the
server's default policy. There is no anonymous access and it is not a setting.
An unauthenticated request is refused by the server before this plugin sees it,
so what a caller without a session receives is the server's answer and not one
described here.

**What it answers.**

| Case                                                 | Answer                                      |
| ---------------------------------------------------- | ------------------------------------------- |
| The token names a live share that invites the caller | `302` to the item in the web client         |
| Anything else                                        | `404`, no body, no headers of this plugin's |

Anything else is every other case and the flatness is deliberate. A share that
does not exist, a share the caller is not invited to, a revoked share, an expired
share, a plugin that has been disabled, a key or a store that cannot be read: all
of them answer with the same bytes, because a caller who can tell them apart can
map what exists on this server. Issue #26 is where that is argued and
`GuestRouteTests.EveryRefusalIsTheSameBytes` is what holds it.

The reason a share was refused is not on the wire and is not going to be. It
survives for the operator, and the surface that shows it is #67 and #70.

**Where the redirect points.** The item's address in the web client, under
whatever path the server is mounted at. **That address was not measured against a
running web client.** This plugin adds nothing to the client and no test here may
reach a server, so where the client shows an item is an assumption written in one
place in the source and marked as one. What is tested is that the address carries
the item the share names, carries the token nowhere, and keeps the path a reverse
proxy mounts the server under.

### POST /ShareLinks/Shares

Makes a share, and the accounts it is for.

**Input.** A body with four fields.

| Field            | Required | What it is                                                   |
| ---------------- | -------- | ------------------------------------------------------------ |
| `ItemId`         | yes      | The one library item the share is for                        |
| `GuestNames`     | yes      | The names the invited guests will be known by on this server |
| `ExpiresAt`      | no       | When the share stops, as an absolute instant                 |
| `MaxBitrateMbps` | no       | The ceiling for this share, in megabits per second           |

`GuestNames` is names and not account identifiers, because at the moment an
operator asks there are no accounts yet: this plugin makes one per name. A name
the server already holds is refused back rather than made unique with a number.

`ExpiresAt` absent takes `DefaultShareLifetimeDays` from the configuration, and
`MaxBitrateMbps` absent takes `DefaultMaxBitrateMbps`, which is itself allowed to
be no ceiling at all. An instant supplied in another offset is converted rather
than reinterpreted, and it has to be after now, because a share is live strictly
before its instant.

**Who may call it.** An administrator, under the server's own elevation policy.

**What it answers.**

| Case                                        | Answer                                           |
| ------------------------------------------- | ------------------------------------------------ |
| The share was made                          | `200`, the share, the link and the credentials   |
| The request or a bound refuses it           | `400` and a sentence naming the field or setting |
| The store could not be read or written      | `500`                                            |
| The configuration is outside its own bounds | `500` and a sentence naming the setting          |

**The link and the credentials are in this answer and in no other.** Only the
keyed hash of the token is written down, so this plugin cannot rebuild the link
when asked, and the credential is handed to the server and kept nowhere here. An
operator who loses the answer revokes the share and makes another one, which is
the same thing `docs/expiry.md` says about extending a link.

**What is refused, and what it costs.** Everything that can be decided without
changing anything is decided first: the fields above, the key, the link, the
ceilings on live shares and on lifetime read off the store, whether the item
exists, and whether each name is free. So an ordinary mistake costs nothing at
all, and no account is made for a request that was going to be refused.

One case is not orderable away. The authoritative ceiling check is inside the
store write, because a check outside it can be overtaken by a second
administrator creating at the same moment, so a create that loses that race has
already made its accounts. Those accounts are removed again, and this is the one
place this plugin deletes an account: only identifiers the server returned inside
that same call, only where no record names them. A removal that itself fails is
not reported as a success; the create is still refused and the accounts are left
in the server's own user list, hidden, under the names that were asked for.

**What it does not do.** It does not narrow which items a guest can see. The
account it makes carries the policy in `docs/guest-capabilities.md`, and
confining a guest to the one shared item is #52 and is not implemented, so a
guest account made today can reach the library its policy allows. That is the
largest single thing this route does not yet do and it is in `docs/limits.md`.

### GET /ShareLinks/Shares

Every share the store holds, with what each one is doing now.

**Input.** Nothing.

**Who may call it.** An administrator, under the server's own elevation policy.

**What it answers.**

| Case                        | Answer                                          |
| --------------------------- | ----------------------------------------------- |
| The store could be read     | `200` and one row per record                    |
| The store could not be read | `500`, no body, and a warning in the server log |

A row carries the share's identifier, the item, the invited accounts, who made it
and when, when it expires, what it is doing now, the ceilings in force, and the
revocation fields where something revoked it. It does not carry the token and it
does not carry the keyed hash of the token. The hash does not open a share, but it
is the value the resolution compares against, and a route handing it out is a
route handing out what an offline search needs.

The link is not there either, and cannot be. Only the keyed hash is written down,
so this plugin cannot produce a link a second time even when asked.

Every record is listed, including the ones that have stopped working. The state
column is what separates them, and it is read at the instant of the request
rather than stored:

| State     | What it means                                              |
| --------- | ---------------------------------------------------------- |
| `Live`    | The share resolves                                         |
| `Expired` | It reached its expiry instant while nothing had revoked it |
| `Revoked` | Somebody revoked it while it was still live                |

A share revoked after it had already expired reads as `Expired`, because expiry
is what stopped it. The revoker, the reason and the instant are still on the row.

`MaxBitrateBitsPerSecond` is the share's own ceiling, which is the number an
operator typed onto it. `AppliedCeilings` is what a guest of it would actually be
held to, one entry per invited account, because the ceiling is a per-account
question and a record names a list:

| Field    | What it carries                                                   |
| -------- | ----------------------------------------------------------------- |
| `UserId` | The invited account the entry is about                            |
| `Reach`  | What the request-path filter would decide for that account        |
| `Cap`    | `BitsPerSecond`, and `Applied` naming every ceiling sitting at it |

`Applied` is a set rather than one name. Two ceilings can sit at the same value
and both apply, and reporting one of them would mean an operator who lowers the
other sees the same number and concludes their change did nothing. Its members are
`Share`, `Account` and `ServerRemoteClientLimit`.

`Reach` is carried beside the number because there are two ways to have none.
`NotAGuestOfThisPlugin` is an invited account this plugin did not create: the
filter does not stand in front of it, so no ceiling of this plugin's reaches it at
all. `Reaches` with `BitsPerSecond` absent is an account the filter does cover and
for which nobody has set a ceiling anywhere. The two are repaired in opposite
directions.

The entries are computed at the instant the listing was read, in the same way the
state is. The ceiling a guest meets is worked out again when they ask to play
something, out of the same three values as they stand then, so a value somebody
moves in between is a disagreement between this answer and a later request rather
than a fault in either.

An unreadable store is an error here and a `404` on the guest route, and that
difference is deliberate. A fault told to a guest is a fault told to whoever holds
the link; an operator is the person who has to act on it, and an empty listing
handed to them reads as a server with no shares on it.

**The order.** The store's own. Sorting is a question about the page and not
about the route.

### POST /ShareLinks/Shares/{shareId}/Revoke

Stops a share.

**Input.** The share's identifier as a path segment, and optionally a body
carrying `Reason`, which is free text one operator writes for another to read
later. A body rather than a query parameter, because a query string ends up in
access logs and browser history and the reason is free text about a person.

**Who may call it.** An administrator, under the server's own elevation policy.

**What it answers.**

| Case                                          | Answer                                    |
| --------------------------------------------- | ----------------------------------------- |
| The store holds that share                    | `200` and the row as it stands afterwards |
| The store holds no share with that identifier | `404`                                     |
| The store could not be read or written        | `500`                                     |

Pressing it twice succeeds and changes nothing. A share that had already stopped,
by an earlier revocation or by its own expiry instant, keeps the instant, the
reason and the revoker it already had, because the first press is what stopped it
and the second press stopped nothing. The row that comes back is therefore the
first press's and not the caller's own.

A share this store does not hold is told apart from one it does, which the guest
route never does. It is right here for the same reason it is wrong there: an
operator who cannot tell a revocation that missed from one that worked will press
it again and believe the second press.

**What else it does.** It ends the server sessions of the guests this plugin made
for that share, where no live share still names them (#55). An invited account
this plugin did not create keeps its session. `docs/revocation.md` is where which
accounts those are, what a guest sees, and what the call does not reach are
written out.

**What it does not do.** It does not delete the record, and it does not reach a
segment request already in flight. The record survives so that an operator can
still see who was invited to what, which is `docs/personal-data.md`, and the
record is deleted by the retention rule instead.

### POST /ShareLinks/Key/Rotate

Replaces the install's keyed-hash key, and stops every share on the server.

**Input.** Nothing. There is no body and no parameter: the call does one thing
and the one thing is not configurable.

**Who may call it.** An administrator, under the server's own elevation policy.

**What it answers.**

| Case                                         | Answer                                      |
| -------------------------------------------- | ------------------------------------------- |
| The shares were stopped and the key replaced | `200`, the count and `Rotated`              |
| The shares were stopped and the key was not  | `500`, the count and `SharesStoppedKeyKept` |
| The store could not be read or written       | `500`, no body, and nothing was changed     |

The body of the first two carries `SharesStopped`, which is how many live shares
this call stopped, and `Outcome`, which is which of the two cases it was. The
count is the number an operator needs: every link that had been handed out has
stopped working and this is how many of them there were.

**Why the answer carries a state and not only a number.** A rotation is two
writes, to the records and to the key file, and the store's half cannot land
partly done: every live record is stopped in one act or none of them is. The pair
can. `SharesStoppedKeyKept` is that state and it says exactly what is true of the
server: the links no longer resolve, because the records refuse them, and the key
that may have leaked is still on disk. Pressing rotate again retries the key
write and stops nothing further, because nothing is left live to stop.

**Why the records are stopped first.** The other order leaves a store full of
records that read live and resolve for nobody, which is the reading the state
column exists to prevent, and it is what a failed second write would make
permanent.

**What else it does.** It disables the guest accounts this plugin made that no
live share names any more, and ends their server sessions, which is what revoking
each of those shares one at a time would have done. A rotation that stopped every
share and left every guest watching would behave differently from the revocation
it is a bulk form of.

**What it does not do.** It does not delete a record, it does not touch an
account this plugin did not create, and it cannot be undone. It also does not
cover a share created in the moment between the two writes: that share is issued
under the old key, is not among the records this call stopped, and stops
resolving anyway when the key lands. The store and the key file are two things
and no lock spans them.

## What this page does not cover

The answers a caller receives from the server rather than from this plugin. A
request with no session, a request the server rate limits, a request that never
reaches routing: none of those are this plugin's to describe, and describing them
here would be describing something this repository cannot read.

Rate limits, quotas and concurrency. Nothing here imposes any, and #56 is where a
ceiling on sessions is decided.
