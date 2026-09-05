#!/usr/bin/env bash
# Refuses a recording in which a clean server could not add the repository URL and
# find this plugin, or found one whose checksum does not match the artefact it
# points at (#90).
#
# It reads a recorded file and never a server. That is what makes it provable: a
# fabricated recording carrying a wrong checksum is a file, so the refusal can be
# shown to bite without anybody publishing a broken catalogue, and the proof runs
# in the same job that judges the real recording.
#
# Usage:
#   refuse-a-catalogue-that-does-not-carry-this-plugin.sh <seen.json>
#
# read-the-catalogue-a-clean-server-sees.sh is what produces that file.
#
# WHAT THIS CANNOT SEE, and it is the case a reader will assume is covered. It
# judges one reading of one catalogue at one moment. The catalogue is generated in
# another repository from the releases this board publishes, so a run that is
# green says the entry was right when it was read and says nothing about the next
# hour. What watches it over time is that generator's own freshness job, which is
# the scope call taken on #90 on 2026-08-29 and recorded in docs/parity-ledger.md.
set -euo pipefail

seen="${1:?the recording to judge}"

[ -f "$seen" ] || {
  printf '::error::%s\n' "there is no recording at $seen, so nothing was judged" >&2
  exit 1
}

refusals=0
say() { printf '\n===== %s =====\n' "$*"; }
refuse() {
  printf '::error::%s\n' "$*" >&2
  refusals=$((refusals + 1))
}

plugin=$(jq -r '.plugin' "$seen")
catalogue=$(jq -r '.catalogue' "$seen")

say "the server was clean before the repository was added"
# Without this the reading below is about a server that was already offering the
# plugin, and a catalogue that carries nothing at all would pass.
if jq -e --arg g "$plugin" 'any(.offeredBefore[]; (.guid | ascii_downcase) == ($g | ascii_downcase))' "$seen" >/dev/null; then
  refuse "this plugin was already offered before $catalogue was added, so this reading says nothing about that repository. The server was not clean."
else
  echo "  offered before: $(jq '.offeredBefore | length' "$seen") packages, none of them this plugin"
fi

# A recording taken against a server that offered nothing at all would pass every
# comparison below by having nothing to disagree with.
if [ "$(jq '.offeredAfter | length' "$seen")" -eq 0 ]; then
  refuse "the server offered no packages at all after $catalogue was added, so the reading is empty rather than negative."
fi

say "the repository put this plugin in front of the operator"
entry=$(jq -c '.entry' "$seen")
if [ "$entry" = "null" ]; then
  refuse "a clean server added $catalogue and was not offered $plugin. What it was offered is: $(jq -r '[.offeredAfter[].name] | join(", ")' "$seen")"
else
  jq -r '"  \(.entry.guid)  \(.entry.name)"' "$seen"
fi

say "a version this server line can load"
chosen=$(jq -c '.chosen' "$seen")
if [ "$entry" != "null" ] && [ "$chosen" = "null" ]; then
  refuse "the entry carries no version this server line can load. The server is $(jq -r '.serverVersion' "$seen") and the entry offers: $(jq -r '[.entry.versions[] | "\(.version) targeting \(.targetAbi)"] | join(", ")' "$seen")"
elif [ "$chosen" != "null" ]; then
  jq -r '"  \(.chosen.version) targeting \(.chosen.targetAbi)"' "$seen"
fi

say "the checksum the catalogue serves is the checksum of the artefact it points at"
if [ "$chosen" != "null" ]; then
  served=$(jq -r '.chosen.checksum' "$seen")
  computed=$(jq -r '.computedChecksum' "$seen")
  echo "  served   $served"
  echo "  computed $computed"
  echo "  source   $(jq -r '.chosen.sourceUrl' "$seen")"
  if [ -z "$computed" ]; then
    refuse "the artefact was never hashed, so the checksum in the catalogue was compared against nothing."
  elif [ "$(printf '%s' "$served" | tr 'A-Z' 'a-z')" != "$(printf '%s' "$computed" | tr 'A-Z' 'a-z')" ]; then
    refuse "the catalogue serves checksum $served for $(jq -r '.chosen.sourceUrl' "$seen") and that file hashes to $computed. A server refuses the install, and the operator sees a download that fails rather than a manifest that is wrong."
  fi
fi

say "the server installed it from that repository and runs it"
installed=$(jq -c '.installed' "$seen")
if [ "$chosen" != "null" ] && [ "$installed" = "null" ]; then
  refuse "the server was asked to install $plugin from $catalogue and never listed it afterwards. Seeing an entry and being able to install it are different facts and this recording only carries the first."
elif [ "$installed" != "null" ]; then
  jq -r '"  \(.installed.Name) \(.installed.Version) \(.installed.Status)"' "$seen"
  status=$(jq -r '.installed.Status' "$seen")
  # A plugin the server loaded but will not run is worse than one it never
  # installed: the dashboard shows it as present.
  if [ "$status" != "Active" ] && [ "$status" != "Restart" ]; then
    refuse "the server installed this plugin from $catalogue and will not run it: its status is $status."
  fi
  if [ "$chosen" != "null" ]; then
    offered=$(jq -r '.chosen.version' "$seen")
    got=$(jq -r '.installed.Version' "$seen")
    # Four parts against four parts. A server that installed something other than
    # what the entry offered has taken it from somewhere else.
    if [ "$offered" != "$got" ]; then
      refuse "the catalogue offered $offered and the server is running $got, so what it installed did not come from that entry."
    fi
  fi
fi

if [ "$refusals" -gt 0 ]; then
  printf '\n::error::%s\n' "$refusals refusal(s): a clean server cannot install this plugin from $catalogue as read here." >&2
  exit 1
fi

printf '\nA clean server added %s, was offered this plugin, and installed and ran it.\n' "$catalogue"
