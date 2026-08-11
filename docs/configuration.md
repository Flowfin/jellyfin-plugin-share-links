# Configuration reference

Every setting this plugin holds. The list is taken from `PluginConfiguration`
rather than from memory, and `ConfigurationReferenceTests` compares the two in both
directions, so a setting added without a row here reds the suite, and so does a row
naming a setting that no longer exists.

## The settings

| Setting                     | Meaning                                                                                                                              | Unit                                                         | Default | Bounds                                                                                                                                   | When it is empty                                                                                                                              |
| --------------------------- | ------------------------------------------------------------------------------------------------------------------------------------ | ------------------------------------------------------------ | ------- | ---------------------------------------------------------------------------------------------------------------------------------------- | --------------------------------------------------------------------------------------------------------------------------------------------- |
| `PublicBaseUrl`             | The address this server is reached at from outside. It is the host the link an operator copies is built on.                          | An absolute URL, written with no trailing slash              | `""`    | Absolute, and `http` or `https`. A value that is anything else is refused and no link is produced from it.                               | The link is built from what the request claimed. That is text a caller supplies, so a forged header produces a link pointing somewhere else.  |
| `MaxLiveShares`             | How many shares may be live across the whole server at once. Live means neither revoked nor past its expiry.                         | A count of shares                                            | `100`   | At least 1. A create that would pass the ceiling is refused by `ShareBounds`, and a value below 1 is refused when the bounds are read.   | Not applicable; the value is a number and is never empty.                                                                                     |
| `MaxLiveSharesPerItem`      | How many live shares may name one item.                                                                                              | A count of shares                                            | `10`    | At least 1, refused in the same place and in the same way as the ceiling above.                                                          | Not applicable; the value is a number and is never empty.                                                                                     |
| `MaxShareLifetimeDays`      | The longest lifetime a link may be given, measured from when the share is created.                                                   | Days                                                         | `30`    | At least 1. Checked when a share is created and never when one is resolved, so lowering it leaves links already handed out alone.        | Not applicable; the value is a number and is never empty.                                                                                     |
| `ExpiredShareRetentionDays` | How long a share that has stopped working is kept before it is deleted, counted from the instant it stopped.                         | Days                                                         | `90`    | At least 0. Zero deletes it at the first write after it stopped working, which is how an operator empties the store of what has expired. | Not applicable; the value is a number and is never empty.                                                                                     |
| `DefaultMaxBitrateMbps`     | The ceiling a new share is given when the operator creating it names none. A share created with a ceiling of its own keeps that one. | Megabits per second, stored on the record as bits per second | `null`  | At least 0.1 and at most 1000, refused by `BitrateCap` when the setting is read. Zero is refused rather than read as no ceiling.         | No ceiling. Delete the line rather than leaving it blank: an empty element is refused by the server's own serialiser before this plugin runs. |

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
request. Server-side refusal on save is #71 and does not exist yet, so a value
edited into the configuration file by hand is refused when a link is built and not
when it is written.

## Where the settings still to come are decided

The four bounds arrived with #29, which is where each number is argued and where
the refusal that reads them lives. `docs/bounds.md` is the longer version of that
argument.

The default bitrate ceiling arrived with #62, after #61 decided that the ceiling
is written onto the invited account rather than enforced in the request path.
That decision is what fixes the two units: an operator writes megabits per second
because that is the unit an uplink is sold in, and the record keeps bits per
second because that is what the account switch takes. `BitrateCap` is where the
conversion and the bounds live and `docs/bitrate-cap.md` is where the enforcement
point is argued.

The rest are still owed. #71 collects the default lifetime and the session
ceiling into the same class, and it is also where refusal on save arrives: today
an invalid value is refused by the routine that reads it, which is a later moment
than the operator typing it. Each of those settings gets a row here when it
lands, and the test is what refuses one that does not.
