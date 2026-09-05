#!/usr/bin/env bash
# Drives a clean Jellyfin through what an operator does to install this plugin:
# add the repository URL, look for the plugin, install it (#90).
#
# The done-when of #90 asks that a clean server can add the repository URL and see
# the plugin, and that the checksum in the manifest matches the published
# artefact. Both are facts about a server and a catalogue this repository does not
# own, so no test here can reach them - docs/testing.md fixes that no test in this
# repository touches the network - and they were read by hand on the issue until
# this script existed.
#
# It RECORDS and judges nothing. What it writes is a file, and
# refuse-a-catalogue-that-does-not-carry-this-plugin.sh is what refuses it, so the
# refusal can be shown to bite against fabricated recordings without a server and
# without anybody breaking the published catalogue.
#
# Usage:
#   read-the-catalogue-a-clean-server-sees.sh <base> <catalogue-url> <out.json>
#
# The server must be a fresh one with no plugin directory. That is the whole point
# of the clause: an operator who has installed nothing adds a URL and finds this
# plugin offered.
set -euo pipefail

base="${1:?the address of the clean server}"
catalogue="${2:?the catalogue URL an operator would add}"
out="${3:?where to write what was seen}"

# This plugin, by the identifier build.yaml fixes. Written out rather than read
# out of the tree, because what is being compared is an entry somebody else's
# generator produced, and the guid is the name the two sides agree on.
plugin="a3703f07-f83d-49a0-a09f-50b890a2baac"

# The account the wizard makes. Nothing here depends on the name; a clean server
# has no account at all and every call below needs one.
operator="catalogue-reader"
password="a-catalogue-password-1"

say() { printf '\n===== %s =====\n' "$*"; }
fail() {
  printf '::error::%s\n' "$*" >&2
  exit 1
}

auth_header() {
  printf 'MediaBrowser Client="catalogue", Device="harness", DeviceId="%s", Version="1.0.0"' "$1"
}

call() {
  local method="$1" path="$2" device="$3" token="${4:-}" payload="${5:-}"
  local -a args=(-sS -X "$method" -o /tmp/catalogue-body -w '%{http_code}')
  args+=(-H "Authorization: $(auth_header "$device")")
  if [ -n "$token" ]; then
    args+=(-H "X-Emby-Token: $token")
  fi
  if [ -n "$payload" ]; then
    args+=(-H 'Content-Type: application/json' -d "$payload")
  fi
  curl "${args[@]}" "$base$path"
}

body() { cat /tmp/catalogue-body; }

# A dotted version as one sortable key, padded to four parts, so 10.11 and
# 10.11.0.0 are the same number. The same shape install-the-supported-set.sh
# uses, because both are choosing a release for a server line.
key_of() {
  printf '%s' "$1" | awk -F. '{printf "%05d%05d%05d%05d", $1, $2, $3, $4}'
}

say "waiting for the server to answer as itself"
# Not for any answer: a server still migrating its database answers every route
# with a splash page and a 503, and the first run of the sibling script took that
# for a started server.
answered=no
for attempt in $(seq 1 180); do
  if curl -fsS "$base/System/Info/Public" 2>/dev/null | jq -e '.Version' >/dev/null 2>&1; then
    echo "answered as itself after ${attempt}s"
    answered=yes
    break
  fi
  sleep 1
done
[ "$answered" = "yes" ] || fail "the server did not answer on $base within 180 seconds"
server_version=$(curl -sS "$base/System/Info/Public" | jq -r '.Version')
echo "server version $server_version"

say "walking the startup wizard"
call POST /Startup/Configuration wizard "" '{"UICulture":"en-US","MetadataCountryCode":"US","PreferredMetadataLanguage":"en"}' >/dev/null
call GET /Startup/User wizard "" >/dev/null
status=$(call POST /Startup/User wizard "" "{\"Name\":\"$operator\",\"Password\":\"$password\"}")
case "$status" in
  200 | 204) ;;
  *) fail "the wizard would not take the first account: $status $(body)" ;;
esac
call POST /Startup/RemoteAccess wizard "" '{"EnableRemoteAccess":true,"EnableAutomaticPortMapping":false}' >/dev/null
call POST /Startup/Complete wizard "" >/dev/null

say "signing in as the operator"
status=$(call POST /Users/AuthenticateByName operator "" "{\"Username\":\"$operator\",\"Pw\":\"$password\"}")
[ "$status" = "200" ] || fail "the operator could not sign in: $status $(body)"
token=$(body | jq -r '.AccessToken')

say "what this server offers before the repository is added"
# Recorded so the reading after it cannot be read as a server that was already
# offering this plugin. A clean Jellyfin ships the official catalogue, which does
# not carry it.
status=$(call GET /Packages operator "$token")
[ "$status" = "200" ] || fail "the package list could not be read before adding the repository: $status $(body)"
before=$(body)
printf '%s' "$before" | jq -r '.[] | "  \(.guid)  \(.name)"' | head -40
echo "  ... $(printf '%s' "$before" | jq 'length') offered in total"

say "adding the repository URL, which is what an operator types"
echo "$catalogue"
# The whole list is written rather than one entry appended, which is the route the
# dashboard takes. The official one is kept, because an operator adding a
# repository does not remove theirs.
call GET /Repositories operator "$token" >/dev/null
existing=$(body)
payload=$(printf '%s' "$existing" | jq --arg url "$catalogue" '. + [{"Name":"Flowfin","Url":$url,"Enabled":true}]')
status=$(call POST /Repositories operator "$token" "$payload")
case "$status" in
  200 | 204) ;;
  *) fail "the server would not take the repository URL: $status $(body)" ;;
esac
call GET /Repositories operator "$token" >/dev/null
body | jq -r '.[] | "  \(.Name)  \(.Url)  enabled=\(.Enabled)"'

say "what this server offers once the repository is added"
# The server caches a repository's manifest, so the list is read until this
# plugin appears rather than once. A single read is the difference between a
# catalogue that does not carry the plugin and one that had not been fetched yet.
after=""
for attempt in $(seq 1 60); do
  status=$(call GET /Packages operator "$token")
  [ "$status" = "200" ] || fail "the package list could not be read after adding the repository: $status $(body)"
  after=$(body)
  if printf '%s' "$after" | jq -e --arg g "$plugin" 'any(.[]; (.guid | ascii_downcase) == ($g | ascii_downcase))' >/dev/null; then
    echo "the plugin was offered after ${attempt}s"
    break
  fi
  sleep 1
done
printf '%s' "$after" | jq -r '.[] | "  \(.guid)  \(.name)"' | head -40
echo "  ... $(printf '%s' "$after" | jq 'length') offered in total"

entry=$(printf '%s' "$after" | jq -c --arg g "$plugin" 'map(select((.guid | ascii_downcase) == ($g | ascii_downcase))) | first // null')

# Everything below needs the entry, and its absence is the refusal's business
# rather than this script's. Recorded and handed on.
chosen="null"
computed=""
installed="null"
if [ "$entry" != "null" ]; then
  say "the version this server line can load"
  chosen_key=""
  while read -r version; do
    number=$(printf '%s' "$version" | jq -r '.version')
    abi=$(printf '%s' "$version" | jq -r '.targetAbi')
    if [ "$(key_of "$abi")" \> "$(key_of "$server_version")" ]; then
      echo "  $number targets $abi, which is above $server_version"
      continue
    fi
    this_key=$(key_of "$number")
    if [ -z "$chosen_key" ] || [ "$this_key" \> "$chosen_key" ]; then
      chosen="$version"
      chosen_key="$this_key"
    fi
  done < <(printf '%s' "$entry" | jq -c '.versions[]')

  if [ "$chosen" != "null" ]; then
    printf '%s' "$chosen" | jq -r '"  chose \(.version) targeting \(.targetAbi)"'

    say "the artefact the entry points at, hashed here rather than read off a sidecar"
    # The checksum clause is about the manifest agreeing with the PUBLISHED
    # ARTEFACT. Reading the .md5 file the release ships would compare the
    # catalogue against another thing the same release run wrote; the archive's
    # own bytes are the only independent side.
    source_url=$(printf '%s' "$chosen" | jq -r '.sourceUrl')
    echo "$source_url"
    curl -fsSL -o /tmp/catalogue-artefact.zip "$source_url" ||
      fail "the artefact the catalogue points at could not be downloaded: $source_url"
    computed=$(md5sum /tmp/catalogue-artefact.zip | cut -d' ' -f1)
    echo "  served   $(printf '%s' "$chosen" | jq -r '.checksum')"
    echo "  computed $computed"

    say "installing it the way an operator would"
    # The server verifies the checksum itself before it unpacks, so this is the
    # checksum arriving where it matters rather than a second comparison of the
    # same two strings.
    name=$(printf '%s' "$entry" | jq -r '.name')
    number=$(printf '%s' "$chosen" | jq -r '.version')
    encoded=$(printf '%s' "$name" | jq -sRr @uri)
    status=$(call POST "/Packages/Installed/$encoded?version=$number" operator "$token")
    case "$status" in
      200 | 204) echo "the server took the install request" ;;
      *) fail "the server refused the install request: $status $(body)" ;;
    esac

    for attempt in $(seq 1 120); do
      status=$(call GET /Plugins operator "$token")
      [ "$status" = "200" ] || fail "the plugin list could not be read: $status"
      found=$(body | jq -c --arg g "$plugin" 'map(select((.Id | ascii_downcase) == ($g | ascii_downcase))) | first // null')
      if [ "$found" != "null" ]; then
        echo "the server lists it after ${attempt}s"
        installed="$found"
        break
      fi
      sleep 1
    done
    if [ "$installed" = "null" ]; then
      echo "the server never listed it"
    fi
  fi
fi

say "writing what was seen"
jq -n \
  --arg plugin "$plugin" \
  --arg catalogue "$catalogue" \
  --arg serverVersion "$server_version" \
  --arg computed "$computed" \
  --argjson before "$(printf '%s' "$before" | jq '[.[] | {guid, name}]')" \
  --argjson after "$(printf '%s' "$after" | jq '[.[] | {guid, name}]')" \
  --argjson entry "$entry" \
  --argjson chosen "$chosen" \
  --argjson installed "$installed" \
  '{plugin: $plugin, catalogue: $catalogue, serverVersion: $serverVersion,
    offeredBefore: $before, offeredAfter: $after,
    entry: $entry, chosen: $chosen, computedChecksum: $computed, installed: $installed}' \
  >"$out"
echo "$out"
