#!/usr/bin/env bash
# Proves that refuse-a-catalogue-that-does-not-carry-this-plugin.sh refuses each
# fault it names, and accepts the near miss beside it.
#
# The judgement in the job runs against a catalogue that carries this plugin
# correctly, which is what a green run means and is also what a check pointed at
# the wrong field, or one that never fires, looks like. So every refusal is shown
# to bite here, against a recording written to carry exactly one fault, and the
# run says which fault each one bit on rather than only that something failed.
#
# It reaches no server and no catalogue. The recordings are files, which is the
# whole reason the reading and the judging are two scripts: a wrong checksum can
# be written down without anybody publishing a broken release to prove the check
# works.
set -euo pipefail

here="$(cd "$(dirname "$0")" && pwd)"
refuse="$here/refuse-a-catalogue-that-does-not-carry-this-plugin.sh"
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT

failures=0
say() { printf '\n===== %s =====\n' "$*"; }

# The recording a green run produces, in the shape
# read-the-catalogue-a-clean-server-sees.sh writes it. Fixture vocabularies are
# fixture vocabularies: the checksum and the sibling names below stand for a
# catalogue and prove nothing about what is served today.
#
# THE GUID IS WRITTEN WITHOUT ITS DASHES ON PURPOSE, because that is how a server
# reports a package and it is not how the catalogue serves it. A fixture carrying
# the dashed form on both sides made the first version of this proof green while
# the run against a real server refused a plugin it was plainly being offered, on
# run 33939876921.
cat >"$work/seen.json" <<'JSON'
{
  "plugin": "a3703f07-f83d-49a0-a09f-50b890a2baac",
  "catalogue": "https://example.invalid/manifest.json",
  "serverVersion": "10.11.11",
  "offeredBefore": [{ "guid": "d8dc4dc7-c2b0-4c9e-bb35-9d0dcd8e9d38", "name": "An Official Plugin" }],
  "offeredAfter": [
    { "guid": "d8dc4dc7-c2b0-4c9e-bb35-9d0dcd8e9d38", "name": "An Official Plugin" },
    { "guid": "a3703f07f83d49a0a09f50b890a2baac", "name": "Share Links" }
  ],
  "entry": {
    "guid": "a3703f07f83d49a0a09f50b890a2baac",
    "name": "Share Links",
    "versions": [
      {
        "version": "0.1.0.0",
        "targetAbi": "10.11.9.0",
        "sourceUrl": "https://example.invalid/share-links_0.1.0.0.zip",
        "checksum": "00112233445566778899aabbccddeeff"
      }
    ]
  },
  "chosen": {
    "version": "0.1.0.0",
    "targetAbi": "10.11.9.0",
    "sourceUrl": "https://example.invalid/share-links_0.1.0.0.zip",
    "checksum": "00112233445566778899aabbccddeeff"
  },
  "computedChecksum": "00112233445566778899aabbccddeeff",
  "installed": {
    "Id": "a3703f07f83d49a0a09f50b890a2baac",
    "Name": "Share Links",
    "Version": "0.1.0.0",
    "Status": "Active"
  }
}
JSON

# Runs the refusal over a one-fault copy of the clean recording and requires it to
# refuse ON THAT FAULT. A run that failed for some other reason would otherwise
# read as a proof, which is the way a near-miss test quietly stops testing
# anything.
bites() {
  local what="$1" mutation="$2" expected="$3" out code
  jq "$mutation" "$work/seen.json" >"$work/probe.json"
  set +e
  out=$(bash "$refuse" "$work/probe.json" 2>&1)
  code=$?
  set -e
  if [ "$code" -eq 0 ]; then
    printf '::error::%s\n' "the check accepted a recording carrying $what"
    failures=$((failures + 1))
    return
  fi
  if ! printf '%s' "$out" | grep -qF "$expected"; then
    printf '::error::%s\n' "the check refused a recording carrying $what, but not for that reason. What it said is below."
    printf '%s\n' "$out"
    failures=$((failures + 1))
    return
  fi
  echo "refused: $what"
}

# Requires the check to accept, which is what says a refusal above was the fault's
# doing rather than something already wrong with the fixture.
accepts() {
  local what="$1" mutation="$2" out code
  jq "$mutation" "$work/seen.json" >"$work/probe.json"
  set +e
  out=$(bash "$refuse" "$work/probe.json" 2>&1)
  code=$?
  set -e
  if [ "$code" -ne 0 ]; then
    printf '::error::%s\n' "the check refused $what, which is not a fault. What it said is below."
    printf '%s\n' "$out"
    failures=$((failures + 1))
    return
  fi
  echo "accepted: $what"
}

say "the recording a green run produces is accepted"
accepts "a clean server that added the repository, saw the plugin and installed it" '.'

say "each refusal bites, on its own fault"
bites "a repository that does not carry this plugin" \
  '.entry = null | .chosen = null | .installed = null | .offeredAfter = [.offeredAfter[0]]' \
  "was not offered"
bites "a server that was already offering it before the repository was added" \
  '.offeredBefore += [{ "guid": "a3703f07f83d49a0a09f50b890a2baac", "name": "Share Links" }]' \
  "The server was not clean"
bites "the same, with the dashes the catalogue serves it with" \
  '.offeredBefore += [{ "guid": "a3703f07-f83d-49a0-a09f-50b890a2baac", "name": "Share Links" }]' \
  "The server was not clean"
bites "a server that offered nothing at all" \
  '.offeredAfter = [] | .entry = null | .chosen = null | .installed = null' \
  "offered no packages at all"
bites "an entry with no version this server line can load" \
  '.entry.versions[0].targetAbi = "12.0.0.0" | .chosen = null | .installed = null' \
  "carries no version this server line can load"
bites "a checksum that is not the artefact's" \
  '.computedChecksum = "ffeeddccbbaa99887766554433221100"' \
  "and that file hashes to"
bites "an artefact that was never hashed" \
  '.computedChecksum = ""' \
  "compared against nothing"
bites "an entry the server would not install" \
  '.installed = null' \
  "never listed it afterwards"
bites "a plugin the server installed and will not run" \
  '.installed.Status = "Malfunctioned"' \
  "will not run it"
bites "a server running a version the entry did not offer" \
  '.installed.Version = "0.2.0.0"' \
  "did not come from that entry"

say "the near misses are accepted"
# The checksum comparison is case-insensitive on purpose: md5sum prints lower
# case and a generator is free to serve upper. A check that reds on that is one
# an operator learns to ignore.
accepts "a served checksum in upper case" \
  '.chosen.checksum = (.chosen.checksum | ascii_upcase) | .entry.versions[0].checksum = .chosen.checksum'
# A server that has taken the plugin but wants a restart before it runs it is the
# ordinary outcome of an install, not a fault.
accepts "a plugin the server will run after a restart" \
  '.installed.Status = "Restart"'
# A guid the server echoes back in a different case is the same guid. The
# comparison lower-cases both sides, and a check that did not would refuse a
# correct catalogue for a formatting choice nobody controls.
accepts "an entry whose guid is served in upper case" \
  '.offeredAfter[1].guid = (.offeredAfter[1].guid | ascii_upcase)'
# And the same guid with its dashes put back, which is the form the catalogue
# serves and the form a server does not. Both spellings have to be one identifier
# here, or the check reads a published catalogue as carrying nothing.
accepts "an entry whose guid carries the dashes the catalogue serves" \
  '.offeredAfter[1].guid = "a3703f07-f83d-49a0-a09f-50b890a2baac"'

say "verdict"
if [ "$failures" -gt 0 ]; then
  echo "$failures of the properties above did not hold"
  exit 1
fi
echo "every refusal bit on its own fault, and the near misses were accepted"
