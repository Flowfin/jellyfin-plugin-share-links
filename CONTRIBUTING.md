# Contributing

Thank you for looking. This is a small plugin with one feature, and the fastest
way to get a change in is to read this page once.

## Report a vulnerability privately

If what you found is a security problem, stop here and read `SECURITY.md`. A
vulnerability filed as a public issue is a working exploit published for
everybody who reads this repository before it is fixed.

## Build it

```
dotnet restore
dotnet build --configuration Release --no-restore -warnaserror
```

The build needs the .NET 9 SDK. Warnings are errors on purpose, and the plugin
project runs analysers with documentation required, so a public member without a
comment does not compile.

## Test it

```
dotnet test --configuration Release
```

The test host runs on the 9.0 runtime, which the 9.0 SDK brings with it. If your
machine has only a newer runtime, the run stops with `You must install or update
.NET to run this application` before a single test executes, and this is the way
through without installing anything:

```
DOTNET_ROLL_FORWARD=LatestMajor dotnet test --configuration Release
```

The suite is written to need nothing but the SDK. No server, no network, no
display, no elevated rights, and no media file. That rule is not a habit, it is
written down in `docs/testing.md` along with what proves the suite obeys it and
which clauses no run can prove. Read that file before adding a test that wants a
real anything.

## Propose a change

Every change starts as an issue. An issue says what is wrong, what the evidence
is, and what done means, and if the evidence is a number it carries the command
that produced it. That is not ceremony: the issue is what the pull request is
reviewed against.

One topic per pull request. The body carries a reference to the issue it closes,
which a check reads and which is the one hygiene rule that holds a merge rather
than annotating it.

Sign your commits off. The Developer Certificate of Origin is in `DCO`, and the
sign-off is what asserts it:

```
git commit -s
```

Every commit in a pull request needs one, matching the commit's author. To fix a
branch that is missing them:

```
git rebase --signoff <base>
```

A commit message says what changed and what failure it prevents. Where it is a
correction, it says what was wrong and how it was found.

## What runs against your change

The workflows in `.github/workflows` are the authority for that, and they change,
so this page does not list them. The parity ledger, issue #11, is where they are
compared against the sibling gate this repository is being brought level with,
and where a check that is still owed names the issue that owes it.

Two of them will surprise you if nobody says so first. The greppable invariant
lint refuses patterns in source text this repository has decided may not appear,
and you can run it yourself:

```
bash .github/scripts/enforce-greppable-invariants.sh
```

The headless job runs the same suite with the network off and without privileges,
so a test that quietly needs either passes locally and reds there.

## What this repository is careful about

The plugin hands out a link that reaches one item for one invited guest. Most of
the rules here exist because of that one sentence. If a change makes a token
easier to guess, easier to log, easier to replay, or makes a share resolve for
somebody it does not name, it needs an argument in its pull request body and not
only a green check. The threat model, issue #23, is where those threats are
written down with the control that answers each one, and `docs/leaked-link.md`
is what a leaked link is worth and why the design says so.

## Conduct

Be civil and be specific. Disagree with the change rather than with the person
proposing it, and assume the person on the other side is trying to make this
better. There is no separate code of conduct document in this repository; this
paragraph is it.

## Licence

GPL-3.0, as it came from the plugin template and as it stays. Contributing means
your change is under that licence.
