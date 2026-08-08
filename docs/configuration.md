# Configuration reference

Every setting this plugin holds. The list is taken from `PluginConfiguration`
rather than from memory, and `ConfigurationReferenceTests` compares the two in both
directions, so a setting added without a row here reds the suite, and so does a row
naming a setting that no longer exists.

## The settings

| Setting         | Meaning                                                                                                     | Unit                                            | Default | Bounds                                                                                                     | When it is empty                                                                                                                             |
| --------------- | ----------------------------------------------------------------------------------------------------------- | ----------------------------------------------- | ------- | ---------------------------------------------------------------------------------------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------- |
| `PublicBaseUrl` | The address this server is reached at from outside. It is the host the link an operator copies is built on. | An absolute URL, written with no trailing slash | `""`    | Absolute, and `http` or `https`. A value that is anything else is refused and no link is produced from it. | The link is built from what the request claimed. That is text a caller supplies, so a forged header produces a link pointing somewhere else. |

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

This table has one row because the class has one property. The settings the earlier
milestones decide arrive in #71, which collects the maximum and default lifetime,
the bitrate ceilings, the live share ceiling, the retention rule and the session
ceiling into the same class. Each of them gets a row here when it lands, and the
test is what refuses one that does not.
