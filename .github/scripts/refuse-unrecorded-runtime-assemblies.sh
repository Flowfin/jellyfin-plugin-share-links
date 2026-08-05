#!/usr/bin/env bash
# Reads the names of the files inside a built plugin package on standard input, one
# per line, and refuses any assembly that is neither the plugin's own nor recorded
# in .github/runtime-assemblies.allow (#30).
#
# It takes a list rather than the package itself so the same code can be run against
# a list nobody built, which is how the check is shown to bite without shipping a
# broken package to prove it.
set -euo pipefail

own="${OWN_ASSEMBLY:-Jellyfin.Plugin.ShareLinks.dll}"
allow="${ALLOW_FILE:-.github/runtime-assemblies.allow}"

if [ ! -f "$allow" ]; then
  echo "::error::$allow is missing. Fail closed: without the record of what is allowed, nothing can be judged."
  exit 1
fi

# Strip comments and blank lines. An empty result is the expected state and must
# not be read as "allow everything".
recorded=$(grep -vE '^[[:space:]]*(#|$)' "$allow" | awk '{print $1}' || true)

assemblies=$(grep -E '\.dll$' || true)

echo "Assemblies in the package:"
if [ -z "$assemblies" ]; then
  echo "::error::The package contains no assembly at all. That is not an empty dependency set, it is an empty package."
  exit 1
fi
printf '%s\n' "$assemblies" | sed 's/^/  /'

unrecorded=0
while IFS= read -r dll; do
  [ -n "$dll" ] || continue
  base=${dll##*/}
  if [ "$base" = "$own" ]; then
    continue
  fi
  if printf '%s\n' "$recorded" | grep -qxF "$base"; then
    echo "recorded  $base"
    continue
  fi
  echo "UNRECORDED $base"
  unrecorded=1
done <<< "$assemblies"

if [ "$unrecorded" -ne 0 ]; then
  echo "::error::The package ships a runtime assembly that is not the plugin's own and is not recorded in ${allow}. Either drop the reference or argue the exception in the issue that introduces it and add the line."
  exit 1
fi

if [ -z "$recorded" ]; then
  echo "The package carries the plugin assembly and nothing else."
else
  echo "The package carries the plugin assembly and only assemblies ${allow} records."
fi
