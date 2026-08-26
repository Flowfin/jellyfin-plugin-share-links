#!/usr/bin/env bash
# Refuses a collision between this plugin and the supported set, over the three
# axes #96 names: routes, scheduled task names, and configuration.
#
# It reads recorded surfaces and never a server. That is what makes it provable:
# a fabricated surface carrying a duplicate task name is a file, so the refusal
# can be shown to bite without anybody building a colliding plugin, and the proof
# runs in the same job that runs the real comparison.
#
# Usage:
#   refuse-a-collision.sh <surface.json> [<surface-of-the-boot-with-this-plugin-alone>]
#
# With one argument it judges what one boot shows on its own. With two it also
# judges the boot with the set against the boot with this plugin alone, which is
# where the axes that need two readings are decided.
#
# WHAT A SET COMPARISON CANNOT SEE, and it is the case a reader will assume is
# covered. Two plugins declaring the SAME route are one key in the server's own
# description of itself, so the path is present in both boots and no comparison
# here says anything about it. What catches that is the other half of the same
# job: `observe-on-a-real-server.sh` is run again on the boot with the set, so
# every route this plugin owns is driven end to end with the siblings present,
# and a server that cannot decide which handler owns a path fails those calls.
set -euo pipefail

surface="${1:?the surface to judge}"
alone="${2:-}"

[ -f "$surface" ] || {
  printf '::error::%s\n' "there is no surface at $surface, so nothing was judged" >&2
  exit 1
}

# The prefix both controllers declare:
#
#   git grep -n '^\[Route(' -- Jellyfin.Plugin.ShareLinks/
route_prefix="/ShareLinks"

refusals=0
say() { printf '\n===== %s =====\n' "$*"; }
refuse() {
  printf '::error::%s\n' "$*" >&2
  refusals=$((refusals + 1))
}

say "the server loaded this plugin and everything else it was given"
plugin=$(jq -r '.plugin' "$surface")
jq -e --arg p "$plugin" '[.plugins[] | select(.id == $p)] | length == 1' "$surface" >/dev/null ||
  refuse "the server did not load this plugin. What it loaded is: $(jq -r '[.plugins[].name] | join(", ")' "$surface")"

jq -r '.plugins[] | "  \(.status)  \(.name)"' "$surface"
not_running=$(jq -r '[.plugins[] | select(.status != "Active") | "\(.name) is \(.status)"] | join(", ")' "$surface")
[ -z "$not_running" ] ||
  refuse "the server loaded a plugin it will not run: $not_running. That is an incompatibility rather than a collision, and #96 says it is fixed or written down as a known limitation with its reason before this run is green again."

say "no two plugins are the same plugin"
# The identifier, which is what the server keys a plugin by, and the name, which
# is what its configuration is filed under. Two plugins agreeing on either is a
# collision before any route is served.
duplicate_ids=$(jq -r '[.plugins[].id] | group_by(.) | map(select(length > 1) | .[0]) | join(", ")' "$surface")
[ -z "$duplicate_ids" ] || refuse "two plugins carry one identifier: $duplicate_ids"
duplicate_names=$(jq -r '[.plugins[].name | ascii_downcase] | group_by(.) | map(select(length > 1) | .[0]) | join(", ")' "$surface")
[ -z "$duplicate_names" ] || refuse "two plugins carry one name, so they are filed under one configuration: $duplicate_names"

say "no two scheduled tasks carry one name"
duplicate_tasks=$(jq -r '[.tasks[].name | ascii_downcase] | group_by(.) | map(select(length > 1) | .[0]) | join(", ")' "$surface")
[ -z "$duplicate_tasks" ] || refuse "two scheduled tasks carry one name, which is what an operator picks them out by: $duplicate_tasks"

say "writing this plugin's configuration reached no other plugin's"
# The probe has to have written something. A configuration that did not move is a
# probe that wrote nothing, and its silence about every other plugin would then
# be about nothing at all.
jq -e --arg p "$plugin" '.configuration.before[$p] != .configuration.after[$p]' "$surface" >/dev/null ||
  refuse "this plugin's own configuration did not change when the probe wrote it, so the probe proved nothing about anybody else's"

moved=$(jq -r --arg p "$plugin" '
  .configuration as $c
  | [$c.after | keys[] | select(. != $p) | select($c.after[.] != $c.before[.])]
  | join(", ")' "$surface")
[ -z "$moved" ] || refuse "another plugin's configuration moved when this plugin's was written: $moved"

if [ -n "$alone" ]; then
  [ -f "$alone" ] || {
    printf '::error::%s\n' "there is no surface at $alone, so the two boots were not compared" >&2
    exit 1
  }

  say "nothing the boot with this plugin alone showed has gone"
  gone_plugins=$(jq -rn --slurpfile a "$alone" --slurpfile b "$surface" \
    '[$a[0].plugins[].id] - [$b[0].plugins[].id] | join(", ")')
  [ -z "$gone_plugins" ] || refuse "a plugin the server loaded alone is not loaded with the set installed: $gone_plugins"

  gone_tasks=$(jq -rn --slurpfile a "$alone" --slurpfile b "$surface" \
    '[$a[0].tasks[].name] - [$b[0].tasks[].name] | join(", ")')
  [ -z "$gone_tasks" ] || refuse "a scheduled task the server ran alone is gone with the set installed: $gone_tasks"

  gone_paths=$(jq -rn --slurpfile a "$alone" --slurpfile b "$surface" \
    '($a[0].paths - $b[0].paths) | join(", ")')
  [ -z "$gone_paths" ] || refuse "a route the server served alone is gone with the set installed: $gone_paths"

  say "nothing the set brought reaches into this plugin's routes"
  added=$(jq -rn --slurpfile a "$alone" --slurpfile b "$surface" \
    '($b[0].paths - $a[0].paths) | join(", ")')
  echo "the set added: ${added:-nothing}"
  # The prefix and what sits under it, and nothing that merely begins with the
  # same letters. A plain `startswith` refuses `/ShareLinksNot/...`, which is a
  # different route family owned by somebody else and not a collision at all,
  # and a scan that reds on it is a scan an operator learns to ignore.
  taken=$(jq -rn --slurpfile a "$alone" --slurpfile b "$surface" --arg prefix "$route_prefix" \
    '($b[0].paths - $a[0].paths) | map(select(. == $prefix or startswith($prefix + "/"))) | join(", ")')
  [ -z "$taken" ] || refuse "the set brought a route under this plugin's own prefix $route_prefix: $taken"
fi

say "verdict"
if [ "$refusals" -gt 0 ]; then
  echo "$refusals collision(s)"
  exit 1
fi
echo "no collision on any axis this reads"
