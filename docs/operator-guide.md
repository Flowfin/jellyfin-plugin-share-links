# The operator guide

From a server with this plugin on it to a guest watching one item, and back to
the share stopped again. It is the path #83 asks for, written against the screens
and routes the tree holds rather than against a plan for them.

Read `README.md` first if you have not: this page assumes you know that a share
is one item for one invited guest, that there are no anonymous links, and that
the guest signs in.

**This page has been walked on a running server once, on 2026-09-04, and the
walk stopped short.** A hand that did not write the guide followed it on a fresh
`jellyfin/jellyfin:10.11.9` with no prior state and the published `0.1.0.0`
archive. The configuration page did nothing at all: its controller sat outside
the element the web client mounts, so sections 1 to 4, 6, 7 and 8 could not be
followed as the page shipped. That is #349, and it is repaired in the change this
sentence lands in.

**The walk has not been made again since that repair.** What is below the
stopping point was read with the controller pasted into the browser's console by
hand, so that the guide's claims could be held against the server at all; that is
a weaker kind of evidence than following the page, and the difference is named
line by line at the foot of this page. Everything not marked there is still read
out of the tree rather than off a screen.

## Before anything, install it

There is nothing published to install from yet. `README.md` says what arrives
when there is, and #90 and #92 are the issues. A package built from the tree
loads today, which is how this plugin is on a server at all before its first
release.

Once it is on the server, open the dashboard, go to the plugin list and open
**Share Links**. Every screen named below is a section of that one page.

## 1. Set the public base URL

The first field on the page, **Public base URL**.

This is the address the server is reached at from outside, and it is what the
link an operator copies is built on. Write it absolute, `http` or `https`, with
no trailing slash.

Fill it in before creating anything. Left empty, the link is built from what the
request claimed it was, and that is text a caller supplies: a forged header
produces a link pointing somewhere else. `docs/configuration.md` carries that in
the settings table and `ShareLinkBuilder` is the routine that refuses a value
which is not an absolute `http` or `https` URL.

## 2. Decide the lifetimes and the ceilings

The rest of the settings form, in the order it shows them.

- **Live shares across the server.** How many shares may be live at once across
  the whole server. Default 100.
- **Live shares naming one item.** How many live shares may point at one item.
  Default 10.
- **Longest lifetime a link may be given, in days.** The ceiling on any single
  share. Default 30. It is checked when a share is created and never when one is
  resolved, so lowering it later leaves links already handed out alone.
- **Lifetime a share gets when no expiry is named, in days.** Default 7, and it
  has to be at or under the ceiling above.
- **How long a share that has stopped working is kept, in days.** Default 90,
  counted from the instant it stopped. Zero deletes such a record at the first
  write after it stopped, which is how to empty the store of what has expired.
- **Ceiling a new share gets when none is named, in megabits per second.** Empty
  by default, which means no ceiling.
- **Sessions one invited guest may hold at once.** Default 5. It is a ceiling on
  the account rather than on the share, so a guest invited to two shares carries
  one number across both.

Press the submit button under them. `docs/configuration.md` is the reference for
every one of these, with its unit, its bounds and what an empty value means.

Two of these are refused at two different moments, and it is worth knowing which:
a value outside its bounds is refused when the page saves it, and again when the
routine that reads it runs. A configuration file edited by hand can hold a value
the page would have refused, and the create route answers such a state with a
fault naming the setting rather than quietly using a default.

## 3. Create the share, which is also how the guest gets an account

The **Create a share** section.

- **Item identifier.** The one item, and only that one: not the season above an
  episode, not the library it sits in, not the next thing in the folder. It is
  the identifier the server holds for the item. Where the web client shows that
  identifier is the client's, and this repository has not measured it; the
  address the guest is finally sent to is built as `/web/#/details?id=<item>`,
  which `ShareLinksGuestController` declares as an assumption rather than a
  measurement.
- **Guests, one name per line.** This is the step that surprises people, and it
  is in the heading above on purpose: creating the share is what creates the
  accounts. You are not naming accounts that already exist. A name the server
  already has is refused, before anything is made.
- **Expires at.** Leave it empty to take the default lifetime from the settings.
  A lifetime past the ceiling is refused and nothing is made.
- **Ceiling for this share, in megabits per second.** Leave it empty to take the
  default from the settings, or leave both empty for no ceiling.

The route behind the button is `POST /ShareLinks/Shares`, and `docs/api.md`
carries what it takes and what it answers.

The order the route works in is worth knowing because it decides what an ordinary
mistake costs: everything that can refuse without changing anything happens
first, so a lifetime past the ceiling, a name that is taken or an item the server
does not hold costs no account and no record.

## 4. Copy the link and the credentials, once

When the create succeeds the page shows the link and one credential per guest,
and says in its own words that they are shown once.

They are shown once because only a keyed hash of the token is written down and no
credential is written down at all. The server cannot produce either of them
again. Leaving the page or creating another share is the end of them.

The credential is 43 characters of base64url. That is unpleasant to type on a
television remote, and it is the honest cost of not opening a second source of
secret material; `docs/guest-accounts.md` argues it.

Send both to the guest yourself. This plugin sends no mail and adds nothing to
the web client, so the link travels however you choose.

## 5. What the guest does

The guest signs in on the server's own sign-in page with the name and credential
you sent, and then opens the link.

**Opening the link in a browser does not work yet, and this paragraph used to
say that it did.** It said a guest who opens the link before signing in is sent
to the sign-in page and opens it again afterwards. Driven against a real Jellyfin
with the packaged plugin installed, neither half happens: the link answers `401`
and a blank page before the guest signs in, and answers `401` again after they
have. A browser attaches no identity to a top level navigation, because the web
client keeps its access token in its own storage rather than in a cookie, and the
same link answers `302` in the same run when the request carries that token in a
header. #269 carries the run and the answer chosen for it, and nothing in the
tree does that yet.

The link resolves for the account the share names and for nobody else. A link in
a chat preview, a mail scanner, a browser history or a proxy log is text, and
text on its own opens nothing here.

If the account cannot sign in, because it was disabled or deleted, the request is
refused for carrying no caller. Disabling is reversible and an account enabled
again resolves its share again; deletion is not, because the record names an
identifier and a new account with the same name is somebody else.

## 6. Read the list

The **Shares** section is every share the server holds, including the ones that
have stopped working. Its columns, in the order the screen heads them:

- **Item.** The identifier of the item the share names. Nothing here asks the
  server for a title.
- **Invited.** The accounts the share resolves for.
- **State.** Live, expired or revoked.
- **Expires.** When it stops answering of its own accord.
- **Ceiling.** The share's own ceiling, which is the number that was typed onto
  it. It is not what a guest is held to.
- **In force.** What a guest of it would actually be held to, one line per
  invited account, with the ceiling that produced the number named beside it.
- **Revoked.** When somebody stopped it, where somebody did.

The last column carries no heading. It is where the revoke button of section 7
sits, on a share that is still live.

The state column is the point. A share that expired and a share that was never
made look the same to somebody who is only told what is live.

The in-force column is the second one to read rather than a repeat of the
ceiling beside it. A share whose own ceiling is doing nothing and one whose
ceiling is the one holding are different situations repaired in different
places, and one number cannot tell them apart. Each of its lines also says
whether that ceiling can be met for this item, and one of those answers wants an
operator: **NOTHING CAN BE SERVED** means every version the server offers is
above the ceiling and none of them can be brought under it, so a guest opening
that link is refused rather than served at a lower quality. Raise the ceiling,
or share something the server can serve under it. `docs/bitrate-cap.md` is the
reference for that column and for the other answers it gives.

The listing carries neither the token nor the hash of it. There is no way to read
a link back out of this page, which is the same fact as the link being shown
once.

Two things the state column does not tell you today. A share whose item a library
scan removed still reads as live, and so does one whose invited accounts have all
gone; both are #39, and the guest meets a refusal in the first case rather than a
dead address. `docs/gone.md` is the decision behind that and says which half is
built.

## 7. Revoke

Find the share in the list and press revoke. The route is
`POST /ShareLinks/Shares/{shareId}/Revoke`.

The next request made against that share is refused, and nothing waits for a
periodic sweep to notice. The record stays, marked revoked and carrying the
instant and who pressed it, so the list can still say who was invited to what.

**It carries no reason when the press came from this page**, and this paragraph
used to say it carried one. `RevocationReason` is a field on the record and the
route takes it in the request body, but the page's revoke button sends no body,
so a revocation made the way this section describes is stored with the instant
and the revoker and nothing else. A caller of the route can supply a reason;
nothing on the page can. Read at the button, in `configPage.html`, and confirmed
on the walk of 2026-09-04, whose record on #83 read the stored record back.

The press has no confirmation step and answers nothing on success: the row
becomes **Revoked** with the instant and the button goes. Section 8's button
behaves the same way and destroys much more.

The guests this plugin made for that share are signed out. A guest who still
holds another live share is not signed out, and their account stays enabled.

Pressing revoke twice succeeds and changes nothing: the first press's instant,
reason and revoker stay where they were. Revoking a share that had already
expired succeeds too.

Expiry needs no action from anybody. A share past its expiry refuses in the same
way a revoked one does.

## 8. When the secret itself may have leaked

The **The keyed hash secret** section, and its button says what it does:
**Replace the secret and stop every share**.

Reach for it when the secret itself may be in the wrong hands: a copy of the data
folder, a backup restored somewhere it should not have been, a support bundle
that turned out to include it. To stop one link that went to the wrong person,
revoke that share instead.

There is no way back. Every link handed out stops working, every share is marked
revoked, and the guests this plugin made for them are signed out and disabled. No
link can be reissued, because only a keyed hash of each token was ever written
down. `docs/share-key.md` is the reference.

## The things that surprise people

**The link is shown once.** Not because it is hidden afterwards, but because
nothing that could rebuild it was written down.

**The guest has to sign in.** There are no anonymous links, and this is the
design rather than a setting. `docs/leaked-link.md` is what a leaked link is
worth and why.

**Creating a share is what creates the guest account.** The account is not
something you prepare first, and it outlives the share: a guest invited to two
shares has one account across both, ends one share with the account still
enabled, and is disabled when the last of their live shares ends.

**Revoking does not stop bytes already in flight.** This is the one place where
the obvious sentence is wrong and the tree says so. Revoking ends the sessions,
so the next thing the client asks the server for is refused. A segment the client
already holds an address for is served by the server, which this plugin does not
stand in front of, and a transcode that is already running is the server's to
end or not. Nothing here measured what happens to either. `docs/revocation.md`
writes that chain out and marks which part of it was measured.

**The guest sees the client's own signed-out state and not a message from this
plugin.** No sentence is sent. `docs/revocation.md` says why and records it as a
residual rather than describing a message that exists.

**A guest who forgets the credential does not reset it here.** This plugin offers
no reset of its own, deliberately: a reset path belonging to the plugin is a
second way into an account the plugin created, and the people who would find it
are the people who have the link. Set the password again from the server's own
user page, or revoke the share and issue a new one. The second is the better
answer when the credential may have gone somewhere it should not have.

## What this page is not

It is not the reference for any of the surfaces it walks through.
`docs/configuration.md` is the settings, `docs/api.md` is the routes,
`docs/limits.md` is what an operator runs into and what to do about each one, and
`docs/security.md` is the posture with its residuals. This page is the order to
do things in and nothing more.

And it was written by somebody holding the tree in their head, which catches a
screen name that is wrong and cannot catch a step that is missing, because
whoever writes a guide supplies the missing step without noticing. The section
below is the one reading it has had by somebody who was not.

## What the walk found

Walked on 2026-09-04 on a fresh `jellyfin/jellyfin:10.11.9`, wizard completed
over the API, one library holding one generated eight-second film, and the
published `share-links_0.1.0.0.zip` unpacked into `/config/plugins/`. The full
record, in the page's and the server's own words, is on #83. What follows is what
it means for this page, and every line of it is still true of the guide unless
this change repaired it.

**The install section has no step that works.** It says nothing is published;
`0.1.0.0-stable` exists. The guide names neither the repository URL an operator
adds under Plugins, Repositories nor the catalogue entry that follows, and the
plugin reached the walked server by unpacking the archive by hand, which no
operator will do. The missing step is: add the repository, install from the
catalogue, restart. Unrepaired here, because what the section should say is what
the first release publishes, which is #90 and #92.

**Opening the page matched.** Dashboard, Plugins, the installed tab, **Share
Links 0.1.0.0 Active**, opening that one page. Every screen name in this guide
held.

**Then the walk stopped**, on #349, which this change repairs and which nothing
has re-walked.

**Sections 1 and 2 matched**, with the controller pasted in by hand: seven fields
in this guide's order, and the server's own file afterwards held the base URL and
the defaults. The button is labelled **Save**, which this guide calls "the submit
button". One press raised the saved-settings toast twice.

**Sections 3 and 4 matched, and the guests field has no label.** This guide calls
it "Guests, one name per line". On the page it is an unlabelled box under the
paragraph beginning _One account is made per name_, with no placeholder either,
and an operator finds it by elimination. Unrepaired here: the label belongs to the
page rather than to this guide.

**Section 5's refusal for a stranger has a number this guide does not give.** The
link carrying an account the share does not name answers `404`, not `401` and not
`403`. This guide says the link "resolves for the account the share names and for
nobody else" and stops there. The rest of section 5 held: no identity at all is
`401`, the guest's own token is `302` to the item.

**Section 6's columns matched and its names did not.** **Invited** shows the
account's identifier rather than its name, and the in-force lines name the same
identifier. This guide says "the accounts the share resolves for", which an
operator reads as names. The list never shows a guest's name anywhere.
Unrepaired here: what the column should carry is the page's question.

**Section 7's effects all held** — the refusal, the sign-out, the disabled
account, the `403` on signing in again — and its record carries no reason, which
is corrected in that section above.

**Section 8 fired on a single click**, with no confirmation asked and nothing
shown afterwards, although the page's own text speaks of a count the server
answers with. For the one act this guide calls irreversible, that is the finding
of the walk after #349. Unrepaired here.

**One thing surprised the walker that no section mentions**: the toasts follow
the server's culture rather than the page's, so a German toast appears on an
English page.

**What the walk did not reach**, so nothing above or below rests on it: bitrate
ceilings and the in-force answers beyond _no ceiling is set anywhere_, expiry by
elapsed time, a second share for one guest, and any playback at all.
