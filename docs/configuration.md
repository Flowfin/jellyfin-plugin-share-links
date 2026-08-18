# Configuration reference

Every setting this plugin holds. The list is taken from `PluginConfiguration`
rather than from memory, and `ConfigurationReferenceTests` compares the two in both
directions, so a setting added without a row here reds the suite, and so does a row
naming a setting that no longer exists.

## The settings

| Setting                     | Meaning                                                                                                                                         | Unit                                                         | Default | Bounds                                                                                                                                                               | When it is empty                                                                                                                              |
| --------------------------- | ----------------------------------------------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------ | ------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------- | --------------------------------------------------------------------------------------------------------------------------------------------- |
| `PublicBaseUrl`             | The address this server is reached at from outside. It is the host the link an operator copies is built on.                                     | An absolute URL, written with no trailing slash              | `""`    | Absolute, and `http` or `https`. A value that is anything else is refused and no link is produced from it.                                                           | The link is built from what the request claimed. That is text a caller supplies, so a forged header produces a link pointing somewhere else.  |
| `MaxLiveShares`             | How many shares may be live across the whole server at once. Live means neither revoked nor past its expiry.                                    | A count of shares                                            | `100`   | At least 1. A create that would pass the ceiling is refused by `ShareBounds`, and a value below 1 is refused when the bounds are read.                               | Not applicable; the value is a number and is never empty.                                                                                     |
| `MaxLiveSharesPerItem`      | How many live shares may name one item.                                                                                                         | A count of shares                                            | `10`    | At least 1, refused in the same place and in the same way as the ceiling above.                                                                                      | Not applicable; the value is a number and is never empty.                                                                                     |
| `MaxShareLifetimeDays`      | The longest lifetime a link may be given, measured from when the share is created.                                                              | Days                                                         | `30`    | At least 1. Checked when a share is created and never when one is resolved, so lowering it leaves links already handed out alone.                                    | Not applicable; the value is a number and is never empty.                                                                                     |
| `DefaultShareLifetimeDays`  | The lifetime a share is given when the operator creating it names no expiry. A share created with an expiry of its own keeps that one.          | Days                                                         | `7`     | At least 1 and at most `MaxShareLifetimeDays`, refused by `ShareConfiguration` when the setting is read and on save. Zero is a link that expired before it was sent. | Not applicable; the value is a number and is never empty.                                                                                     |
| `ExpiredShareRetentionDays` | How long a share that has stopped working is kept before it is deleted, counted from the instant it stopped.                                    | Days                                                         | `90`    | At least 0. Zero deletes it at the first write after it stopped working, which is how an operator empties the store of what has expired.                             | Not applicable; the value is a number and is never empty.                                                                                     |
| `DefaultMaxBitrateMbps`     | The ceiling a new share is given when the operator creating it names none. A share created with a ceiling of its own keeps that one.            | Megabits per second, stored on the record as bits per second | `null`  | At least 0.1 and at most 1000, refused by `BitrateCap` when the setting is read. Zero is refused rather than read as no ceiling.                                     | No ceiling. Delete the line rather than leaving it blank: an empty element is refused by the server's own serialiser before this plugin runs. |
| `GuestMaxActiveSessions`    | How many sessions one invited guest may hold at once. It is a ceiling on the account, so a guest invited to two shares carries one across both. | A count of sessions                                          | `5`     | At least 1 and at most 20, refused by `GuestPolicy` when the setting is read. Zero is refused rather than read as no ceiling.                                        | Not applicable; the value is a number and is never empty.                                                                                     |

## What is checked and what is not

The `Default` column carries the value the way C# spells it, and the test compares
it against the default a fresh `PluginConfiguration` actually holds. So a default
that moves in the class and not in this table is a red suite rather than a document
somebody trusts.

The other four columns are prose. Nothing reads them. A meaning, a unit, a bound or
an empty-value answer that is wrong here is wrong silently, and a reader who trusts
the test to have judged the whole row is trusting more than it did. That is the
limit of the guard rather than a gap somebody forgot to close, and the review is
where a wrong sentence in those columns is caught.

The bounds column is a claim about where the value is validated, and validation
lives with the routine that reads the setting rather than with the class that holds
it. For `PublicBaseUrl` that routine is `ShareLinkBuilder`, which refuses a value
that is not an absolute `http` or `https` URL rather than falling back to the
request.

## The two moments a value is refused

There are two, they catch different edits, and neither one covers the other.

The first is when the setting is read. Every setting is read by one routine and
that routine refuses a value outside what the setting admits: `ShareLinkBuilder`
for the base URL, `ShareBounds` for the four ceilings, `ShareConfiguration` for
the default lifetime, `BitrateCap` for the ceiling a new share is given and
`GuestPolicy` for the session ceiling. This is the moment that catches a file
edited by hand, because a hand edit passes through nothing else. What it costs is
that the refusal arrives later than the mistake: for a ceiling, the next time
somebody creates a share.

The second is when the configuration is saved. `Plugin.UpdateConfiguration` asks
`ShareConfiguration.Refuse` and throws when it answers, so a value saved through
the server is refused as it is written and the message names the setting. This is
the moment an operator experiences as validation, and it reaches nothing that did
not pass through the server.

`ShareConfiguration.Refuse` is where the whole file is judged at once. It asks
each of the routines above rather than comparing anything a second time, so what
it admits is exactly what the plugin will admit later, and a bound cannot be
loosened in one of the two places.

## What a hand-edited file refuses, and what it does not

A file edited into an invalid state stops the plugin doing the thing the bad
setting governs, and does not stop the things it does not govern. That division
is deliberate rather than incidental.

Creating a share reads the ceilings, the default lifetime and the default bitrate
ceiling, and building the link an operator copies reads the base URL, so an
invalid value in any of those refuses the create. Inviting a guest reads the
session ceiling and refuses in the same way.

Resolving a share reads none of them. `ShareResolution` is a function of the
records, the key, the token, the caller, the plugin's status and the clock, and
of no setting at all, so there is no ceiling it could apply an unknown value of.
Links an operator has already handed out go on working while a typo in the file
is fixed, which is the right way round: a share that is already live was created
under settings that were valid at the time, and refusing it would turn one bad
character into an outage for every guest.

## Where the settings still to come are decided

The four bounds arrived with #29, which is where each number is argued and where
the refusal that reads them lives. `docs/bounds.md` is the longer version of that
argument.

The default bitrate ceiling arrived with #62. The two units are fixed by the two
ends rather than by where the ceiling is enforced: an operator writes megabits per
second because that is the unit an uplink is sold in, and the record keeps bits
per second because that is the unit the server counts a rate in, so no conversion
sits between the stored number and the number a ceiling is compared against.
`BitrateCap` is where the conversion and the bounds live and
`docs/bitrate-cap.md` is where the enforcement point is argued and where #61's
answer is written down.

The session ceiling arrived with #56, which decided the number and where it is
enforced. It is written onto the invited account rather than counted by this
plugin, so what happens at the ceiling is the server's behaviour and not this
plugin's; `docs/guest-capabilities.md` is where that is argued and where the
consequence for a guest invited to two shares is written down.

The default lifetime and refusal on save both arrived with #71, which is also
where the two moments above were separated. The number seven is argued in
`ShareConfiguration` beside the constant rather than here.

The highest ceiling an operator may set is a constant in `BitrateCap` rather than
a setting, decided with the ceiling itself in #62 and argued there: its job is to
catch a value typed in bits per second, not to be tuned, and a bound that is
itself configurable is a bound that needs one.

Any setting that arrives later gets a row here when it lands, and the test is
what refuses one that does not.
