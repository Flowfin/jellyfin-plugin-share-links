# What a share does to playback state, and who can see it

This is the answer issue #59 asks for. `docs/guest-capabilities.md` decided that a
guest may resume, seek, mark watched and rate, and said that what those writes mean
for who can see a guest's viewing belongs here.

The short answer is that the writes happen, the operator can see them, and this
plugin does not stand in the way of either. The rest of this page is why, and what
that costs.

## Playback progress is recorded

A guest watching the shared item leaves the same trace any other account does. The
server keeps it on its own user data rather than in anything this plugin owns, and
the members it keeps are these.

| What is kept                   | Where it lives                       | Type        |
| ------------------------------ | ------------------------------------ | ----------- |
| How far into the item they got | `UserItemData.PlaybackPositionTicks` | `long`      |
| Whether they finished it       | `UserItemData.Played`                | `bool`      |
| How many times they started it | `UserItemData.PlayCount`             | `int`       |
| When they last watched it      | `UserItemData.LastPlayedDate`        | `DateTime?` |
| What they scored it            | `UserItemData.Rating`                | `double?`   |
| Whether they favourited it     | `UserItemData.IsFavorite`            | `bool`      |
| Whether they liked it          | `UserItemData.Likes`                 | `bool?`     |

This is not a choice this plugin makes so much as one it declines to fight. The
account policy carries no switch that turns progress recording off, and the user
configuration carries none either. Suppressing it would mean this plugin standing
in the path of every playback request and every user-data write, which is a far
larger surface than the thing being suppressed is worth, and it would break resume,
which the guest capability list allows on purpose.

## What the operator can see

Three surfaces, all of them the server's own.

The live session, while a guest is watching. This is the account, the title, the
position, the client and the address it is being watched from, on the server's own
dashboard rather than in a view this plugin adds.

| What it shows              | Where it lives               | Type              |
| -------------------------- | ---------------------------- | ----------------- |
| Which account              | `SessionInfo.UserId`         | `Guid`            |
| Under what name            | `SessionInfo.UserName`       | `string`          |
| What is playing            | `SessionInfo.NowPlayingItem` | `BaseItemDto`     |
| Where in it, paused or not | `SessionInfo.PlayState`      | `PlayerStateInfo` |
| On what client             | `SessionInfo.DeviceName`     | `string`          |
| From what address          | `SessionInfo.RemoteEndPoint` | `string`          |

The item afterwards, read back through the API for the account that watched, so the
trace outlives the session.

| What it shows             | Where it lives                          | Type        |
| ------------------------- | --------------------------------------- | ----------- |
| How far they got          | `UserItemDataDto.PlaybackPositionTicks` | `long`      |
| Whether they finished it  | `UserItemDataDto.Played`                | `bool`      |
| How many times            | `UserItemDataDto.PlayCount`             | `int`       |
| When they last watched it | `UserItemDataDto.LastPlayedDate`        | `DateTime?` |
| How far as a percentage   | `UserItemDataDto.PlayedPercentage`      | `double?`   |

The activity log. The server writes it for its own events, including sessions
starting and playback beginning, and this plugin writes nothing into it.

| What it shows | Where it lives            | Type       |
| ------------- | ------------------------- | ---------- |
| Which account | `ActivityLogEntry.UserId` | `Guid`     |
| Which item    | `ActivityLogEntry.ItemId` | `string`   |
| What happened | `ActivityLogEntry.Name`   | `string`   |
| Of what kind  | `ActivityLogEntry.Type`   | `string`   |
| When          | `ActivityLogEntry.Date`   | `DateTime` |

So an operator who wants to know what a guest watched, when, from where and how far
they got, can find out. There is no configuration in this plugin that changes that,
and this document exists so that an operator handing out a link knows it before
they hand it out rather than after.

The types above are the ones the compiler sees. Whether a reference is allowed to
be null is not among them, so a `string` in these tables is a claim about the type
and not about the value being present.

## The residuals, stated rather than discovered

Two accounts of the same invitation are one account. Where a share invites one
account and two people use it, they see each other's progress, each other's resume
positions and each other's watched marks, because it is one account's user data and
the server has no notion of who is holding it. Nothing here separates them, and no
token model can.

The operator sees viewing they did not have to ask for. Handing somebody a share is
also acquiring a record of them watching it. The honest form of that is written
here rather than left as something a guest might assume was private.

The address is visible. `SessionInfo.RemoteEndPoint` is where a guest was watching
from, which is a different kind of fact from what they watched, and it is kept by
the server for as long as the session lives.

## Retention

None of what is above is this plugin's to keep or to delete. It belongs to the
server's user data and to the server's activity log, and what removes it is
removing the account it belongs to, which `docs/guest-accounts.md` decides for an
account this plugin created.

What this plugin holds about a person is a different list and a different retention
answer, and it is `docs/personal-data.md` rather than this page. Nothing here sets a retention length,
because writing a second number next to that one would be two answers in the tree.

## What is checked, and what is not

`PlaybackVisibilityTests` reads this page rather than a copy of it. It takes every
backticked `Type.Member` out of the tables above, resolves it against the server
this plugin compiles against, and compares the stated type against the one the
member really has. A name is a claim about another artefact, and a server line that
renames or drops one turns this page into a description of somewhere else. Adding a
row is therefore adding an assertion, and there is no second list to remember,
which is the failure the same guard for `docs/configuration.md` was written after.

It also holds the negative. This page says the user configuration offers no switch
that suppresses playback progress, and the test pins the whole set of user
configuration members, so a server line that adds one reds the suite and this page
is re-read rather than left asserting an absence that has stopped being true. That
is the direction a negative claim rots in.

What is not checked is the behaviour. No test here drives a guest through a playback
request, because there is no route for one to travel and no guest account for it to
belong to; the account creation path is #51's decision and not yet code, and the
routes are #67 and #68. When they exist, the test this page owes is that a
resolution for a guest sets no user-data suppression of its own and leaves the
server's writes alone. That is a stronger test than the one here and it is not
available yet.

Nothing checks the prose. What a member means, whether the left-hand column
describes it correctly, and whether the four tables above are all the surfaces there
are, are judgements no run makes.
