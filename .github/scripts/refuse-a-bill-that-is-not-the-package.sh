#!/usr/bin/env bash
# Refuses a CycloneDX bill of materials that describes something other than the
# package this repository publishes (#341).
#
# Two properties, both about the artefact rather than about the project:
#
#   The subject carries the version build.yaml declares. CycloneDX defaults the
#   metadata component version to 0.0.0 when nothing sets it, so a bill that was
#   never told the version and a bill told the wrong one look identical in the
#   file, and neither can be paired with a release by whoever reads it later.
#
#   No package is named at two versions. The project has been multi-targeted
#   since #276 and the package ships the one line build.yaml names, so a scan
#   that aggregated every target framework produces exactly this shape: one name,
#   two versions, one of them a server line no operator can install.
#
# It takes the bill as a path so the same code can be run over a bill nobody
# generated, which is how the check is shown to bite without publishing a wrong
# one to prove it.
set -euo pipefail

bom="${1:?usage: refuse-a-bill-that-is-not-the-package.sh <bom.json> <expected-version>}"
expected="${2:?usage: refuse-a-bill-that-is-not-the-package.sh <bom.json> <expected-version>}"

if [ ! -f "$bom" ]; then
  echo "::error::$bom does not exist. Fail closed: a bill that was never written cannot be judged."
  exit 1
fi

# A precondition rather than a property of its own. The neighbouring step refuses
# a bill that names neither the assembly nor the direct package references, for
# its own reason; this one only needs to know that the component list is a list,
# because both properties below pass vacuously over an empty one.
count="$(jq '.components | length // 0' "$bom")"
if [ "$count" -eq 0 ]; then
  echo "::error::The bill names no components at all. That is not a package with no dependencies, it is a bill that was not produced."
  exit 1
fi

subject="$(jq -r '.metadata.component.version // empty' "$bom")"
if [ -z "$subject" ]; then
  echo "::error::The bill names no version for its own subject, so nothing says which artefact it is a bill of."
  exit 1
fi

if [ "$subject" != "$expected" ]; then
  echo "::error::The bill's subject is version ${subject} and the package build.yaml declares is ${expected}. A bill whose version matches no artefact cannot be paired with a release."
  exit 1
fi

# One jq call rather than a shell loop calling jq per name: the loop's body reads
# the same standard input the loop is fed from, which silently emptied the version
# list for every name but the last when this was written that way.
duplicates="$(jq -r '
  [.components[] | {name, version}]
  | group_by(.name)
  | map(select(length > 1))
  | .[]
  | "  \(.[0].name): \([.[].version] | join(" "))"
' "$bom")"
if [ -n "$duplicates" ]; then
  echo "Packages named at more than one version:"
  printf '%s\n' "$duplicates"
  echo "::error::The bill names a package at more than one version, so it describes more than one build. The package ships one server line; generate the bill for that line rather than aggregating every target framework."
  exit 1
fi

echo "The bill is of version ${subject}, names ${count} components, and names no package twice."
