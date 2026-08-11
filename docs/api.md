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

| Method | Path                        | Reached by                          |
| ------ | --------------------------- | ----------------------------------- |
| GET    | `/ShareLinks/Guest/{token}` | any caller the server has signed in |

One route. The administrator routes for creating, listing and revoking a share
are #67 and do not exist, so nothing about them is described here.

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

## What this page does not cover

The answers a caller receives from the server rather than from this plugin. A
request with no session, a request the server rate limits, a request that never
reaches routing: none of those are this plugin's to describe, and describing them
here would be describing something this repository cannot read.

Rate limits, quotas and concurrency. Nothing here imposes any, and #56 is where a
ceiling on sessions is decided.
