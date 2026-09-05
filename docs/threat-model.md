# Threat model

This is the file issue #23 asks for. The plugin adds a route to a media server
that anybody on the network can reach, and the thing it hands an operator is text
that travels through channels nobody here controls. That earns a document rather
than a paragraph in the readme.

Read `README.md` first for what the feature is. Read this for what can go wrong
with it and what answers each one.

## What is in the tree

Thirty-seven files, two of which are routes:

    git ls-tree -r --name-only origin/master -- Jellyfin.Plugin.ShareLinks/ | wc -l
    37

    git grep -lE 'ApiController|ControllerBase|HttpGet|HttpPost' origin/master -- 'Jellyfin.Plugin.ShareLinks/*.cs'
    origin/master:Jellyfin.Plugin.ShareLinks/ShareLinksAdminController.cs
    origin/master:Jellyfin.Plugin.ShareLinks/ShareLinksGuestController.cs

This section said the opposite until 2026-08-17. It said the plugin's identity,
its dashboard page and the routine that mints a token were the whole of the code,
that there was no route, no share record and no store, and it pasted the second
command above with its exit status given as 1. I found it by re-running that
command rather than by reading the sentence around it.

Every row below names the tests that hold its control and the issue the control
was decided on. THIS PARAGRAPH SAID MOST ROWS STILL NAMED AN ISSUE RATHER THAN A
TEST, THAT THE TABLE THEREFORE UNDERSTATED WHAT RUNS, AND THAT RE-READING EVERY
ROW AGAINST THE TESTS THAT LANDED WAS NOT DONE HERE. It is done, at this commit.
Twenty-one rows carried an issue and no test; each was read against the suite and
now names the tests that bite for it:

    grep -cE '^\| *T[0-9]+' docs/threat-model.md
    26

    awk -F'|' '/^\| *T[0-9]+/ {print $5}' docs/threat-model.md | grep -c '`'
    26

    awk -F'|' '/^\| *T[0-9]+/ {print $5}' docs/threat-model.md | grep -oE '`[A-Za-z][A-Za-z0-9_]*`' | sort -u | wc -l
    71

Every number there moves with every row anybody edits, so re-run the commands
rather than reading the output beside them. `ThreatModelTests` refuses a name in
that column that no test in the assembly answers to, so a rename reds the suite
instead of leaving a backtick that reads like something that runs.

WHAT THIS IS NOT IS THE PASS #93 ASKS FOR, AND THAT PASS HAS BEEN MADE ONCE. It
confirms each proof PASSING at a release commit, reads what the built package
contains, checks the negative capability list on the assembled plugin rather
than on its parts, and reads the artefact's dependency list, its SBOM and its
scorecard. `docs/release-readiness.md` is where it is recorded, and its first
record is the reading of `0.1.0.0` at `0852955` on 2026-09-05: every name then
in this column was found passing on both lines, and two rows were found
crediting a test with more than it asserts, which that page names row by row.
What is done here is the comparison the pass inherited, which is which test
proves which row.

Two rows say less than the sentence beside them, and each says so in its own cell
rather than only here. T6's constant-time half is held by the greppable invariant
lint and by no test. T22's uninstall half runs no hook, so what is left on disk is
an operator's action.

T16 WAS THE THIRD AND WAS A DIFFERENT FAULT. Its control column claimed a bound on
sessions and devices per share, and the bound in the tree is on an account and
distinguishes no device, so the row disagreed with its own proof cell. A control
column that claims a bound the plugin does not have is the opposite direction from
the understatement this section is about and is the worse one, because an operator
reads it as coverage. #303 is where that was repaired, and its threat was re-read
against the corrected control rather than left pointing at a sentence that had
moved.

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

Every row carries a control and a proof. A proof is the tests that hold the
control, named so that `ThreatModelTests` can refuse one the assembly does not
carry, with the issue the control was decided on beside them; an issue number
here is where the argument was made and never a control that is owed. Nothing
accepted sits in this table;
the accepted residuals are the section after it, in words, so that a blank cell
can never be mistaken for an oversight.

| #   | Threat                                                                                                           | Control                                                                                                                                                              | Proof                                                                                                                                                                                                                                          |
| --- | ---------------------------------------------------------------------------------------------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| T1  | Somebody holding a link they were not sent opens it                                                              | The link names a record, never a caller. Identity comes from the server's request context, and both refusals give nothing away                                       | `ACallerTheShareDoesNotNameGetsNothing`, `AValidUnexpiredTokenFromACallerTheServerHasNotIdentifiedGetsNothing`, `EveryRefusalIsTheSameBytes`, decided on #24                                                                                   |
| T2  | A token is guessed, or the space is searched for live shares                                                     | 256 bits drawn from the operating system's cryptographic generator, in one routine, encoded without shortening                                                       | `AMintedTokenCarriesTheDeclaredEntropy`, `ALargeBatchOfTokensContainsNoDuplicate`, `AMintedTokenUsesOnlyTheDeclaredAlphabet`                                                                                                                   |
| T3  | Live shares are enumerated by asking, so a refusal tells an attacker which tokens are worth trying               | Every refusal on the guest route answers the same way, whatever the reason                                                                                           | `EveryRefusalIsTheSameBytes`, `AStoreThatCannotBeReadRefusesLikeEverythingElse`, `AnUnauthenticatedRequestIsRefusedBeforeTheGuestRouteReadsTheStore`, decided on #26                                                                           |
| T4  | Somebody reading the store learns tokens from it                                                                 | The store holds a keyed hash of the token and never the token                                                                                                        | `TheSerialisedFormCarriesNoRawToken`, `ASearchOfTheSerialisedFormWouldFindARawToken`, `NoMemberIsNamedForTheTokenItself`, decided on #43                                                                                                       |
| T5  | A stolen store is brute-forced offline into working tokens                                                       | The hash is keyed, and the key is not in the store                                                                                                                   | `TheSerialisedFormCarriesNoKeyMaterial`, `TheKeyNeverAppearsInThePluginConfiguration`, `AStoreFileOnDiskCannotBeUsedToReconstructALink`, decided on #28                                                                                        |
| T6  | The comparison of a presented token against the store leaks which prefix was right                               | Constant-time comparison, with the invariant lint refusing an equality or `Equals` comparison against anything named token, secret or hash                           | `AnyOtherPresentedTokenIsRefused`, `AValidTokenWithOneCharacterChangedResolvesNothingAtAnyPosition`, decided on #43. The timing half is the lint's and no test here asserts it                                                                 |
| T7  | A token that is revoked or expired still resolves                                                                | Revocation and expiry are read on every resolution rather than at redemption, in one routine that makes the whole decision                                           | `TheDecisionAnswersEverySituationTheSameWayEveryTime`, `TheRecordsStateIsDecidedBeforeTheCallerIs`, `RevocationIsTheReasonReportedForAShareThatIsAlsoExpired`, decided on #46, #45, #48                                                        |
| T8  | The same token is presented again, from a second device or a second address                                      | A token works as often as it is presented until it expires or is revoked, and a presentation is bound to neither a device nor a session                              | `TheSameTokenResolvesEveryTimeItIsPresented`, `ATokenThatHasWorkedStopsWorkingOnceTheShareIsRevoked`, #25                                                                                                                                      |
| T9  | A token reaches a log, a crash dump or an audit line, and the log becomes a working link                         | The never-log list, and the invariant lint refusing a logging call that names a token or a secret                                                                    | `NoLineCarriesTheRawToken`, `NoLineCarriesTheStoredHash`, `TheFieldsALineCarriesAreTheOnesThePolicyAllows`, decided on #27                                                                                                                     |
| T10 | The scope of a share widens, so a token reaches a second item                                                    | A token is bound to one item at mint time and the binding is not re-derived from the request                                                                         | `TheShareHandedBackIsTheOneTheTokenNames`, `TwoMintedTokensNeverResolveEachOther`, `TheSharedItemIsReachedByTheAccountTheShareNames`, decided on #44, #47                                                                                      |
| T11 | A guest reaches past the shared item into the rest of the library                                                | Confinement chosen deliberately rather than inherited, and the list of what a token can never reach, with a test per line                                            | `ThisPluginCarriesTheFilterTheComparisonChose`, `EachOfTheFiveWideningsIsRefused`, `AGuestWithNoLiveRecordLeftReachesNothingTheListJudges`, `EveryHeldLineNamesATestThatExists`, decided on #52, #47                                           |
| T12 | A guest's own account is left wider than it was, after the share ends                                            | Anything the plugin changed about the account is restored, and nothing is widened in the first place                                                                 | `TheRuleAboutWhichAccountsAreWrittenToIsHeldByATestThatCanFail`, `AnAccountThisPluginDidNotCreateIsNotConfined`, `AnInvitedAccountThisPluginDidNotMakeIsNotReleased`, decided on #58                                                           |
| T13 | Another user of the same server creates, lists or revokes shares                                                 | Every route is authorized explicitly, and the set of routes is proven closed rather than assumed                                                                     | `EveryControllerActionThisPluginExposesCarriesAnExplicitPolicy`, `JudgingAWholeAssemblyFindsEveryControllerAndRefusesExactlyTheBadOnes`, `EveryAdministratorActionIsReachedOnlyUnderTheServersOwnElevationPolicy`, decided on #69, #77         |
| T14 | A route ships reachable by anybody, by an attribute somebody forgot                                              | The invariant lint refuses an anonymous route, and the fixture that proves it bites is in the tree                                                                   | `TheGuardRefusesAnActionWhoseAuthorizationAttributeIsMissing`, `TheGuardRefusesAnActionMadeAnonymousByASubclassedAttribute`, `TheGuardRefusesAPolicyNameThatIsOneCharacterWrong`, decided on #69                                               |
| T15 | A stream that is playing continues after the operator revokes the share                                          | Revocation reaches the session and not only the record                                                                                                               | `RevokingSignsOutTheGuestsThisPluginMadeForTheShareAndNobodyElse`, `AGuestWhoStillHoldsAnotherLiveShareIsNotSignedOut`, #55 for what it does not reach                                                                                         |
| T16 | One invited guest's account holds more concurrent streams than the share was created for                         | A bound on how many sessions one invited guest's account may hold at once. No device is distinguished, and none is bounded                                           | `TheCeilingAnOperatorConfiguredIsTheOneTheGuestGets`, `AnAccountThatAlreadyCarriesAHigherCeilingIsNarrowedToTheConfiguredOne`, `TheCeilingIsPerAccountAndNothingHereTakesAShare`, decided on #56                                               |
| T17 | The link is built from what the request says the host is, so a forged header makes a link pointing elsewhere     | The link is built from a base the plugin holds rather than from anything the request supplied. Where that base comes from is #49's to decide and is not assumed here | `AForgedHostDoesNotReachTheLink`, `AForgedHostCarryingAPortAndAPathDoesNotReachTheLinkEither`, `AConfiguredValueThatIsNotUsableIsRefusedRatherThanFallenBackFrom`, decided on #49                                                              |
| T18 | An operator action, or a script driving one, fills the store                                                     | A bound on what one action creates and on what the store grows into                                                                                                  | `EveryBoundHasADefaultOnTheConfiguration`, `ACreatePastTheServerCeilingIsRefusedAndTheStoreDoesNotGrow`, `RetentionKeepsWhatStoppedInsideTheWindowAndDropsWhatDidNot`, decided on #29                                                          |
| T19 | A crash or two concurrent writes truncates the store, taking every live share with it                            | Write through a temporary file and rename, with writers serialised                                                                                                   | `AWriteThatDiesPartwayLeavesThePreviousRecordsIntactAndReadable`, `AWriteThatDiesPartwayLeavesNothingBesideTheStore`, `EveryRecordWrittenByManyTasksAtOnceSurvives`, decided on #35                                                            |
| T20 | An upgrade meets records written by an older version and loses them, or a downgrade guesses at newer ones        | A schema version in the store, a forward migration, and a refusal rather than a guess when the store is newer                                                        | `WhatThisPluginWritesCarriesTheStoreVersion`, `AStoreFromBeforeTheStampIsReadAndMigratedRatherThanRefused`, `AStoreFromANewerPluginIsRefusedAndSaysBothNumbers`, decided on #37                                                                |
| T21 | Restoring a backup brings revoked or expired shares back to life                                                 | The three cases a restore produces, each with a defined outcome and operator guidance, in `docs/backup-restore.md`                                                   | `ARestoredStoreBringsBackAShareThatWasRevokedAfterTheBackup`                                                                                                                                                                                   |
| T22 | Records outlive the plugin, or survive an uninstall the operator believed removed them                           | What disabling and uninstalling do, and what is left on disk                                                                                                         | `APluginThatIsNotActiveRefusesALiveShare`, `APluginThatIsNotActiveDoesNotReachTheKeyAtAll`, decided on #38. Those are the disabling half; no hook runs on an uninstall, so what is left on disk is an operator's action and no test reaches it |
| T23 | A record points at an item or an account that is gone, and the failure is a null reference rather than a refusal | Each case has a defined outcome, and the administrator view shows a share that can no longer resolve as such                                                         | `TheListingCarriesEveryRecordAndSaysWhichOfThemStillResolve`, `AShareWhoseItemTheServerNoLongerHoldsIsRefusedByTheDecision`, `AStoreThatCannotBeReadIsAnErrorRatherThanAnEmptyListing`, decided on #39                                         |
| T24 | The plugin holds more about an invited guest than the feature needs, for longer than it needs                    | What personal data is held, and for how long, stated rather than accumulated                                                                                         | #31, `EveryFieldOfTheRecordHasARow`, `RemovingAShareLeavesNothingInTheFileNamingItsGuest`                                                                                                                                                      |
| T25 | Expiry is wrong at the boundary, and nobody notices because the tests sleep                                      | The clock comes from a seam, refused by the invariant lint when it does not, and boundaries are covered without waiting                                              | `AClockWalkedAcrossTheInstantChangesTheAnswerAtTheInstantAndNotBesideIt`, `TheClockThePluginRegistersIsTheOneThatDoesNotStepBackwards`, `AShareOnceRefusedAsExpiredIsStillRefusedAfterTheClockStepsBackwards`, decided on #36, #79             |
| T26 | The bitrate ceiling is enforced somewhere a guest can step around                                                | The ceiling is enforced at the point where the stream is actually decided, which #61 settles by measurement rather than by preference                                | `ThePlaybackInformationRequestIsLoweredToTheCeilingInForce`, `AStreamRequestAboveTheCeilingIsRefused`, `TheStreamBoundaryIsWalkedAtEachCeiling`, decided on #61                                                                                |

## The reuse rule

A token works as often as it is presented, until the instant the record names or
until the share is revoked, whichever comes first. It is not burned on first use.

Burning it would break the ordinary case rather than an attack. An invited guest
opens the link on a phone, pauses, and opens it again on a television. That is one
guest doing the thing the share was created for, and a token that stopped after
the phone would read to them as a broken link. `docs/expiry.md` reaches the same
case from the other side when it refuses a lifetime measured from first use.

A presentation is bound to neither a device nor a session. Nothing about either is
an input to the decision: `ShareResolution.Resolve` takes the records, the key, the
token, the account the server identified the caller as, the plugin's status and the
clock, and that is the whole of it. So the same token twice in one session and the
same token twice in two sessions are one case here, and they are one case by
construction rather than by a check that treats them alike.

Nothing is written when a token is presented. Every property of a share record is
set at construction and never after, so a resolution has nowhere to record that one
happened. Reuse is allowed by the shape of the record as well as by the decision,
and burning a token on first use would be a change to both rather than a line
somebody could add to the routine alone.

The same token arriving from two addresses at once resolves for both, where both
callers are accounts the record names. The plugin never sees an address and never
compares one. A share names a set of accounts, which is decision 5 in #94, so two
people in one household watching at once is the feature rather than a case to be
caught; what bounds how many of them there may be is #56 and not this rule.

Revocation is what stops a token that has been working, on the next presentation
rather than at the next restart, and expiry does the same at the instant the record
names. Neither depends on how often the token worked before.

What an operator sees when a token is presented again is a second resolved line
naming the same share, and nothing that separates the two callers. `docs/logging.md`
is where that was decided (#27): a line carries the share and never the account, so
an operator still cannot tell one guest who opened the link twice from two guests
who opened it once, which is stated here rather than left to be found. The server's
own session record is where that question is answered, and it is not this plugin's.

## Accepted, and why

These are threats this plugin does not answer. Each one is here because it was
considered and left, not because nobody thought of it.

A guest who is entitled to watch can hand their own session to somebody else, or
point a camera at the screen. No token model prevents either. The share controls
who may start a session, and after that the media is out. This is the residual
issue #25 asks to be stated in a sentence, and it is the reason T1 is about text
rather than about media.

Reuse is not observable, which is the residual the rule above leaves. Presenting a
token again is allowed, no address is compared and no line is written, so an
invited account that passes its own sign-in on is a case nothing here detects. It
is the paragraph above in a second form, and #56 bounds how much of it one share
can carry rather than telling the two apart.

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

It does not decide anything. Every row points at the issue that decides its half.
THIS PARAGRAPH SAID SEVERAL OF THOSE WERE STILL WAITING ON #94 - how a guest comes
to have an account, how they are confined, and whether they may download rather
than only stream - AND ALL THREE WERE ANSWERED BEFORE THIS FILE LANDED. The tree
carries each answer:

    git grep -n 'The plugin creates the account' -- docs/guest-accounts.md
    docs/guest-accounts.md:43:### The plugin creates the account with the invitation
    docs/guest-accounts.md:60:The plugin creates the account, and it owns that account end to end. This is

    git grep -n "authorization filter of this plugin's own" -- docs/guest-confinement.md
    docs/guest-confinement.md:62:## Candidate two: an authorization filter of this plugin's own

    grep -n 'Download it' docs/guest-capabilities.md
    21:| Download it                        | refused | `EnableContentDownloading` false            |

and the issue that collected the three is closed:

    gh issue view 94 --json state,closedAt --jq '"\(.state) \(.closedAt)"'
    CLOSED 2026-08-11T18:31:52Z

So no row is owed twice over. This was recorded on #93 on 2026-08-08, one day
after the file landed, and left to #140 and #84 to repair; both closed with the
sentence still standing, which is why it is repaired here instead of there.

It does not enumerate the checks. What runs is what the workflows run, and
`docs/parity-ledger.md` is where the gate is compared against the one this
repository is levelling with.

Nothing refuses a stale row here, and the half that is now refused is the smaller
one. `ThreatModelTests` reds when a row names a test this assembly does not carry,
so a rename cannot leave a backtick standing where the thing it named is gone.
What no route reads is whether a test a row names still proves the control beside
it: a control that moves while its test goes on compiling leaves every route green
and the file quietly wrong. That is a judgement about meaning, and the
release-readiness pass is where this file is read again with it in mind:
`docs/release-readiness.md` carries each reading, and the first, at `0852955`,
found two rows crediting a test with more than it asserts and no control unheld.
The security page issue #84 was the other such place and has closed:

    gh issue view 84 --json state --jq .state
    CLOSED
