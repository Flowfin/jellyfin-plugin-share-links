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
`GuestRouteTests.ACallerTheShareNamesIsSentToTheItem` and
`GuestRouteTests.ACallerTheShareDoesNotNameGetsNothing` are the two halves of the
first sentence, and
`GuestRouteTests.AValidUnexpiredTokenFromACallerTheServerHasNotIdentifiedGetsNothing`
is the second: a good token with nobody behind it reaches nothing.

## What is built and what is not

No version of this plugin has been published. That is a fact about the forge
rather than about this tree, so it is read there:

    git ls-remote --tags origin | wc -l
    0

    gh release list --repo Flowfin/jellyfin-plugin-share-links
    (no rows)

The manifest field a catalogue shows under a version's changelog used to carry
that sentence as well, and it no longer does: the packaging tool copies it into
every package it builds, so a claim about the release history there would ship
inside the release that refutes it. It names where the notes are instead.

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

**THIS PARAGRAPH SAID THE RELEASE AND THE READING HAD NOT HAPPENED, AND BOTH
HAD.** It said nothing was published, and that nobody had walked the operator
guide on a clean server. Both happened on 2026-09-04 and neither was re-read
here, which is the defect the paragraph above describes, one section later. The
release is read rather than remembered:

    gh release view 0.1.0.0-stable --repo Flowfin/jellyfin-plugin-share-links --json tagName,publishedAt --jq '"\(.tagName) \(.publishedAt)"'
    0.1.0.0-stable 2026-09-04T15:53:06Z

So a package built from the tree is no longer the only way this plugin reaches a
server, and the section on installing below is the route an operator takes
instead. The walk is #236, which is closed; what it found is written line by line
at the foot of `docs/operator-guide.md`, and what it did not reach is still read
out of the tree rather than seen. That page is where the two are separated, and
it is the thing to read before trusting a screen name in this repository. #269 is
a defect on the browser path that is open rather than explained, and
`docs/limits.md` is where the behaviour that reads as a defect until somebody
explains it is collected.

The open issues on this repository are the current state; this file is not, and
does not try to be.

## Supported server versions

One server line, declared once, in `build.yaml`:

    git grep -n '^targetAbi:' -- build.yaml
    build.yaml:16:targetAbi: "10.11.9.0"

That number is what the package offers a server and a catalogue as the line it
was built for. Whether a particular server accepts it is the server's decision
and not this plugin's.

Two numbers of that shape may appear in this file and no third, and a test
refuses one that does: `ReadmeTests.EveryVersionTheReadmeNamesIsTheOneTheManifestCarries`
reads both out of `build.yaml`, the server line above and the release version
below, and refuses any other. `ReadmeTests.TheReadmeNamesTheVersionRatherThanLeavingItOut`
is the other direction for the server line, so the rule cannot be satisfied by
naming no version at all. A readme naming a version the package does not carry
sends somebody to install something that will not load, and each number is in two
files rather than one only because a reader will not open the manifest.

The tree compiles a second line and does not support it. `Directory.Build.props`
names a Jellyfin package version per target framework, so a build produces
assemblies for both 10.11 and 12.0 and the checks here judge both. What that buys
is finding out on the day it happens which of this plugin's calls move when the
server line does. It is not a claim of support: the 12.0 line has no released
version yet, the package this repository builds carries the one `targetAbi`
above, and nothing here has been run against a 12.0 server. #181 is where that is
decided and what turns the second line into a supported one.

## Installing

It arrives the way a Jellyfin plugin usually does. An operator adds a repository
URL to the server's plugin catalogue, under Dashboard, Plugins, Repositories, and
installs **Share Links** from the entry that appears. The URL is

    https://flowfin.dev/manifest.json

and the version that catalogue serves today is `0.1.0.0`, offered for the one
server line the `targetAbi` above names. That manifest is generated from the
finished releases rather than written by hand, which was #90; whether this plugin
is also submitted to the official Jellyfin catalogue is #92, which declined it,
and `docs/catalogue-checklist.md` carries that decision and its reason.

The checksum the catalogue serves is the one the published archive hashes to, so
a server that refuses the download is saying something about the download rather
than about the manifest:

    curl -sS https://flowfin.dev/manifest.json | python -c 'import json,sys; print(*[v["checksum"] for e in json.load(sys.stdin) if e["name"] == "Share Links" for v in e["versions"]])'
    6a3261b3e4b6ab6bd4de787d994aa0bb

    curl -sSL https://github.com/Flowfin/jellyfin-plugin-share-links/releases/download/0.1.0.0-stable/share-links_0.1.0.0.md5
    6a3261b3e4b6ab6bd4de787d994aa0bb  share-links_0.1.0.0.zip

A clean server has taken that route, and no person has. The third boot of
`.github/workflows/observations.yml` starts a server with an empty plugin
directory, adds the URL above and installs this plugin from the entry that
appears, and a second script judges what it recorded rather than the recording
being read by somebody. A run of it on the mainline:

    gh run view 33951290469 --repo Flowfin/jellyfin-plugin-share-links --json conclusion,headSha,createdAt --jq '"\(.conclusion) \(.createdAt) \(.headSha)"'
    success 2026-09-05T06:59:05Z bd4c823adfcd2cfd10649544552049f838a1ea43

That run's steps are named `Refuse a catalogue a clean server cannot install this
plugin from` and `What the clean server said`, and its log carries the guid the
repository put in front of the operator, the version offered, and the served and
computed checksums agreeing.

**WHAT NOBODY HAS DONE IS FOLLOW IT ON THE SCREENS.** That boot drives the
server's own API, and the one walk of this plugin by a person, on 2026-09-04, put
it on the server by unpacking the archive into the plugin folder by hand. So the
dashboard names in the paragraph above are read out of the way Jellyfin installs
a plugin rather than off a screen somebody watched, and they are the only claim
here that neither the suite nor that boot judges.

Building a package from the tree works today and produces a plugin that loads,
names itself and offers the page described above.

**THIS PARAGRAPH SAID THE PAGE HAD NOTHING ON IT, AND THAT THE VERSION SUCH A
BUILD CARRIES IS RESERVED FOR THE CASE.** Both stopped being true and neither was
re-read. #70 put the controls on the page, which the section above this one
already says, so one paragraph of this file contradicted another. And the
reservation of the all-zero version was retired when a release that stamps its
own version turned out to be incompatible with it; `docs/versioning.md` carries that under a
heading saying so, and what it now says is what a build from the tree carries and
how it is told apart from a release - by the commit in the informational version
and by the attestation on the published archive, not by the version.

## Creating a share

1. Sign in as an administrator and open Share Links from the server dashboard.
2. Pick the item. One item and only that item: not the season above an episode,
   not the library it sits in, not the next thing in the folder.
3. Name the guests, one per line. Creating the share is what creates their
   accounts, and the credential for each is shown once beside the link
   (`ShareCreationTests.AShareForSeveralGuestsMakesOneAccountEachAndPairsTheCredentialsWithTheNames`).
4. Set when it expires, and a bitrate ceiling if the uplink wants one.
5. Copy the link. It is shown once, when the share is created
   (`ShareCreationTests.NeitherTheLinkNorTheCredentialIsInTheListingAfterwards`,
   and `ConfigurationPageTests.ThePageSaysTheLinkIsShownOnce` for the page saying
   so where an operator reads it).
6. Send it to the guest yourself. The plugin sends no mail and adds nothing to
   the web client, so the link travels however the operator chooses.

The guest signs in with the name and credential they were sent, opens the link,
and lands on the one item the share names. `docs/operator-guide.md` walks the
same path with the screen each step happens on.

## Revoking a share

1. Open Share Links from the dashboard and find the share in the list.
2. Revoke it. The next request made against that share is refused, and nothing
   waits for a periodic sweep to notice
   (`ShareRevocationTests.TheNextRequestAfterRevocationIsRefusedWithNoSweepHavingRun`).
3. The record stays, marked revoked and carrying the time it was revoked, so the
   list can still say who was invited to what
   (`ShareRevocationTests.RevokingRecordsWhenWhoAndWhy`).

Revoking a share that has already been revoked, or one that has already expired,
succeeds and changes nothing:
`ShareRevocationTests.RevokingTwiceSucceedsAndChangesNothing` and
`ShareRevocationTests.RevokingAnExpiredShareSucceedsAndChangesNothing`.

Expiry needs no action from anybody. A share past its expiry refuses in the same
way a revoked one does, which is `GuestRouteTests.EveryRefusalIsTheSameBytes`
rather than a statement about expiry alone: every refusal on that route is the
same bytes, so the two cannot be told apart from outside.

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
document under `docs/` has to be accounted for, and a test compares the two in
both directions - `LimitsTests.EveryDocumentThisPageNamesIsInTheTree` and
`LimitsTests.EveryDocumentInTheTreeIsNamedHere` - so it is the index to read
rather than this one.

## Licence

GPL-3.0. `LICENSE` is the authority.

A Jellyfin plugin links against the server's GPLv3 packages when it is compiled,
so the built plugin is GPLv3 whatever a source licence says. That is worth
knowing before choosing a different one.

See [NOTICE.md](NOTICE.md) for the intended-use notice.
