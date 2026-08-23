# The fuzz harness

Coverage-guided fuzzing of the one input this plugin takes from somebody it has
not yet decided anything about (#19). `ShareLinksGuestController` takes the token
as a path segment the caller writes, and hands it to `ShareResolution.Resolve`.
That routine is what this harness drives.

`.github/workflows/fuzz.yml` is the job. It runs weekly and on dispatch, never on
a pull request, so it holds no merge.

## What it holds, and what it cannot

There is no decode step in front of the target and there is not meant to be. The
presented token is checked for null or empty and its UTF-8 bytes go into a keyed
hash. A length or alphabet check ahead of that comparison would make a
wrong-shaped token refuse more cheaply than a right-shaped one that names
nothing, which is what #26 rules out.

So the target holds a property rather than watching a parser, in three parts:

- no input throws
- no input resolves a share
- every non-empty input is refused with the same reason as every other

The third is the one that would notice a shape check appearing in front of the
comparison. The first is what a fuzzer is ordinarily for.

What it cannot hold is that two refusals **cost** the same. Time is not something
a libFuzzer oracle can read, and `ShareLookupCostTests` is where the comparison's
shape is judged instead.

## The seed corpus

`corpus/` is committed and is derived rather than written. `ShareTokenCorpus`
computes every seed from `ShareTokens.Alphabet` and `ShareTokens.EncodedLength`,
so the day the encoding changes the seeds change with it instead of going on
seeding the old shape while looking maintained.

Regenerate after changing the derivation:

```
dotnet run --project Jellyfin.Plugin.ShareLinks.Fuzz -- emit Jellyfin.Plugin.ShareLinks.Fuzz/corpus
```

`FuzzCorpusTests` compares the committed bytes against the derivation on every
pull request, so a seed edited by hand and a derivation edited without
regenerating both red a merge.

The seeds are marked binary in `.gitattributes`. A seed exists to be the exact
input the target receives, and a line ending normalised on the way into git is a
different input than the one that was committed. One of them is deliberately not
valid UTF-8, which is also why `unicode-guard.yml` has to see them as bytes.

## A crasher is a finding, never a harness patch

If a scheduled run writes a reproducer, download the `fuzz-reproducers` artifact,
minimise it, and open it as a finding against the plugin. Do not make the harness
stop reporting it. If the reproducer turns out to be exploitable, `SECURITY.md`
is the private route and a public issue is a published exploit.

## Why the job's green runs are worth reading

A scheduled job whose target has been renamed away, or whose oracle asserts
nothing, reports exactly what a clean run reports. Every earlier reading on #19
refused to land a harness for that reason. Three things stand against it here.

The suite compiles this project and runs its target over every committed seed on
each pull request, so a rename reds a merge rather than a Sunday.

`FuzzTargetOracleTests` feeds the target the two inputs that break its property
and requires it to say so. The fixture the harness holds is built so that no
input can reach those cases, which is exactly why they are asserted somewhere
they can be.

And `rehearsal/plant-an-index-into-the-presented-token.patch` puts a defect on
the path the job fuzzes: a read at a fixed offset into the presented token
without asking how long it is, which throws for anything shorter. Dispatch
`fuzz.yml` with `plant` set and the job applies it, fails closed if it no longer
applies, and reds if the run does **not** find it. Nothing else applies that
patch, so no build a reader could install carries it.

## The means

C# in this repository, driven by SharpFuzz under libFuzzer on Linux. The target
is a routine in this plugin's own assembly, so a harness in any other language
would be calling across a boundary that does not exist; SharpFuzz is what makes a
managed delegate something libFuzzer can drive, and its instrumentation CLI and
the libFuzzer runtime work on Linux alone, which is why the job is Linux-only and
why nothing about it runs on a developer's machine. The cost paid knowingly is
one NuGet package, one dotnet tool, and a native shim compiled with clang inside
the job.

The same reading is what keeps the job off the pull request path. It is minutes
of compute and its verdict moves for reasons a change did not cause, so as a
required check it would be one people learn to ignore.
