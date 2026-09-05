# The release-readiness pass

This is the pass issue #93 asks for: one deliberate reading, made at a release
commit, of the threat model against the code that exists, of every proof it
names, of what the built package contains, of the negative capability list on the
assembled plugin rather than on its parts, and of the artefact's dependency
list, its bill of materials and its scorecard. It is not a new control. Every
subject below is already judged by a workflow or a test; what the pass adds is
that they are read together, at one commit, by somebody looking for what they do
not cover.

The pass is recorded in the tree because two of its subjects do not stay
readable anywhere else: the bill of materials is a workflow artefact with a
fourteen-day life, and a scorecard result is replaced by the next one. Each
record below carries the commands that produced it, run against the reference
the record names and never against a working tree. Re-run them rather than
reading the output beside them.

## What the pass reads

**The proofs.** Every test the proof column of `docs/threat-model.md` names,
confirmed passing at the release commit by name and not by the suite's total.
`ThreatModelTests` already refuses a name no test answers to; what it cannot
read is whether the test still proves the control beside it, so the pass reads
each row's tests against its control and writes down where the two have
drifted.

**The package.** What the archive holds, what the assembly inside it references,
which server line it declares, and whether the archive is the one the workflow
attests to having built.

**The assembled plugin.** The suite judges the plugin assembly it was built
beside. The pass puts the shipped assembly in that assembly's place and runs the
suite again, so that the route policy, the negative capability list and the
threat model are judged on the bytes an operator installs.

**The supply chain.** The artefact's dependency list read off the assembly, the
bill of materials the packaging run wrote for the same commit, that bill
queried against a vulnerability database, and the scorecard.

**The documents.** Whether what the tree says about the release is true of the
release that exists.

## 0.1.0.0, read on 2026-09-05

### The release commit

The tag names the merge of #348, and the release was published from it:

    git ls-remote --tags origin
    c465112dc97dd23fadb4a17e043f9ea7aaec8397    refs/tags/0.1.0.0-stable
    08529552a2fa283724cdfd573eeecad04202c850    refs/tags/0.1.0.0-stable^{}

    gh release view 0.1.0.0-stable --json tagName,publishedAt --jq '"\(.tagName) \(.publishedAt)"'
    0.1.0.0-stable 2026-09-04T15:53:06Z

Every push-triggered workflow at that commit ended green: build, test, ci,
invariants, headless, coverage, CodeQL, package, scorecard, prettier,
unicode-guard, zizmor and the publish itself:

    gh run list --commit 08529552a2fa283724cdfd573eeecad04202c850 --limit 30 \
      --json name,conclusion --jq '[.[] | select(.conclusion != "success")] | length'
    0

Three workflows run on a schedule and none of them ran at that commit. The
interoperability matrix ran at `68209cd` the morning before and at `2268bfe` the
evening after; the fuzz run is from `45b57ee` on 2026-09-02 and the mutation run
from `b00bba1` on 2026-08-30. `docs/RELEASING.md` names a green matrix at the
commit being released as a release condition, and that condition was not met
as written: nothing on the publish route reads it, which that page also says,
and the release was cut between two green runs on neighbouring commits rather
than after one on its own.

### The proofs, at the release commit

Built and run from a checkout of `0852955`, with SDK `10.0.400` and only the
.NET 10 runtime installed, so the `net9.0` leg ran under
`DOTNET_ROLL_FORWARD=LatestMajor`:

    dotnet test -c Release --no-build --nologo --logger trx
    Passed!  - Failed:     0, Passed:  1289, Skipped:     0, Total:  1289 - Jellyfin.Plugin.ShareLinks.Tests.dll (net10.0)
    Passed!  - Failed:     0, Passed:  1289, Skipped:     0, Total:  1289 - Jellyfin.Plugin.ShareLinks.Tests.dll (net9.0)

The threat model at that commit carries 26 rows naming 71 distinct tests, by
the two commands the file itself gives. Each of the 71 names was looked up in
both result files and each was found with the outcome `Passed`, on both lines.
A total of 1289 says nothing about a named test; the lookup is what confirms
the clause.

### Each row, read against its tests

I read the body of every one of the 71 tests against the control in its row.
In 24 rows the tests assert the control as the row states it. Two rows say
something their tests do not, and neither is a gap in a control:

- T12 names `TheRuleAboutWhichAccountsAreWrittenToIsHeldByATestThatCanFail`,
  which asserts by reflection that another test exists. The test that holds
  the control is the one it points at,
  `ThePolicyIsWrittenOntoTheAccountsTheCreateMadeAndOntoNobodyElse`, and the row
  does not name it. The control is held; the row is one step removed from the
  test that holds it.
- T14's control says the invariant lint refuses an anonymous route. Its three
  named tests drive `RoutePolicy`, a judge in the test assembly, over fixtures;
  the lint's own refusal is `route-is-not-anonymous` in
  `.github/scripts/enforce-greppable-invariants.sh`, and no test holds that
  one. Two guards refuse the same shape and the row credits one with the other's
  proof.

T6 and T22 say in their own cells what their tests do not hold, and the reading
found nothing beyond what the cells already say.

### The package

Four assets, and the two checksum files agree with the archive:

    gh release download 0.1.0.0-stable
    md5sum share-links_0.1.0.0.zip
    6a3261b3e4b6ab6bd4de787d994aa0bb  share-links_0.1.0.0.zip
    sha256sum share-links_0.1.0.0.zip
    c67e1b27048a3c6c5ed151fd1b6422a2af63c86b2560c220b76463c9ad929132  share-links_0.1.0.0.zip

Both checksums were written by the run that built the archive, so agreement
proves the download and nothing about the build. What proves the build is the
provenance statement, which names the archive by the same digest, the workflow
that built it, the run, and the commit:

    gh attestation verify share-links_0.1.0.0.zip --repo Flowfin/jellyfin-plugin-share-links --format json
    predicateType  https://slsa.dev/provenance/v1
    subject        share-links_0.1.0.0.zip  sha256 c67e1b27...929132
    builder        .github/workflows/publish.yaml@refs/tags/0.1.0.0-stable
    invocation     actions/runs/33891908949/attempts/1
    gitCommit      08529552a2fa283724cdfd573eeecad04202c850

The archive holds two entries and no other assembly:

    unzip -l share-links_0.1.0.0.zip
         1230  meta.json
       148992  Jellyfin.Plugin.ShareLinks.dll

`meta.json` declares `version` `0.1.0.0`, `targetAbi` `10.11.9.0` and the
plugin's permanent guid, and the packaging run's own check reported that the
package carries the plugin assembly and nothing else.

The assembly, read with `System.Reflection.Metadata`: assembly version
`0.1.0.0`, informational version `0.1.0.0+0852955...`, target framework
`.NETCoreApp,Version=v9.0`, configuration `Release`, and a `Reproducible`
entry in its debug directory. It references 22 assemblies: 17 of the .NET 9
framework at `9.0.0.0`, and five of the server at `10.11.9.0`, which are
`MediaBrowser.Controller`, `MediaBrowser.Model`, `MediaBrowser.Common`,
`Jellyfin.Data` and `Jellyfin.Database.Implementations`. So the artefact's
dependency list is the server it declares in `targetAbi` and the framework,
and nothing else. Nothing is strong-named.

**Not reproduced here.** A build of the same commit on this machine, with the
publish route's own arguments, produced a different assembly: 149504 bytes
against 148992. The run built with SDK `9.0.x` and this machine holds
`10.0.400`, so the compiler differed, and the claim in `docs/versioning.md`
that one commit builds to one assembly was not measured across SDKs. It was
neither confirmed nor refuted by this pass.

### The assembled plugin

The shipped assembly was copied over the built one in the test output
directory and the `net9.0` suite run again without rebuilding:

    sha256sum Jellyfin.Plugin.ShareLinks.Tests/bin/Release/net9.0/Jellyfin.Plugin.ShareLinks.dll
    efc32a48ab577898851edb34cc8fb0b8c92288ce52bf89557a3b0009860ef1a3
    dotnet test Jellyfin.Plugin.ShareLinks.Tests -c Release -f net9.0 --no-build --nologo
    Passed!  - Failed:     0, Passed:  1289, Skipped:     0, Total:  1289 - Jellyfin.Plugin.ShareLinks.Tests.dll (net9.0)

That digest is the assembly inside the archive. So every test that judges
`typeof(Plugin).Assembly` judged the bytes an operator installs: the route
policy over every controller, the single confinement filter, the negative
capability list, and the threat model's proof column. The verdict split of
`docs/negative-capabilities.md` at that commit, by the command the page gives,
was 4 held, 3 held in part and 3 not held, and `docs/security.md` carried 12
held, 6 held in part and 7 not held by a test here.

What that run does not say: tests that read source files or documents read the
checkout, not the archive, and the archive holds no source. It confirms the
assembly; it does not confirm that the checkout is the one the assembly came
from, which is the provenance statement's job above.

### The bill of materials

Written by the packaging run at the release commit, run `33891883336`, as the
artefact `plugin-sbom`, and the run's own refusal was green:

    The bill is of version 0.1.0.0, names 36 components, names no package twice, and names nothing the project references for build time only.

    sha256sum bom.json
    f2b18af5a0fa9b99565085a7701760118628f9d66a11fed10c6103e68388a9bc  bom.json

CycloneDX 1.7, subject `Jellyfin.Plugin.ShareLinks` at `0.1.0.0`, direct
dependencies `Jellyfin.Controller` and `Jellyfin.Model` at `10.11.9`, and 34
packages reached through them. The digest is here because the artefact expires
on 2026-09-18 and nothing attaches it to the release, which #93 recorded on
2026-09-02 and which stands.

Every component was queried against the OSV database on the day of the pass:

    curl -s -X POST https://api.osv.dev/v1/querybatch -d @queries.json
    queried 36 components, 0 with a known vulnerability

The bill describes the host closure the plugin compiles against. The archive
carries one assembly and none of those 36 packages, so the bill over-states
what an operator installs and under-states nothing; whether it should describe
the closure or the one assembly is the question #93 left open on 2026-09-03 and
this pass does not decide it.

### The scorecard

The scorecard run at the release commit, run `33891883392`, reported six checks
below full marks: branch protection at 3, pinned dependencies at 9 because a
`nuget` command is not pinned by hash, fuzzing at 0, code review at 0 with no
approved changeset in fifteen, maintained at 0 because the repository is
younger than ninety days, and best-practices badge at 0. The published result
of the morning after, at `bd4c823`, scored 6.2 and added two: signed releases
at 0, and packaging not detected.

Two of those are the scorecard failing to see what is there, and they are
recorded so nobody repairs the plugin to satisfy the reading. Fuzzing at 0
stands beside `.github/workflows/fuzz.yml`, which runs weekly; the scorecard
recognises a fixed set of fuzzing services and this harness is not one of them.
Signed releases at 0 stands beside the provenance statement verified above; the
scorecard looks for a provenance file among the release assets, and this
release's statement lives in the forge's attestation store instead. The other
four are true of this board and are decisions rather than defects: one account
merges its own changes, the ruleset is what it is, the repository is new.

### The documents

Six passages in the tree at the tip of `master` said, the day after the
release, that nothing is published. Three of them were the first three entries
of `docs/limits.md` and are repaired in the change that lands this record; the
other four are in `README.md`, `docs/api.md`, `docs/catalogue-checklist.md`
and `docs/operator-guide.md`, and #362 holds them.

The note fragment that became the release's body was still in `changelog.d`
the day after the release, because the first release raised no version and the
deletion `docs/RELEASING.md` prescribes hangs on the raise. #363 holds that,
and the deletion lands beside this record.

The published archive carries the configuration-page defect #349 found on the
walk of 2026-09-04: the page renders and does nothing. The repair is on the
mainline and in no release, which `docs/limits.md` now says where an operator
reads it.

### What this pass did not do

Nothing here was run against a Jellyfin server. The observation job that
drives one is the workflow named above and it did not run at the release
commit.

The pass did not read the scorecard's SARIF against the ruleset, did not
re-derive the coverage or mutation figures, and did not open the fuzz corpus.
It confirms that those runs exist and where they stopped, and no more.

I am the same account that wrote the code and the threat model, so this is a
reading by an author and not a second reader's. Nothing in this repository
refuses that, and this page does not soften it: a pass that reads the author's
own claims against the author's own tests finds what the author can see.
