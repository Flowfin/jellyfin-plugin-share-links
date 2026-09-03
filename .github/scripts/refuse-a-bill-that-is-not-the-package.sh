#!/usr/bin/env bash
# Refuses a CycloneDX bill of materials that describes something other than the
# package this repository publishes (#341, #345).
#
# Three properties, each about the artefact rather than about the project:
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
#   No package the project references for build time only is named. The
#   analyzers are package references the project marks PrivateAssets="All", a
#   build-time tool reaches no archive, and a bill naming one describes the
#   build rather than what an operator installs. The names come from the caller,
#   which reads them out of MSBuild's evaluation of the project rather than out
#   of a list kept anywhere, so this script holds no name of its own.
#
# WHAT THE THIRD ONE CANNOT SEE. It compares names. A package that is in the
# bill only because an excluded one depends on it is removed by the tool's own
# reachability pass and is not judged here by name, so a tool that stopped
# removing orphans would leave such a package in and this would stay green. A
# package both an excluded reference and a shipped one reach belongs in the
# bill and is not the case this is for.
#
# It takes the bill as a path so the same code can be run over a bill nobody
# generated, which is how the check is shown to bite without publishing a wrong
# one to prove it. The list is the third argument and may be empty, which is a
# project with no build-time only reference; it may not be omitted, because a
# caller that forgot it would otherwise get a clean run over a property that was
# never judged.
set -euo pipefail

usage="usage: refuse-a-bill-that-is-not-the-package.sh <bom.json> <expected-version> <build-time-only-names, comma separated, may be empty>"
bom="${1:?${usage}}"
expected="${2:?${usage}}"
build_time_only="${3?${usage}}"

if [ ! -f "$bom" ]; then
  echo "::error::$bom does not exist. Fail closed: a bill that was never written cannot be judged."
  exit 1
fi

# A precondition rather than a property of its own. The neighbouring step refuses
# a bill that names neither the assembly nor the direct package references, for
# its own reason; this one only needs to know that the component list is a list,
# because the properties below pass vacuously over an empty one.
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

# NuGet package identifiers are case-insensitive, so the comparison is too. The
# list is split in jq rather than in the shell so that a name is compared whole
# and a stray space around a comma is not part of it.
named="$(jq -r --arg list "$build_time_only" '
  ($list | split(",") | map(gsub("^\\s+|\\s+$"; "") | select(length > 0) | ascii_downcase)) as $excluded
  | .components[]
  | select((.name | ascii_downcase) as $n | $excluded | index($n))
  | "  \(.name) \(.version)"
' "$bom")"
if [ -n "$named" ]; then
  echo "Packages the project references for build time only, named in the bill:"
  printf '%s\n' "$named"
  echo "::error::The bill names a package the project references for build time only. No artefact carries an analyzer; generate the bill with those references excluded rather than describing the build."
  exit 1
fi

echo "The bill is of version ${subject}, names ${count} components, names no package twice, and names nothing the project references for build time only."
