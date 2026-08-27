# Releasing

A release is published by pushing a tag. Nothing is created by hand.

## The tag

The tag has the form `X.Y.Z-stable` or `X.Y.Z.W-stable`, for example `1.4.0-stable`
or `0.1.0.0-stable`. The numeric part is the plugin version that Jellyfin installs,
and it must be exactly the `version` in `build.yaml`, written the same way, with the
same number of parts. The `-stable` suffix lives only in the tag and in the release
name.

## Cutting a release

1. Write the release notes as you go, not at the end. Every change worth telling
   an operator about leaves a fragment in `changelog.d`, named `<issue>.<kind>.md`,
   in the change that makes it. `changelog.d/README.md` is the convention. A tag
   pushed with an empty `changelog.d` is refused by the gate job before anything
   is built.
2. Update `version` in `build.yaml` on the release branch and merge it. That field
   is the only place the number is written: `Directory.Build.props` reads it, so
   the assembly is stamped with it and no second file needs editing. Run
   `dotnet restore --force-evaluate` in the same change, because the test
   project's `packages.lock.json` records the plugin project's version and goes
   stale otherwise. Locked-mode restore does not refuse a stale entry there, so
   nothing reds if this is forgotten.
3. Check that the commit you want to release is on that branch.
4. Check that the interoperability matrix is green for that commit. The section
   below says how it is read and what a red one means.
5. Push the tag for that commit:

    ```
    git tag 1.4.0-stable <commit>
    git push origin 1.4.0-stable
    ```

The `Publish Release` workflow takes it from there.

Push one tag at a time and wait for its run to finish. GitHub keeps at most one
queued run per concurrency group, and although the group here is keyed on the tag,
serialising them by hand is what keeps the release order readable.

In the same change that raises the version, delete the fragments that went into
it. `changelog.d` holds what is unreleased; the notes for a version that shipped
live in that release.

## The interoperability matrix is a release condition

The rule this plugin is held to is that it works alone and works with every other
supported sibling installed at the same time (#96). What holds it is the
`observations` workflow, `.github/workflows/observations.yml`. It brings a real
Jellyfin up twice from the package built out of the tree: once with this plugin
alone, and once on a clean server carrying this plugin beside everything the
Flowfin catalogue serves for the line being booted. The same observations are
made against both, a fatal is refused and so is an error line whose source
context is this plugin, and the two servers are compared on the three axes the
rule is about: routes, scheduled task names and configuration.

A release is not cut while that run is red. Before pushing the tag:

```
gh run list --repo Flowfin/jellyfin-plugin-share-links \
  --workflow observations.yml --limit 1 \
  --json databaseId,headSha,event,conclusion \
  --jq '.[] | "\(.databaseId) \(.event) \(.conclusion) \(.headSha)"'
```

Read the head sha it reports rather than only the conclusion. The run is on a
nightly schedule, so the most recent one is usually older than the commit being
released, and a green run on an older commit says nothing about this one. Where
it is not the commit the tag will name, start one on that commit with the
workflow's `workflow_dispatch` trigger and wait for it.

A red matrix holds the release until one of two things is true. Either the
collision is fixed, or the incompatibility is written into `docs/limits.md` as a
known limitation, carrying the reason it stands and the line beginning **What an
operator does.** that every entry on that page carries. Writing it down is not a
way past the matrix: the entry names which sibling, on which server line, and
what an operator gives up by installing both.

### What this condition is worth, and it is less than it reads

**Nothing on the publish route reads that verdict.** The gate job judges the tree
and the tag, and never the tracker or another workflow's runs, so a tag pushed
while the matrix is red publishes exactly like one pushed while it is green. What
holds this condition is whoever cuts the release, plus
`InteroperabilityMatrixTests`, which refuses this section drifting away from the
workflow and the page it names. Neither of those is a machine refusing a release.

**The matrix boots one server line.** The tree compiles two, and the second has no
released server to boot, which is #181. A green matrix therefore says this plugin
came up beside the catalogue's set on the 10.11 line and says nothing about 12.0.

**The set is what the catalogue served at the moment the run took it**, rather
than a list in this repository, so it widens on the day a sibling board publishes
its first release. Those plugins are the ones that ship together, not the ones
known to work together, and this run is what turns the first into the second. A
green one says they installed beside each other without colliding on a route, a
task name or a configuration key. It does not say anybody has used them together.

**A set comparison cannot see two plugins declaring one route.** Two owners of one
path are one key in the server's own description of itself, so that path is
present in both boots and the comparison says nothing about it. What catches it is
the second run of the observations, where a server that cannot decide which
handler owns a path fails the calls.

## Where the release notes come from

From the fragments in `changelog.d`, assembled by
`.github/scripts/assemble-release-notes.sh` in the gate job and handed to the
release job as the body of the release. Nothing else writes them, and the forge's
own note generator is off:

    grep -n 'body_path\|generate_release_notes' .github/workflows/publish.yaml

Two sources for one body is the failure that decided this. The forge builds notes
from the commits between two tags, which is a record of the work, and the notes an
operator reads answer what is different on their server after they upgrade.
Decided on #89 on 2026-08-11 and again on 2026-08-24; `changelog.d/README.md`
argues it where somebody writing a fragment meets it.

## What a prerelease means here

Nothing yet. This plugin publishes one channel, every published version is
stable, and `prerelease` is `false` on every run:

    grep -n 'prerelease:' .github/workflows/publish.yaml

There is no public prerelease channel and no nightly build, decided on #89 on
2026-08-11 and recorded as declined in `docs/parity-ledger.md`. A version that is
not ready is not tagged; the tag is what publishes, so there is no state between
built and released for a prerelease to occupy. If a beta channel is ever run, #91
is where it is decided and this section is what it changes.

## What the run produces

The workflow builds the plugin from the tagged commit, creates the GitHub release
for the tag, and attaches four files:

- the plugin archive
- the packaging metadata written beside it, `<archive>.zip.meta.json`
- one `.md5` file, the checksum of the archive
- one `.sha256` file for the same archive

The `.md5` is the value a Jellyfin catalog serves as the plugin checksum. There is
exactly one per release so that no generator can pair a checksum with the wrong
file. Both the archive and the metadata are checked for existence by name before the
release job runs, so a release with three of the four files is not a state this route
can reach.

The run also signs a build provenance statement for the archive, in a separate job
that downloads the archive and runs no build tooling. A downloaded archive can be
checked against it:

```
gh attestation verify <archive>.zip --repo <owner>/<repository>
```

Nothing here writes a plugin catalog. A GitHub release is the whole output. If this
repository previously published through the Jellyfin meta plugins workflow, that path
is gone and no catalog is fed until a manifest generator is added.

## What fails the run

- The tag does not end in `-stable`, or the workflow was started from something
  other than a tag.
- The numeric part of the tag differs from `version` in `build.yaml`.
- `build.yaml` is missing a required field, or `version`, `targetAbi`, `framework`
  or `guid` has the wrong shape.
- `framework` in `build.yaml` names a target the plugin project is not built for.
- A packaging manifest that shadows `build.yaml` is present, such as `jprm.yaml` or
  `meta.yaml`.
- `build.yaml` declares an `image` file that is not in the repository.
- The tagged commit is not contained in a release branch, or the tag was moved after
  the run started.
- There is no `packages.lock.json` next to the plugin project, so the release build
  cannot restore against a reviewed dependency graph. Create one with
  `dotnet restore <project> -p:RestorePackagesWithLockFile=true` and commit it.
- The version stamped into the assembly is not the version in `build.yaml`.
- The build produced no archive, or more than one, or no packaging metadata.
- `changelog.d` holds no fragment, so the release would carry no notes.
- A fragment there is empty, or is not named `<issue>.<kind>.md` with a kind the
  assembler accepts.
- A release already exists for the tag.

All of these fail before anything is published.

## What the run notes without failing

The packaging tool warns when `build.yaml` declares neither `image` nor `imageUrl`.
The plugin then shows without a logo in a catalog. That is a warning on every run
until a logo exists, and it is not a reason to hold a release.

## Re-running

A release that exists is not touched again. The release job asks whether a release
exists for the tag before it writes anything and stops if one does, and the upload
step is configured not to replace an asset of the same name. Replacing the bytes of a
version people have already installed is the failure this prevents, and it is worth
more than the convenience of a re-run.

So: if a release went out with the wrong contents, fix the problem, raise the version
in `build.yaml`, and push a new tag.

If a run failed **before** the release was created, the tag is still clean. Fix the
cause and re-run the workflow from the Actions page, or delete and re-push the tag.

If a run failed **after** the release was created but before every asset was attached,
the release is incomplete and a re-run will refuse it. What is possible then depends
on the repository settings below. Without immutable releases you can delete the
incomplete release, delete the tag, and push it again. With immutable releases you
cannot, and the version has to be raised.

## Repository settings this expects

- Default workflow permissions set to read only.
- A rule that restricts who may push `*-stable` tags.
- The `ABI floor build` check required on the release branches.
- Immutable releases, if the repository wants the guarantee that a published release
  can never be edited or deleted at all. The workflow does not depend on it: the
  refusal to touch an existing release is enforced in the release job. Turning it on
  removes the only recovery path for an incomplete release, so try it on one
  repository and cut a release there before turning it on everywhere.
