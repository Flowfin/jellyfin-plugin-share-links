#!/usr/bin/env bash
# Proves that `refuse-a-collision.sh` refuses each collision it names, and that
# it accepts the near miss beside it.
#
# The comparison in the job runs against two servers that did not collide, which
# is what a green run means and is also what a scan pointed at an empty set, or
# at a comparison that never fires, looks like. So every refusal is shown to bite
# here, against a surface written to carry exactly one fault, and the run says
# which fault each one bit on rather than only that something failed.
#
# It reaches no server. The surfaces are files, which is the whole reason the
# reading and the judging are two scripts: a collision can be written down
# without anybody having to build a colliding plugin to prove the scan works.
set -euo pipefail

here="$(cd "$(dirname "$0")" && pwd)"
refuse="$here/refuse-a-collision.sh"
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT

failures=0
say() { printf '\n===== %s =====\n' "$*"; }

# The two surfaces a green run produces, in the shape `read-the-server-surface.sh`
# writes them. Fixture vocabularies are fixture vocabularies: the identifiers and
# names below stand for a plugin and two siblings and prove nothing about which
# plugins the catalogue serves today.
cat >"$work/alone.json" <<'JSON'
{
  "base": "http://127.0.0.1:8096",
  "plugin": "a3703f07f83d49a0a09f50b890a2baac",
  "plugins": [
    { "id": "a3703f07f83d49a0a09f50b890a2baac", "raw": "a3703f07-f83d-49a0-a09f-50b890a2baac", "name": "Share Links", "version": "0.1.0.0", "status": "Active" }
  ],
  "tasks": [{ "name": "Scan Media Library", "key": "RefreshLibrary", "category": "Library" }],
  "paths": ["/ShareLinks/Shares", "/Users/Me"],
  "configuration": {
    "before": { "a3703f07f83d49a0a09f50b890a2baac": { "DefaultShareLifetimeDays": 7 } },
    "after": { "a3703f07f83d49a0a09f50b890a2baac": { "DefaultShareLifetimeDays": 6 } }
  }
}
JSON

cat >"$work/with-the-set.json" <<'JSON'
{
  "base": "http://127.0.0.1:8096",
  "plugin": "a3703f07f83d49a0a09f50b890a2baac",
  "plugins": [
    { "id": "a3703f07f83d49a0a09f50b890a2baac", "raw": "a3703f07-f83d-49a0-a09f-50b890a2baac", "name": "Share Links", "version": "0.1.0.0", "status": "Active" },
    { "id": "0f9c9107b31b459e81fa6d35dac25e79", "raw": "0f9c9107-b31b-459e-81fa-6d35dac25e79", "name": "A Sibling", "version": "0.1.0.0", "status": "Active" },
    { "id": "29e9026752ee4becb4fb870b8f5ddc53", "raw": "29e90267-52ee-4bec-b4fb-870b8f5ddc53", "name": "Another Sibling", "version": "0.1.0.0", "status": "Active" }
  ],
  "tasks": [
    { "name": "Scan Media Library", "key": "RefreshLibrary", "category": "Library" },
    { "name": "Trim the sibling's records", "key": "TrimSibling", "category": "A Sibling" }
  ],
  "paths": ["/ShareLinks/Shares", "/Users/Me", "/ASibling/Queue", "/AnotherSibling/Summary"],
  "configuration": {
    "before": {
      "a3703f07f83d49a0a09f50b890a2baac": { "DefaultShareLifetimeDays": 7 },
      "0f9c9107b31b459e81fa6d35dac25e79": { "Something": 1 },
      "29e9026752ee4becb4fb870b8f5ddc53": "no configuration, 404"
    },
    "after": {
      "a3703f07f83d49a0a09f50b890a2baac": { "DefaultShareLifetimeDays": 6 },
      "0f9c9107b31b459e81fa6d35dac25e79": { "Something": 1 },
      "29e9026752ee4becb4fb870b8f5ddc53": "no configuration, 404"
    }
  }
}
JSON

# Runs the scan over a one-fault copy of the clean surface and requires it to
# refuse ON THAT FAULT. A run that failed for some other reason would otherwise
# read as a proof, which is the way a near-miss test quietly stops testing
# anything.
bites() {
  local what="$1" mutation="$2" expected="$3" out code
  jq "$mutation" "$work/with-the-set.json" >"$work/probe.json"
  set +e
  out=$(bash "$refuse" "$work/probe.json" "$work/alone.json" 2>&1)
  code=$?
  set -e
  if [ "$code" -eq 0 ]; then
    printf '::error::%s\n' "the scan accepted a surface carrying $what"
    failures=$((failures + 1))
    return
  fi
  if ! printf '%s' "$out" | grep -qF "$expected"; then
    printf '::error::%s\n' "the scan refused a surface carrying $what, but not for that reason. What it said is below."
    printf '%s\n' "$out"
    failures=$((failures + 1))
    return
  fi
  echo "refused: $what"
}

# Requires the scan to accept, which is what says a refusal above was the fault's
# doing rather than something already wrong with the fixture.
accepts() {
  local what="$1" mutation="$2" out code
  jq "$mutation" "$work/with-the-set.json" >"$work/probe.json"
  set +e
  out=$(bash "$refuse" "$work/probe.json" "$work/alone.json" 2>&1)
  code=$?
  set -e
  if [ "$code" -ne 0 ]; then
    printf '::error::%s\n' "the scan refused $what, which is not a collision. What it said is below."
    printf '%s\n' "$out"
    failures=$((failures + 1))
    return
  fi
  echo "accepted: $what"
}

say "the pair a green run produces is accepted"
accepts "two boots that did not collide" '.'

say "each refusal bites, on its own fault"
bites "a plugin the server will not run" \
  '.plugins[1].status = "Malfunctioned"' \
  "the server loaded a plugin it will not run"
bites "two plugins under one identifier" \
  '.plugins[1].id = .plugins[0].id' \
  "two plugins carry one identifier"
bites "two plugins under one name" \
  '.plugins[1].name = "share links"' \
  "two plugins carry one name"
bites "two scheduled tasks under one name" \
  '.tasks[1].name = "Scan Media Library"' \
  "two scheduled tasks carry one name"
bites "a probe that wrote nothing" \
  '.configuration.after[.plugin] = .configuration.before[.plugin]' \
  "did not change when the probe wrote it"
bites "a sibling's configuration moving when this plugin's was written" \
  '.configuration.after["0f9c9107b31b459e81fa6d35dac25e79"] = { "Something": 2 }' \
  "another plugin's configuration moved"
bites "this plugin missing from the boot with the set" \
  '.plugins = [.plugins[1], .plugins[2]]' \
  "the server did not load this plugin"
bites "a scheduled task that the boot alone had" \
  '.tasks = [.tasks[1]]' \
  "is gone with the set installed"
bites "a route that the boot alone served" \
  '.paths = ["/Users/Me", "/ASibling/Queue"]' \
  "a route the server served alone is gone"
bites "a sibling route under this plugin's prefix" \
  '.paths += ["/ShareLinks/Hijacked"]' \
  "under this plugin's own prefix"
bites "a sibling route on this plugin's prefix itself" \
  '.paths += ["/ShareLinks"]' \
  "under this plugin's own prefix"

say "the near miss is accepted"
# One character away from the refusal above. A route family whose name begins
# with this plugin's prefix and continues into a different word belongs to
# somebody else, and a scan that reds on it is one an operator learns to ignore.
accepts "a sibling route on a prefix that merely starts the same way" \
  '.paths += ["/ShareLinksNot/Elsewhere"]'

say "verdict"
if [ "$failures" -gt 0 ]; then
  echo "$failures of the properties above did not hold"
  exit 1
fi
echo "every refusal bit on its own fault, and the near miss was accepted"
