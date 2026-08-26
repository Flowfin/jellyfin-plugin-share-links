#!/usr/bin/env bash
# Records the surface a running Jellyfin presents, so two boots can be compared
# on the three axes #96 names: routes, scheduled task names, and configuration.
#
# It reads and writes a file. It judges nothing, and that separation is the
# point: what refuses a collision is `refuse-a-collision.sh`, which reads these
# files and never a server, so the refusal can be shown to bite against a
# fabricated surface without anybody having to build a colliding plugin to prove
# it.
#
# It runs after `observe-on-a-real-server.sh` against the same server, and takes
# that script's session rather than signing in again. Two scripts holding one
# password would be one password with two homes, and the first of them to be
# changed would leave the other reading a server it can no longer sign in to.
#
# Usage:
#   read-the-server-surface.sh <base> <out.json>
set -euo pipefail

base="${1:?the address of the server to read}"
out="${2:?the file to record the surface in}"

session="/tmp/observation-session"

# This plugin, by the identifier its own source fixes, and the prefix both its
# controllers declare:
#
#   git grep -n '^\[Route(' -- Jellyfin.Plugin.ShareLinks/
#
# Written out for the reason the observation script writes the identifier out:
# what this reads is a server that was handed a package, and these are the names
# that package answers to.
plugin="a3703f07-f83d-49a0-a09f-50b890a2baac"

say() { printf '\n===== %s =====\n' "$*"; }
fail() {
  printf '::error::%s\n' "$*" >&2
  exit 1
}

[ -f "$session" ] || fail "there is no session at $session, so the observations were never made against this server and there is nothing signed in to read it with"
token=$(jq -r '.token' "$session")
[ -n "$token" ] && [ "$token" != "null" ] || fail "the session at $session carries no token"

call() {
  local path="$1"
  curl -sS -o /tmp/surface-body -w '%{http_code}' \
    -H 'Authorization: MediaBrowser Client="observations", Device="surface", DeviceId="surface", Version="1.0.0"' \
    -H "X-Emby-Token: $token" \
    "$base$path"
}

body() { cat /tmp/surface-body; }

say "the plugins the server loaded"
status=$(call /Plugins)
[ "$status" = "200" ] || fail "the plugin list could not be read: $status $(body)"
# Two identifiers per plugin, and the difference matters. `id` has the dashes
# taken out and the case flattened, which is what two boots are compared on,
# because the server writes an identifier one way and a source file fixes it
# another. `raw` is what the server actually wrote, and it is what a route is
# asked with, because the form the server prints is the form it answers to.
plugins=$(body | jq -c '[.[] | {id: (.Id | ascii_downcase | gsub("-"; "")), raw: .Id, name: .Name, version: .Version, status: .Status}] | sort_by(.name)')
printf '%s' "$plugins" | jq -r '.[] | "  \(.raw)  \(.status)  \(.name) \(.version)"'

say "the scheduled tasks the server runs"
status=$(call /ScheduledTasks)
[ "$status" = "200" ] || fail "the scheduled tasks could not be read: $status $(body)"
tasks=$(body | jq -c '[.[] | {name: .Name, key: .Key, category: .Category}] | sort_by(.name)')
printf '%s' "$tasks" | jq -r '.[] | "  \(.name)  [\(.key)]"'

say "the routes the server serves"
# Taken from the server's own description of itself rather than from anything in
# this tree, because what is being compared is what two boots of a server offer
# and not what a source file declares. The address is asked for rather than
# assumed: a server that moved it would otherwise leave this recording an empty
# route set, and an empty set compares clean against another empty set.
paths=""
for candidate in /api-docs/openapi.json /openapi.json; do
  status=$(call "$candidate")
  echo "$candidate -> $status"
  if [ "$status" = "200" ] && body | jq -e '.paths | type == "object"' >/dev/null 2>&1; then
    paths=$(body | jq -c '.paths | keys')
    echo "read from $candidate"
    break
  fi
done
[ -n "$paths" ] || fail "the server described none of its routes at any address this knows, so there is no route surface to compare. What was tried is printed above."
echo "$(printf '%s' "$paths" | jq 'length') routes"

say "whether writing this plugin's configuration reaches any other plugin's"
# The third axis, observed rather than reasoned about. Every loaded plugin's
# configuration is read, this plugin's is changed, and every one of them is read
# again. Two plugins sharing a configuration store is the fight this axis is
# about, and it shows up here as somebody else's configuration moving when
# nobody asked it to.
read_every_configuration() {
  local out="{}" raw id name got
  while read -r raw; do
    id=$(printf '%s' "$plugins" | jq -r --arg r "$raw" '.[] | select(.raw == $r) | .id')
    name=$(printf '%s' "$plugins" | jq -r --arg r "$raw" '.[] | select(.raw == $r) | .name')
    got=$(call "/Plugins/$raw/Configuration")
    if [ "$got" = "200" ]; then
      out=$(printf '%s' "$out" | jq -c --arg i "$id" --argjson c "$(body)" '.[$i] = $c')
    else
      # A plugin with no configuration page answers something other than 200,
      # and that is an absence rather than a failure. It is recorded as the
      # status so the comparison has something to compare and the reader can see
      # which plugins were silent.
      out=$(printf '%s' "$out" | jq -c --arg i "$id" --arg s "no configuration, $got" '.[$i] = $s')
    fi
    # To stderr, not stdout. What this function writes on stdout is the JSON its
    # caller captures, and a progress line mixed into that is a document that no
    # longer parses - which is how the first run of this step failed.
    echo "  $name -> $got" >&2
  done < <(printf '%s' "$plugins" | jq -r '.[].raw')
  printf '%s' "$out"
}

echo "before:"
before=$(read_every_configuration)

# The change is made to a field the observations already set, so it is a field
# this plugin really holds rather than one invented here, and it is made after
# every observation so nothing downstream is standing on the old value.
status=$(call "/Plugins/$plugin/Configuration")
[ "$status" = "200" ] || fail "this plugin's configuration could not be read: $status $(body)"
changed=$(body | jq -c '.DefaultShareLifetimeDays = 6')
status=$(curl -sS -o /tmp/surface-body -w '%{http_code}' -X POST \
  -H 'Authorization: MediaBrowser Client="observations", Device="surface", DeviceId="surface", Version="1.0.0"' \
  -H "X-Emby-Token: $token" \
  -H 'Content-Type: application/json' \
  -d "$changed" \
  "$base/Plugins/$plugin/Configuration")
echo "POST /Plugins/$plugin/Configuration -> $status"
case "$status" in
  200 | 204) ;;
  *) fail "this plugin would not take the configuration the probe writes: $status $(body)" ;;
esac

echo "after:"
after=$(read_every_configuration)

jq -n \
  --arg base "$base" \
  --arg plugin "${plugin//-/}" \
  --argjson plugins "$plugins" \
  --argjson tasks "$tasks" \
  --argjson paths "$paths" \
  --argjson before "$before" \
  --argjson after "$after" \
  '{base: $base, plugin: $plugin, plugins: $plugins, tasks: $tasks, paths: $paths, configuration: {before: $before, after: $after}}' \
  >"$out"

say "recorded"
echo "$out"
jq -c '{plugins: (.plugins | length), tasks: (.tasks | length), paths: (.paths | length)}' "$out"
