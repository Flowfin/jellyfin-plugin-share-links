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
