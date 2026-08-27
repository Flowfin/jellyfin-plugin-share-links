# Share Links

Share Links does one thing. An operator picks one library item and gets a link to
that one item for a guest they have invited. The link expires, an operator can
revoke it before it expires, and a share can carry a ceiling on the bitrate the
guest streams at.

> [!NOTE]
>
> **Part of [Flowfin](https://github.com/Flowfin).** It works with any Jellyfin
> server, and with the Flowfin clients.

Sharing is designed for invited guests of the server operator. There are no
anonymous public links. A guest signs in to the server with an account, and the
link resolves for the account the share names and for nobody else. A link that
ends up in a chat preview, a mail scanner, a browser history or a proxy log is
text, and text on its own opens nothing here.

## What is built and what is not

No version of this plugin has been published. The manifest says so in the field a
catalogue shows:

    grep -A2 '^changelog:' build.yaml
    changelog: >
      No version of this plugin has been published yet, so there is nothing here to
      read. A released version carries its own entry.

What the tree holds is the share record, the store it is kept in, the token and
the keyed hash it is looked up by, the one routine that decides whether a share
resolves, the guest route a link points at, the administrator routes that create,
list and revoke a share, and the request-path filter that confines a guest and
applies the ceiling:

    git grep -nE '\[HttpGet|\[HttpPost|\[HttpDelete' -- 'Jellyfin.Plugin.ShareLinks/*.cs'
    Jellyfin.Plugin.ShareLinks/ShareLinksAdminController.cs:176:    [HttpPost("Shares")]
    Jellyfin.Plugin.ShareLinks/ShareLinksAdminController.cs:369:    [HttpGet("Shares")]
    Jellyfin.Plugin.ShareLinks/ShareLinksAdminController.cs:442:    [HttpPost("Shares/{shareId}/Revoke")]
    Jellyfin.Plugin.ShareLinks/ShareLinksAdminController.cs:561:    [HttpPost("Key/Rotate")]
    Jellyfin.Plugin.ShareLinks/ShareLinksGuestController.cs:159:    [HttpGet("Guest/{token}")]

The page an operator drives them from is in the tree as well,
`Jellyfin.Plugin.ShareLinks/Configuration/configPage.html`. So the sections below
describe surfaces that exist rather than a plan for them.

**THIS SECTION SAID THE OPPOSITE UNTIL #294.** It said the tree held no way to
make a share, that the whole route surface was one action and it was the guest's,
that an operator had nothing to press, and that everything below it was the design
rather than a description of a working install - and it pasted the command above
returning one line to prove it. Every word of that was true when it was written.
#67 built the administrator routes and #70 built the page, both closed, and this
paragraph was not re-read against them. The pasted output is the part to learn
from: a command in a document is the thing that goes stale first and the thing a
reader trusts most.

What has not happened is a release and a reading. Nothing is published, so a
package built from the tree is the only way this plugin is on a server today. And
nobody has walked the operator guide on a clean server: #236 is that reading and
it has not been made, so every screen name and refusal in this repository's
documentation is read out of the tree rather than seen. #269 is a defect on the
browser path that is open rather than explained, and `docs/limits.md` is where the
behaviour that reads as a defect until somebody explains it is collected.

The open issues on this repository are the current state; this file is not, and
does not try to be.

## Supported server versions

One server line, declared once, in `build.yaml`:

    grep -n '^targetAbi:' build.yaml
    13:targetAbi: "10.11.9.0"

That number is what the package offers a server and a catalogue as the line it
was built for. Whether a particular server accepts it is the server's decision
and not this plugin's.

No other version of that shape appears in this file, and a test refuses one that
does. A readme naming a version the package does not carry sends somebody to
install something that will not load, and the number is in two files rather than
one only because a reader will not open the manifest.

The tree compiles a second line and does not support it. `Directory.Build.props`
names a Jellyfin package version per target framework, so a build produces
assemblies for both 10.11 and 12.0 and the checks here judge both. What that buys
is finding out on the day it happens which of this plugin's calls move when the
server line does. It is not a claim of support: the 12.0 line has no released
version yet, the package this repository builds carries the one `targetAbi`
above, and nothing here has been run against a 12.0 server. #181 is where that is
decided and what turns the second line into a supported one.

## Installing

There is nothing published to install from. When there is, it arrives the way a
Jellyfin plugin usually does: an operator adds a repository URL to the server's
plugin catalogue and installs from the entry that appears. The manifest behind
that URL is generated by the release rather than written by hand, which is #90,
and whether this plugin is also submitted to the official catalogue is #92.

Building a package from the tree works today and produces a plugin that loads,
names itself and offers a page with nothing on it. `docs/versioning.md` says what
version such a build carries and why that version is reserved for exactly this
case.

## Creating a share

1. Sign in as an administrator and open Share Links from the server dashboard.
2. Pick the item. One item and only that item: not the season above an episode,
   not the library it sits in, not the next thing in the folder.
3. Name the guests, one per line. Creating the share is what creates their
   accounts, and the credential for each is shown once beside the link.
4. Set when it expires, and a bitrate ceiling if the uplink wants one.
5. Copy the link. It is shown once, when the share is created.
6. Send it to the guest yourself. The plugin sends no mail and adds nothing to
   the web client, so the link travels however the operator chooses.

The guest signs in with the name and credential they were sent, opens the link,
and lands on the one item the share names. `docs/operator-guide.md` walks the
same path with the screen each step happens on.

## Revoking a share

1. Open Share Links from the dashboard and find the share in the list.
2. Revoke it. The next request made against that share is refused, and nothing
   waits for a periodic sweep to notice.
3. The record stays, marked revoked and carrying the time it was revoked, so the
   list can still say who was invited to what.

Revoking a share that has already been revoked, or one that has already expired,
succeeds and changes nothing.

Expiry needs no action from anybody. A share past its expiry refuses in the same
way a revoked one does.

## Documentation

- `docs/share-store.md`, where share records are kept and why the other two
  candidates were refused.
- `docs/leaked-link.md`, what a leaked link is worth and where the token sits in
  the URL.
- `docs/plugin-lifecycle.md`, what disabling and uninstalling do, and what is
  left on disk.
- `docs/versioning.md`, the version scheme and what a build from the tree
  carries.
- `docs/testing.md`, the conditions the test suite runs under.
- `docs/operator-guide.md`, the path from installing to revoking, with the screen
  each step happens on.
- `docs/limits.md`, what an operator runs into and what to do about each one.

That list is a selection rather than the set. `docs/limits.md` is where every
document under `docs/` has to be accounted for, and a test compares the two, so
it is the index to read rather than this one.

## Licence

GPL-3.0. `LICENSE` is the authority.

A Jellyfin plugin links against the server's GPLv3 packages when it is compiled,
so the built plugin is GPLv3 whatever a source licence says. That is worth
knowing before choosing a different one.

See [NOTICE.md](NOTICE.md) for the intended-use notice.
