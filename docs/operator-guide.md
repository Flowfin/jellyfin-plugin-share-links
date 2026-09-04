# The operator guide

From a server with this plugin on it to a guest watching one item, and back to
the share stopped again. It is the path #83 asks for, written against the screens
and routes the tree holds rather than against a plan for them.

Read `README.md` first if you have not: this page assumes you know that a share
is one item for one invited guest, that there are no anonymous links, and that
the guest signs in.

**No step below has been performed on a running server.** Every screen name,
route, setting and refusal is read out of this tree, and that is a different kind
of evidence from a walkthrough. #236 is where one person walks this page on a
clean server and records what actually happened, and #83's last clause is that
run rather than this page.

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
instant, the reason and who pressed it, so the list can still say who was invited
to what.

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

And it has not been walked. Everything above is read from the tree at the commit
it landed on, which catches a screen name that is wrong and cannot catch a step
that is missing, because whoever writes a guide supplies the missing step without
noticing. That is #236's run and it has not happened.
