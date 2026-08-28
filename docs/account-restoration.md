# Putting a guest's account back

Issue #58 asks for every change this plugin makes to a guest's account to be
additive and reversible, for an account never to be widened, and for it to be put
back as it was when the last share touching it ends. Reversing a change means
knowing what the account was before it, and where that knowledge is kept was
decided nowhere in the tree. This page decides it.

Everything below about the server was read out of the packages this plugin
compiles against, at the version `Directory.Build.props` pins:

    grep -n 'JellyfinVersion' Directory.Build.props
    15:        <JellyfinVersion>10.11.11</JellyfinVersion>

## What this plugin writes onto an account

One routine writes the policy, and it writes every switch rather than only the
ones whose default is wrong:

    git grep -n 'public static void Apply' -- Jellyfin.Plugin.ShareLinks/GuestPolicy.cs
    Jellyfin.Plugin.ShareLinks/GuestPolicy.cs:181:    public static void Apply(UserPolicy policy, int maxActiveSessions)

Most of those switches narrow. Three of them do not, and they are why this page
exists rather than a detail inside it. Playing media, reaching the server from
outside the operator's network, and transcoding are all turned on, because a
guest who cannot play the shared item over the internet has been given nothing.
On an account that had any of the three switched off, writing that policy is a
widening.

That is asserted rather than described.
`AccountRestorationTests.ApplyingTheGuestPolicyToAnAccountThatWasNarrowedWidensIt`
hands the routine a policy with those three off and requires all three to come
back on, so the premise this whole page rests on reds the suite the day the
policy stops widening and the argument here has to be read again.

The session ceiling is the exception, and it is an exception in the narrow
direction only. An account already carrying a lower one keeps it, which is #56's
and which `GuestSessionCeilingTests` holds.

## The two places the prior state could have lived

It could live on the share record, beside the accounts the record invites. That
puts it somewhere the store already keeps, already versions and already migrates,
and it needs no second file.

What it costs is that two live shares naming one account carry two copies of what
that account used to be, written at two different moments, and the second copy
records a state this plugin had already changed. Restoring from it restores a
fiction, and nothing in the shape says which of the two copies is the real one.
`docs/share-store.md` refuses the plugin configuration file as a home for share
data partly for this reason, and it bites harder here, because the disagreement
is not between a document and a store but between two records inside one store.

Or it could be a store of its own, keyed by account rather than by share, with
the account's lifetime rather than the share's. That answers the disagreement,
because there is then one row per account and one moment it was written.

What it costs is a second file with its own schema, its own migration and its own
way of going stale. A row for an account somebody removed from the server is a
row nothing ever collects. A row that is missing cannot be told apart from an
account that never needed one, so the safe reading of silence has to be picked in
advance, and both readings are wrong in a case somebody will meet: read as
nothing to restore, it leaves an account widened; read as an account to put back,
it takes permissions off an account this plugin never touched.

## The decision

Neither, because nothing is recorded that would ever have to be put back.

This plugin writes a policy onto an account it created and onto no other account.
An account it created has no prior state worth keeping: before the invitation the
account did not exist. So the prior state of every account this plugin may write
to is one sentence, the same sentence for all of them, and a fact that is
identical for every subject is not a thing a store holds. It is a rule.

What makes the rule checkable is already on the record. `PluginCreatedUserIds`
says which of a record's invited accounts this plugin made, and
`WasCreatedByThisPlugin` answers yes only where the record both invites the
account and claims to have created it:

    git grep -n 'public bool WasCreatedByThisPlugin' -- Jellyfin.Plugin.ShareLinks/ShareRecord.cs
    Jellyfin.Plugin.ShareLinks/ShareRecord.cs:326:    public bool WasCreatedByThisPlugin(Guid userId) =>

#144 landed that predicate as the gate on removing an account. This page makes it
the gate on writing a policy as well. They are one gate for one reason: an
account this plugin did not make belongs to somebody who did, and both deleting
it and rewriting what it may do are things done to that person rather than to a
guest.

## What restoring an account then means

What `docs/guest-accounts.md` already describes, and no mechanism beyond it. When
the last live share naming an account has ended, the account is disabled. When
the last record naming it is deleted under the retention rule, the account is
deleted with it. Putting the account back and taking it away are one act here,
because the state it is being put back to is not existing.

Disabling is a write onto the same policy, `IsDisabled`, and it is allowed for
the same reason every other write is: the account is one this plugin made.

## What an operator does not get

An operator who wants to share with an account that already exists on their
server does not get that account narrowed for the share and restored afterwards.
Under this decision the plugin writes no policy onto it at all.

A route offering that kind of invitation therefore has two shapes and not three.
It creates a guest account, as every other invitation does, and the operator's
own account is not involved. Or it names the operator's account in the record and
changes nothing about it, in which case what that account may do during the share
is whatever the operator already allowed and the share confines nothing.

That constraint is written here rather than left to #67, because the create route
is where it would otherwise be settled in passing by whoever writes it.

## What this does not cover

This plugin now changes an account, and this section said it did not. The create
route in #67 landed, and it reaches the interface the earlier text records as
absent:

    git grep -ln 'IUserManager' -- Jellyfin.Plugin.ShareLinks
    Jellyfin.Plugin.ShareLinks/ShareLinksAdminController.cs

`AccountRestorationTests.ThisPluginTouchesNoUserDataYet` was the tripwire for
exactly that moment and it fired, which is what it was for. It watched both
interfaces under the name `ThisPluginWritesToNoAccountYet` until `fd319d8`
narrowed it to the half that still holds and renamed it with the claim, so the
older name is the history of this paragraph and is not in the suite. The rule
above is no longer standing in front of code; it is standing behind one call, and
what holds it is that the identifier reaching `UpdatePolicyAsync` is one
`CreateUserAsync` returned in the same call. That is a fact about which value reaches a call rather
than about which types an assembly names, so no reading of the compiled metadata
can see it. It is held by
`ShareCreationTests.ThePolicyIsWrittenOntoTheAccountsTheCreateMadeAndOntoNobodyElse`,
which drives the route against a server that already holds somebody else's
account and requires that account's policy to be untouched.

`IUserDataManager` is still reached nowhere, and the tripwire that survives says
so. That is the other half of the original one: playback state, favourites and
what a person has watched are a different account surface from the policy, and
nothing here writes to it.

The one write that is not a policy write is the credential this plugin mints for
an account it just made, handed to the server by `ChangePassword` and kept
nowhere here. It is not prior state and there is nothing to restore: before the
invitation the account did not exist, which is this page's whole argument.

The two conditions #58 closes on are not met by this page and it does not close
it. Both are tests over an account that was changed and then either left alone or
put back, and there is neither an account nor a change. What this settles is the
question those two tests would otherwise have had to answer first, and settling
it before the first policy write is the whole point of writing it now.

An account an operator narrows or widens by hand while a share is live is outside
all of this. The plugin never reads a policy back, so a change made outside it is
neither noticed nor undone.

Whether the server honours a policy at all is the server's business and is
asserted by nothing here. `GuestPolicy` says the same about itself, and this page
inherits it rather than repeating the argument.
