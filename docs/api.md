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
| GET    | `/ShareLinks/Shares`                  | an administrator                    |
| POST   | `/ShareLinks/Shares/{shareId}/Revoke` | an administrator                    |

Three routes. The route that creates a share is #67 and does not exist, so
nothing about it is described here. Creating a share now also creates the account
it is for, which is decision 2 of #94 and the lifecycle in
`docs/guest-accounts.md`, and none of that is in the tree either.

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
and when, when it expires, what it is doing now, and the revocation fields where
something revoked it. It does not carry the token and it does not carry the keyed
hash of the token. The hash does not open a share, but it is the value the
resolution compares against, and a route handing it out is a route handing out
what an offline search needs.

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

**What it does not do.** It does not delete the record, and it does not touch a
session or a stream that is already playing. The record survives so that an
operator can still see who was invited to what, which is `docs/personal-data.md`,
and the record is deleted by the retention rule instead. Stopping a stream that is
playing is #55 and is not implemented.

## What this page does not cover

The answers a caller receives from the server rather than from this plugin. A
request with no session, a request the server rate limits, a request that never
reaches routing: none of those are this plugin's to describe, and describing them
here would be describing something this repository cannot read.

Rate limits, quotas and concurrency. Nothing here imposes any, and #56 is where a
ceiling on sessions is decided.
