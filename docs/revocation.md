# What revoking a share stops

## Why this page exists

Revocation that only affects the next click is not revocation (#55). A guest who
is watching holds a session the server signed in, and that session goes on working
after the record says the share has stopped, because nothing about the record
reaches the session. This page is what the revoke route does about that, what a
guest sees when it happens, and what it does not reach.

`docs/api.md` is the route itself. `docs/bounds.md` is what live means and #46 is
where the record half of revocation is argued.

## The two halves of a revocation

The record is written first. `POST /ShareLinks/Shares/{shareId}/Revoke` stamps the
instant, the operator and the reason onto the record, and from that moment the
resolution decision refuses the token, because it reads `RevokedAt` on every
request. That is the half that stops the link being opened again.

The sessions are ended second, and never first. A revocation that signed a guest
out and was then refused by the store would have stopped somebody watching a share
that is still live, and this plugin has nothing to sign them back in with. So the
order is: write, then read the store back, then ask the server to end what is left
with nothing to watch.

## Whose sessions end

Only the accounts this plugin created for the share, and only where no live share
still names them.

An invited account this plugin did not create is left alone. It belongs to
somebody who uses this server for their own watching, and signing that person out
is a change to a person rather than to a share. `PluginCreatedUserIds` is the
provenance that separates the two, which is #144, and it is the same line
`docs/guest-accounts.md` draws before it will delete anything.

A guest who still holds another live share keeps watching it. Revoking one share
is not a reason to end somebody's other stream, so the accounts are compared
against every record the store holds after the write, and live is
`ShareBounds.IsLive`'s answer rather than a second comparison over the same two
fields.

What is asked of the server is `ISessionManager.RevokeUserTokens` for each such
account, with nothing spared. Every token the account holds, rather than a session
or a device this plugin picked out: a guest may have opened the link on two
devices, this plugin records neither, and every token under an account it created
belongs to the share that created the account.

## What the guest sees

The client is signed out. The next thing it asks the server for is refused because
the token it holds has been revoked, and what it shows is its own signed-out or
connection-lost state rather than a message from this plugin.

A message was considered and is not sent. `ISessionManager.SendMessageCommand`
exists on the line this plugin compiles against and would put a sentence on the
screen before the sign-out, and two things stop it. It is addressed from a
controlling session, and this plugin holds the caller's account rather than the
caller's session, so there is no identifier to send it from. And whether a client
renders such a message is the client's, which no test in this repository can
observe and no run here has observed.

So the honest statement is the one the issue asked to be avoided rather than the
one it preferred: the guest sees the client's own handling of a session that has
ended, and not a sentence this plugin wrote. That is a residual and it is recorded
here rather than described as a message that exists.

## What this does not reach

A segment request already in flight. The handle belongs to the server, nothing in
this plugin stands in the playback path, and the guest route answers by redirecting
to the item's own address rather than by proxying it. So a client that has already
been given a segment address is refused by the server the next time it presents the
revoked token, and not by anything here. `docs/refused-tests.md` is where the test
that would watch the handle itself is refused with the reason.

A transcode the session owns is not killed by name. `ITranscodeManager` is not
referenced by this plugin, so what happens to a running transcode when its
session's tokens are revoked is the server's own behaviour. Nothing here measured
it and nothing here claims it.

An account this plugin did not create keeps its session, as above. An operator who
shared with an account they made themselves and wants that person signed out does
it from the server's own dashboard.

## What is measured and what is not

Measured, in this repository's suite: which accounts the revoke route asks the
session manager about, that it asks about no others, that a guest holding another
live share is not among them, that a guest whose other share has expired is, that
nothing is spared on the ask, and that a revocation that found no share asks about
nobody. `AdministratorRouteTests` and `GuestSessionsTests` carry those.

Not measured: everything past the ask. That `RevokeUserTokens` ends the sessions,
that the server then refuses the next segment, and what a client puts on the screen
are all statements about a running server. The reading of what the member does is
taken from its name and its documentation on the packages this project compiles
against, and not from the server's implementation, which is not in this tree and
was not read. No run against a server was made for this page.
