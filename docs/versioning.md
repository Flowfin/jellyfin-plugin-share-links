# Versioning

## The scheme

A release is `MAJOR.MINOR.PATCH.0`. Four parts, because a Jellyfin plugin version
is a four part number wherever a server reads it, and the fourth part is always
zero so it carries no meaning nobody has defined.

`MAJOR.MINOR.PATCH` follows the usual reading. `MAJOR` moves when an operator has
to do something before upgrading. `MINOR` moves when there is something new that
an existing installation does not have to care about. `PATCH` moves for a fix.

`targetAbi` in `build.yaml` is not part of this. It names the server line the
build was made against and moves on its own.

## Where the number is written

`build.yaml`, in its `version` field, and nowhere else.
`Directory.Build.props` reads that field at evaluation time and hands it to
`AssemblyVersion`, `FileVersion` and `Version`, so the number a catalogue shows
and the number inside the DLL are one number that was written once.

    git grep -n '<ManifestVersion>' -- Directory.Build.props

Two mechanisms then hold it. The gate job in `.github/workflows/publish.yaml`
refuses a tag whose numeric part is not that field, and a later step in the same
workflow refuses a build whose assembly is stamped anything else. The second is
reachable only by pushing a tag, and a tag cannot be taken back, so
`PackagingMetadataTests.TheAssemblyCarriesTheVersionTheManifestDeclares` makes the
same comparison on every run.

## THIS SECTION USED TO RESERVE `0.0.0.0`, AND THE RESERVATION IS RETIRED

What stood here said `Directory.Build.props` pinned `0.0.0.0`, that no release
ever carried it, and that a release supplied its version on the build command
line - so an assembly reporting `0.0.0.0` was known not to have come from the
release process. The informational version said the same thing in words, as
`0.0.0.0-unreleased+<commit>`, and a test held that rule.

The reservation and a working release could not both exist. The packaging tool
builds the plugin itself and is handed no MSBuild properties, so the only number
it can stamp into the shipped assembly is one the tree already holds; a release
supplying its version on a command line reaches the archive's name and never the
DLL inside it. The tag that would have found that out is the one input here that
cannot be taken back, so it was measured on #136 before one was spent.

**What is lost is stated rather than softened.** An assembly no longer says
whether the release process built it. A local build of the tree at this commit
reports the same version as the release cut from it, and the two are told apart
by the commit in the informational version and by the provenance attestation on
the published archive, not by the version. `-unreleased` is gone from
`Version`, and the test that held it is gone with it.

**What survives.** No release carries `0.0.0.0`: it is not a version anybody
publishes, and `PackagingMetadataTests.TheVersionIsNotTheOneThatSaysNoReleaseWasMade`
refuses a manifest that has fallen back to it. Nothing writes a release version
into the tree behind a person's back either - the inherited release-notes
workflow rewrote the version properties with `sed` and committed the result, and
issue #7 removed it, so the only edit that moves this number is the one a person
makes to `build.yaml`.

The informational version still carries the commit, appended by the SDK, and
`Jellyfin.Plugin.ShareLinks.Tests` holds both that and the agreement between the
informational version and the assembly version as tests rather than as this
paragraph.

## The server lines this repository compiles against

`Directory.Build.props` pins one Jellyfin package version per target framework,
because the two server lines cannot share a compiled assembly. What each pin
follows is a different rule, and neither of them is "the newest thing published".

    git grep -n 'JellyfinVersion Condition' -- Directory.Build.props

**The 10.11 pin follows the floor the manifest names.** `build.yaml` declares
`targetAbi`, a server admits the package on that floor, and an assembly compiled
against anything newer binds types that floor does not have - so the pin is the
`targetAbi` with the trailing part dropped. Building against the newest 10.11
release instead is what made three sibling boards publish a package their own
declared floor could not load, on 2026-09-03, and #136 is where that was repaired
here.

**The 12.0 pin stays where it landed.** It moves only by a change that says why,
until a `12.0.0` exists; on the day one appears it moves to `12.0.0` and the abi
floor leg reads it. Decided on #340 on 2026-09-04, and the two answers it was
decided against are worth carrying because both are the obvious thing to do:

- NOT "track the newest candidate". Every candidate on the 12.0 line stamps the
  same assembly version, so a pin that follows candidates changes the API surface
  the build compiles against without changing anything a manifest or a floor check
  can see. A build that binds a candidate's surface promises the line, and the line
  has no release to hold it to.
- NOT "leave it unmaintained until 12.0.0 exists". A pin nobody owns is a pin that
  moves by accident, dragged along in a dependency bump. It is owned: it stays, and
  the sentence saying so sits beside it in `Directory.Build.props` as well as here,
  so the next reader meets a decision rather than a value.

The same reading is taken on the sibling boards, on `jellyfin-plugin-server-pairing`
#344 and `jellyfin-plugin-sso` #1: the 12.0 line waits for the 12.0 line to have a
release, and the 12.0 leg of a floor proof is skipped with that reason printed
rather than run against a candidate.

The pins this repository holds while that rule is in force are `10.11.9` for
`net9.0` and `12.0.0-rc5` for `net10.0`.
`ServerLinePinTests` compares those two values against the property file, so a pin
that moves without this section moving with it reds the suite. That is the whole
of what it holds: it cannot tell a considered move from a careless one, and the
reason a pin moved is what the commit message and the pull request body are for.

NOTHING IN THIS TREE NOTICES THE DAY `12.0.0` IS RELEASED. A test asserting the
12.0 pin is still a candidate was written and taken out again: proving it bites
needs a fixture that pins a release, no release on that line exists, and a restore
against a version nuget does not hold fails before any assertion runs. So the end
of this rule is a thing somebody reads rather than something the suite reports,
and what is held is only that whoever moves the pin moves this section with it.

## Two builds of one commit

The same commit built twice produces the same assembly, byte for byte, and that
is checked by hashing both outputs rather than assumed.

`Deterministic` alone does not give it. It was already on by default and two clean
builds in one directory already matched, but the same commit built in a second
directory did not, because the absolute source path goes into the output.
`ContinuousIntegrationBuild` is what normalises those paths, and it is set for
every build rather than only in CI, because a property that holds in one place is
not the property.

It costs something. A local debugger no longer finds sources next to the assembly
by absolute path and goes through the source server instead. That is the trade,
and it is worth it for an artefact somebody installs from a manifest and may want
to check against the source it claims to come from.
