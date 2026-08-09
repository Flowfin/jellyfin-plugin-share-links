# Threat model

This is the file issue #23 asks for. The plugin adds a route to a media server
that anybody on the network can reach, and the thing it hands an operator is text
that travels through channels nobody here controls. That earns a document rather
than a paragraph in the readme.

Read `README.md` first for what the feature is. Read this for what can go wrong
with it and what answers each one.

## What is in the tree while this is being written

Almost none of it. The plugin's identity, its dashboard page and the routine that
mints a token are the whole of the code today:

    git grep -lE 'ApiController|ControllerBase|HttpGet|HttpPost' -- 'Jellyfin.Plugin.ShareLinks/*.cs' ; echo "exit=$?"
    exit=1

There is no route, no share record and no store. So most rows below name an issue
in the proof column rather than a test, and that is the honest state rather than a
gap in the table. A row naming an issue is a control that is owed. A row naming a
test is a control that runs. The two are different words on purpose, and the
document is worth less than it looks if a reader reads them as the same.

## Assets

The media itself. What a share exists to hand out, one item at a time, and the
only asset here whose loss cannot be undone by revoking anything. Once a guest has
watched, they have watched.

The share records. Each one says which item, which invited accounts, which window
in time and which ceiling. Read them and you learn who was invited to what. Write
them and you have granted yourself a share. Delete them and every live share is
gone at once.

The keyed hash secret. The one value that turns a token into the thing the store
compares against. It is the asset whose loss is worst, because it is the asset
that makes a stolen store useful.

The guest's session. What the guest gets after signing in, and what actually
carries the entitlement to stream. A token opens the door; a session walks
through it.

## Actors

The operator. Trusted. They already have every media file on the server and every
administrator route. Nothing here defends against them, and a row that pretended
to would be pretending.

An invited guest. Semi-trusted, and trusted about exactly one item for exactly one
window. The interesting threats are the ones where a guest reaches past that
window or past that item, deliberately or by an accident of how the server is
configured.

Another user of the same server. Has an account and is not invited to this share.
They can reach the routes; whether they get an answer is what the authorization
rows below are about.

Somebody holding a link they were not sent. No account, no session, and text they
found in a preview fetch, a mail scanner, a browser history or a proxy log.
`docs/leaked-link.md` is the whole answer to this actor and this document does not
restate it.

## Entry points

The administrator routes, which create, list and revoke shares. Issue #67 builds
them.

The guest route, which the link points at and which is the only route reachable by
somebody who has not been invited to anything. Issue #68 builds it.

The configuration surface, which is the dashboard page and the configuration file
behind it. Issue #71 fixes its schema and validation. It is an entry point because
what an operator can set there bounds what the rest of the plugin will do.

## Trust boundaries

The network edge. Everything past it is attacker-supplied, including the token,
the headers and the host the request claims to be for.

The reverse proxy. Terminates transport security, rewrites headers, and writes an
access log this plugin never sees. The plugin trusts it for nothing, which is
issue #49's reason for existing.

The server's own authorization layer. The plugin trusts this one, and says so.
Who the caller is comes out of the server's request context and never out of the
link, which is issue #53. If that layer is wrong, this plugin is wrong with it,
and no control below repairs that.

## The threats

Every row carries a control and a proof. A proof is a test name that exists in
this tree, or an issue number that owes it. Nothing accepted sits in this table;
the accepted residuals are the section after it, in words, so that a blank cell
can never be mistaken for an oversight.

| #   | Threat                                                                                                           | Control                                                                                                                                                              | Proof                                                                                                                        |
| --- | ---------------------------------------------------------------------------------------------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ---------------------------------------------------------------------------------------------------------------------------- |
| T1  | Somebody holding a link they were not sent opens it                                                              | The link names a record, never a caller. Identity comes from the server's request context, and both refusals give nothing away                                       | #24                                                                                                                          |
| T2  | A token is guessed, or the space is searched for live shares                                                     | 256 bits drawn from the operating system's cryptographic generator, in one routine, encoded without shortening                                                       | `AMintedTokenCarriesTheDeclaredEntropy`, `ALargeBatchOfTokensContainsNoDuplicate`, `AMintedTokenUsesOnlyTheDeclaredAlphabet` |
| T3  | Live shares are enumerated by asking, so a refusal tells an attacker which tokens are worth trying               | Every refusal on the guest route answers the same way, whatever the reason                                                                                           | #26                                                                                                                          |
| T4  | Somebody reading the store learns tokens from it                                                                 | The store holds a keyed hash of the token and never the token                                                                                                        | #43                                                                                                                          |
| T5  | A stolen store is brute-forced offline into working tokens                                                       | The hash is keyed, and the key is not in the store                                                                                                                   | #28                                                                                                                          |
| T6  | The comparison of a presented token against the store leaks which prefix was right                               | Constant-time comparison, with the invariant lint refusing an equality or `Equals` comparison against anything named token, secret or hash                           | #43                                                                                                                          |
| T7  | A token that is revoked or expired still resolves                                                                | Revocation and expiry are read on every resolution rather than at redemption, in one routine that makes the whole decision                                           | #46, #45, #48                                                                                                                |
| T8  | The same token is presented again, from a second device or a second address                                      | The reuse rule, written down and tested for one session, two sessions, and after revocation                                                                          | #25                                                                                                                          |
| T9  | A token reaches a log, a crash dump or an audit line, and the log becomes a working link                         | The never-log list, and the invariant lint refusing a logging call that names a token or a secret                                                                    | #27                                                                                                                          |
| T10 | The scope of a share widens, so a token reaches a second item                                                    | A token is bound to one item at mint time and the binding is not re-derived from the request                                                                         | #44, #47                                                                                                                     |
| T11 | A guest reaches past the shared item into the rest of the library                                                | Confinement chosen deliberately rather than inherited, and the list of what a token can never reach, with a test per line                                            | #52, #47                                                                                                                     |
| T12 | A guest's own account is left wider than it was, after the share ends                                            | Anything the plugin changed about the account is restored, and nothing is widened in the first place                                                                 | #58                                                                                                                          |
| T13 | Another user of the same server creates, lists or revokes shares                                                 | Every route is authorized explicitly, and the set of routes is proven closed rather than assumed                                                                     | #69, #77                                                                                                                     |
| T14 | A route ships reachable by anybody, by an attribute somebody forgot                                              | The invariant lint refuses an anonymous route, and the fixture that proves it bites is in the tree                                                                   | #69                                                                                                                          |
| T15 | A stream that is playing continues after the operator revokes the share                                          | Revocation reaches the session and not only the record                                                                                                               | #55                                                                                                                          |
| T16 | One share becomes a household's worth of concurrent streams                                                      | A bound on sessions and devices per share                                                                                                                            | #56                                                                                                                          |
| T17 | The link is built from what the request says the host is, so a forged header makes a link pointing elsewhere     | The link is built from a base the plugin holds rather than from anything the request supplied. Where that base comes from is #49's to decide and is not assumed here | #49                                                                                                                          |
| T18 | An operator action, or a script driving one, fills the store                                                     | A bound on what one action creates and on what the store grows into                                                                                                  | #29                                                                                                                          |
| T19 | A crash or two concurrent writes truncates the store, taking every live share with it                            | Write through a temporary file and rename, with writers serialised                                                                                                   | #35                                                                                                                          |
| T20 | An upgrade meets records written by an older version and loses them, or a downgrade guesses at newer ones        | A schema version in the store, a forward migration, and a refusal rather than a guess when the store is newer                                                        | #37                                                                                                                          |
| T21 | Restoring a backup brings revoked or expired shares back to life                                                 | What a restore does to live shares, decided and written down                                                                                                         | #40                                                                                                                          |
| T22 | Records outlive the plugin, or survive an uninstall the operator believed removed them                           | What disabling and uninstalling do, and what is left on disk                                                                                                         | #38                                                                                                                          |
| T23 | A record points at an item or an account that is gone, and the failure is a null reference rather than a refusal | Each case has a defined outcome, and the administrator view shows a share that can no longer resolve as such                                                         | #39                                                                                                                          |
| T24 | The plugin holds more about an invited guest than the feature needs, for longer than it needs                    | What personal data is held, and for how long, stated rather than accumulated                                                                                         | #31, `EveryFieldOfTheRecordHasARow`, `RemovingAShareLeavesNothingInTheFileNamingItsGuest`                                    |
| T25 | Expiry is wrong at the boundary, and nobody notices because the tests sleep                                      | The clock comes from a seam, refused by the invariant lint when it does not, and boundaries are covered without waiting                                              | #36, #79                                                                                                                     |
| T26 | The bitrate ceiling is enforced somewhere a guest can step around                                                | The ceiling is enforced at the point where the stream is actually decided, which #61 settles by measurement rather than by preference                                | #61                                                                                                                          |

## Accepted, and why

These are threats this plugin does not answer. Each one is here because it was
considered and left, not because nobody thought of it.

A guest who is entitled to watch can hand their own session to somebody else, or
point a camera at the screen. No token model prevents either. The share controls
who may start a session, and after that the media is out. This is the residual
issue #25 asks to be stated in a sentence, and it is the reason T1 is about text
rather than about media.

The operator is trusted completely. They can read every file the server can read,
including the store and the key, and they can create a share for anything. A
control against the operator would be a control against the person the plugin is
built for.

Anybody who can read the server's filesystem as the server's own user has the key
and the media both. Nothing here is a defence against that, and the keyed hash in
T4 buys exactly one thing, which is that a copy of the store alone is not enough.

Transport security belongs to the deployment. If the link travels over plain
HTTP, the token is on the wire, and no control in this plugin is between it and
whoever is listening. The plugin does not check that it is served over TLS, and it
does not refuse to run when it is not.

The channels a link travels through are assumed hostile and are not otherwise
addressed. Nothing was measured against any chat client, mail scanner or proxy.
`docs/leaked-link.md` argues that none of them gain anything, and that argument
rests on the design rather than on any of them having been observed.

The server's own authorization layer is trusted, as the boundary section says. A
defect there is a defect here.

## What this file does not settle

It does not decide anything. Every row points at the issue that decides its half,
and several of those are waiting on answers collected in #94, which is where the
question of how a guest comes to have an account, how they are confined, and
whether they may download rather than only stream, are all still open. A row whose
proof is one of those issues is owed twice over, and the table does not pretend
otherwise.

It does not enumerate the checks. What runs is what the workflows run, and
`docs/parity-ledger.md` is where the gate is compared against the one this
repository is levelling with.

Nothing refuses a stale row here. This is a document. If a proof lands and the row
still names an issue, every route stays green and the file is quietly wrong. The
security page issue #84 and the release-readiness pass issue #93 are the two
places that read this file again with that in mind.
