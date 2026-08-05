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

## What the tree holds

`0.0.0.0` and nothing else. `Directory.Build.props` pins `AssemblyVersion` and
`FileVersion` at `0.0.0.0` and `Version` at `0.0.0.0-unreleased`, and no release
ever carries either. A release supplies its version on the build command line
instead, so the number lives in the release and not in a file somebody can build
from afterwards.

That is the whole point of reserving it. Before this, the release process wrote
the released version into `Directory.Build.props` and committed it, so from then
on an ordinary local build produced an assembly reporting the released version.
Two artefacts that are not the same thing reported the same version, and the one
question a version has to answer is which of the two you are holding.

So an assembly reporting `0.0.0.0` was not built by the release process. There is
no build that reports `0.0.0.0` and is a release, and no release that reports
`0.0.0.0`.

The informational version says the same thing in words rather than by absence. An
unreleased build reports `0.0.0.0-unreleased+<commit>`, where the commit is
appended by the SDK from the repository. `Jellyfin.Plugin.ShareLinks.Tests` holds
the rule as a test rather than as this paragraph.

One route used to write a release version into the tree. The inherited
release-notes workflow rewrote the three properties with `sed` and committed the
result, which is exactly the shape this scheme exists to stop. Issue #7 removed
that workflow, so no route in the tree does it today.

Nothing refuses a new one. This scheme is a rule a person follows, not a check
that fails, and the release process it depends on is still to be decided in issue
#89.

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
