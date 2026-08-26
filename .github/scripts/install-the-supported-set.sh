#!/usr/bin/env bash
# Installs the supported set of sibling plugins beside this one, for the second
# boot #96 asks for.
#
# The set is not a list written here. It is what the Flowfin catalogue serves,
# read at the moment it is used, because that is the set an operator can actually
# install and it grows the day a sibling board publishes its first release. A
# list in this file would be the enumeration this fleet counts as a defect, and
# it would go stale the same week.
#
# The subject is NOT taken from the catalogue. This board has published nothing,
# so it is absent from what the catalogue serves; on the day it publishes, its
# own entry appears there and a run that took the set literally would install a
# released package beside, or instead of, the one the workflow just built out of
# the tree. So this plugin's identifier is skipped by guid, and the subject keeps
# coming from the tree.
#
# Usage:
#   install-the-supported-set.sh <catalogue-url> <plugins-dir> <server-version>
#
# The server version is what decides which archive of a sibling is installed. A
# board that publishes for two server lines at once publishes two releases, and
# asking for the newest would put a build for the next line on this one and read
# the resulting refusal as a collision. What is chosen is the newest version
# whose targetAbi is not above the server being booted.
set -euo pipefail

catalogue="${1:?the catalogue URL to read the supported set from}"
plugins_dir="${2:?the directory the server reads its plugins from}"
server_version="${3:?the version of the server the set is being installed for}"

# This plugin, by the identifier `build.yaml` fixes. Written out rather than read
# out of the tree, for the same reason the observation script writes it out: what
# this is comparing against is a catalogue entry somebody else generated, and the
# guid is the name the two sides agree on.
this_plugin="a3703f07-f83d-49a0-a09f-50b890a2baac"

say() { printf '\n===== %s =====\n' "$*"; }
fail() {
  printf '::error::%s\n' "$*" >&2
  exit 1
}

# A dotted version as one sortable key, padded to four parts. A missing part is
# zero, which is what makes `10.11` and `10.11.0.0` the same number here.
key_of() {
  printf '%s' "$1" | awk -F. '{printf "%05d%05d%05d%05d", $1, $2, $3, $4}'
}

say "reading the supported set from the catalogue"
echo "$catalogue"
served=$(curl -fsSL "$catalogue") || fail "the catalogue did not answer, so what the supported set is today is unknown. Nothing was installed."
printf '%s' "$served" | jq -e 'type == "array"' >/dev/null || fail "the catalogue did not answer with a list of entries: $(printf '%s' "$served" | head -c 400)"
printf '%s' "$served" | jq -r '.[] | "  \(.guid)  \(.name)"'

mkdir -p "$plugins_dir"
installed=0

while read -r guid; do
  name=$(printf '%s' "$served" | jq -r --arg g "$guid" '.[] | select(.guid == $g) | .name')

  # Compared with the dashes taken out of both sides, the way the observation
  # script compares the same identifier against what the server reports.
  if [ "${guid//-/}" = "${this_plugin//-/}" ]; then
    say "skipping this plugin's own catalogue entry"
    echo "$guid $name is the subject of this run, and the subject comes from the package the workflow built out of the tree."
    continue
  fi

  say "$name"

  # The newest version this server line can load, chosen rather than assumed.
  chosen=""
  chosen_key=""
  while read -r version; do
    version_number=$(printf '%s' "$version" | jq -r '.version')
    abi=$(printf '%s' "$version" | jq -r '.targetAbi')
    if [ "$(key_of "$abi")" \> "$(key_of "$server_version")" ]; then
      echo "  $version_number targets $abi, which is above $server_version, so it is not for this server line"
      continue
    fi
    this_key=$(key_of "$version_number")
    if [ -z "$chosen_key" ] || [ "$this_key" \> "$chosen_key" ]; then
      chosen="$version"
      chosen_key="$this_key"
    fi
  done < <(printf '%s' "$served" | jq -c --arg g "$guid" '.[] | select(.guid == $g) | .versions[]')

  if [ -z "$chosen" ]; then
    fail "$name publishes nothing this server line can load, and an incompatibility is a result rather than something to install around. #96 says it is documented as a known limitation with its reason before this run can be green again."
  fi

  version_number=$(printf '%s' "$chosen" | jq -r '.version')
  url=$(printf '%s' "$chosen" | jq -r '.sourceUrl')
  checksum=$(printf '%s' "$chosen" | jq -r '.checksum')
  echo "  $version_number, targetAbi $(printf '%s' "$chosen" | jq -r '.targetAbi')"
  echo "  $url"

  archive=$(mktemp)
  curl -fsSL -o "$archive" "$url" || fail "the archive for $name did not download: $url"

  # The catalogue publishes a checksum for every archive it serves, so the bytes
  # that reach this server are the bytes the catalogue named. An install that
  # skipped this would still boot, and the run would be reporting on whatever a
  # release asset happens to hold today.
  got=$(md5sum "$archive" | cut -d' ' -f1)
  [ "$got" = "$checksum" ] || fail "the archive for $name is not what the catalogue named: it says $checksum and the bytes are $got"
  echo "  checksum $got, as the catalogue names it"

  # One directory per plugin, named after the entry so the log and the bind mount
  # read the same. The server scans every directory under this one, so the name is
  # for the reader rather than for the server.
  target="$plugins_dir/$(printf '%s' "$name" | tr -cd '[:alnum:]')"
  mkdir -p "$target"
  unzip -o -q "$archive" -d "$target"
  rm -f "$archive"
  ls -la "$target"
  installed=$((installed + 1))
done < <(printf '%s' "$served" | jq -r '.[].guid')

say "what is installed beside this plugin"
[ "$installed" -gt 0 ] || fail "the catalogue served no sibling this server line can load, so a boot made now would be the boot with this plugin alone under a second name. A scan over a set of one is green by construction, which is the shape this repository refuses elsewhere."
echo "$installed sibling(s)"
ls -la "$plugins_dir"
