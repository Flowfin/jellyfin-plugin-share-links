#!/usr/bin/env bash
# Drives a running Jellyfin with this plugin installed and makes the observations
# no test in this repository can make (#237).
#
# Three clauses on this board say a claim has to be seen on a running server
# rather than against a double. What a unit test drives is the decision. What
# this drives is a server that read the plugin out of a directory, built its own
# request pipeline around it, and answered a client that never read any of our
# code. The difference is the whole point: whether the filter this plugin adds to
# the server's options is applied at all is the server's own ordering, and
# nothing in the suite reaches it.
#
# It takes the address as an argument rather than starting the server itself, so
# the same script runs against the container the workflow starts and against a
# server somebody already has. Every step prints what it asked and what came
# back, because an observation nobody can read afterwards is not one.
set -euo pipefail

base="${1:-http://127.0.0.1:8096}"

# The account the wizard makes, and the ceiling the first observation stands on.
# Two rather than the default five, because the observation opens one session
# more than the ceiling and each one is a login round trip.
operator="observer"
password="an-observation-password-1"
ceiling=2

# The bitrate the share is capped at, in bits per second, and a request well
# above it. A fifth of a megabit, which is the lowest ceiling this plugin accepts
# and is under what the clip the workflow generates carries. That the clip really
# is above the cap is asserted further down rather than assumed: a cap above the
# item would leave the whole of the second observation true and about nothing.
cap=200000
capMbps=0.2
above=8000000

# The plugin, by the identifier its own source fixes. Written out rather than
# read out of the tree, because what this drives is a server that was handed a
# package, and this is the name that package answers to.
plugin="a3703f07-f83d-49a0-a09f-50b890a2baac"

say() { printf '\n===== %s =====\n' "$*"; }
fail() { printf '::error::%s\n' "$*" >&2; exit 1; }

# One client identity per call, because the session ceiling counts devices and a
# shared device identifier would make every login the same session.
auth_header() {
  printf 'MediaBrowser Client="observations", Device="harness", DeviceId="%s", Version="1.0.0"' "$1"
}

# curl with the status code on stdout and the body in a file, so a step can judge
# the status and read the body without asking twice.
call() {
  local method="$1" path="$2" device="$3" token="${4:-}" payload="${5:-}"
  local -a args=(-sS -X "$method" -o /tmp/observation-body -w '%{http_code}')
  args+=(-H "Authorization: $(auth_header "$device")")
  if [ -n "$token" ]; then
    args+=(-H "X-Emby-Token: $token")
  fi
  if [ -n "$payload" ]; then
    args+=(-H 'Content-Type: application/json' -d "$payload")
  fi
  curl "${args[@]}" "$base$path"
}

body() { cat /tmp/observation-body; }

say "waiting for the server to answer"
# A server that is still migrating its database answers every route with a
# splash page and a 503, so waiting for any answer at all is waiting for the
# wrong thing: the first run of this script took that page for a started server
# and walked the whole wizard into it. What is waited for is the public
# information route answering as itself, which is JSON carrying a version.
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
curl -sS "$base/System/Info/Public"
echo

say "walking the startup wizard"
status=$(call POST /Startup/Configuration wizard "" '{"UICulture":"en-US","MetadataCountryCode":"US","PreferredMetadataLanguage":"en"}')
echo "POST /Startup/Configuration -> $status"
status=$(call GET /Startup/User wizard "")
echo "GET /Startup/User -> $status"
status=$(call POST /Startup/User wizard "" "{\"Name\":\"$operator\",\"Password\":\"$password\"}")
echo "POST /Startup/User -> $status"
case "$status" in
  200 | 204) ;;
  *) fail "the wizard would not take the first account: $status $(body)" ;;
esac
status=$(call POST /Startup/RemoteAccess wizard "" '{"EnableRemoteAccess":true,"EnableAutomaticPortMapping":false}')
echo "POST /Startup/RemoteAccess -> $status"
status=$(call POST /Startup/Complete wizard "")
echo "POST /Startup/Complete -> $status"

say "signing in as the operator"
status=$(call POST /Users/AuthenticateByName operator "" "{\"Username\":\"$operator\",\"Pw\":\"$password\"}")
[ "$status" = "200" ] || fail "the operator could not sign in: $status $(body)"
token=$(body | jq -r '.AccessToken')
operator_id=$(body | jq -r '.User.Id')
echo "signed in, user $operator_id"

say "the server loaded the plugin"
status=$(call GET /Plugins operator "$token")
[ "$status" = "200" ] || fail "the plugin list could not be read: $status"
body | jq -r '.[] | "\(.Id) \(.Name) \(.Version) \(.Status)"'
# The identifier is compared with its dashes taken out of both sides. The server
# writes a plugin's identifier without them and the source fixes it with them, so
# comparing the two as written finds nothing and says the plugin is absent while
# the line above it says Active.
body | jq -e --arg id "${plugin//-/}" '[.[] | select((.Id | ascii_downcase | gsub("-"; "")) == $id)] | length == 1' >/dev/null ||
  fail "the server did not load this plugin. What it loaded is listed above."

say "configuring the plugin the way an operator would"
configuration=$(printf '{"PublicBaseUrl":"%s","GuestMaxActiveSessions":%d,"DefaultShareLifetimeDays":7}' "$base" "$ceiling")
status=$(call POST "/Plugins/$plugin/Configuration" operator "$token" "$configuration")
echo "POST /Plugins/$plugin/Configuration -> $status"
case "$status" in
  200 | 204) ;;
  *) fail "the plugin would not take its configuration: $status $(body)" ;;
esac
status=$(call GET "/Plugins/$plugin/Configuration" operator "$token")
echo "read back: $(body)"
body | jq -e --argjson want "$ceiling" '.GuestMaxActiveSessions == $want' >/dev/null ||
  fail "the ceiling did not persist"

say "adding the library and waiting for the item"
status=$(call POST "/Library/VirtualFolders?name=Observations&collectionType=movies&refreshLibrary=true" operator "$token" \
  '{"LibraryOptions":{"PathInfos":[{"Path":"/media/movies"}],"EnableRealtimeMonitor":false}}')
echo "POST /Library/VirtualFolders -> $status"
case "$status" in
  200 | 204) ;;
  *) fail "the library could not be added: $status $(body)" ;;
esac

item=""
for attempt in $(seq 1 90); do
  status=$(call GET "/Items?userId=$operator_id&includeItemTypes=Movie&recursive=true&fields=MediaSources" operator "$token")
  if [ "$status" = "200" ]; then
    item=$(body | jq -r '.Items[0].Id // empty')
    if [ -n "$item" ]; then
      echo "the item appeared after $((attempt * 2))s: $item"
      body | jq -r '.Items[0] | "\(.Name) bitrate=\(.MediaSources[0].Bitrate // "unknown")"'
      break
    fi
  fi
  sleep 2
done
[ -n "$item" ] || fail "no movie appeared in the library within 180 seconds"

say "creating a share, which is what mints the guest account"
status=$(call POST /ShareLinks/Shares operator "$token" \
  "{\"ItemId\":\"$item\",\"GuestNames\":[\"Observed Guest\"],\"MaxBitrateMbps\":$capMbps}")
[ "$status" = "200" ] || fail "the share was not created: $status $(body)"
# The names the answer actually uses. `GuestCredential` carries `Name` and
# `Credential`, and asking it for a user name and a password read `null` twice
# and turned the first sign-in into a 401 that looked like a ceiling refusal.
guest=$(body | jq -r '.Guests[0].Name')
guest_password=$(body | jq -r '.Guests[0].Credential')
[ "$guest" != "null" ] && [ -n "$guest" ] || fail "the answer named no guest: $(body)"
[ "$guest_password" != "null" ] && [ -n "$guest_password" ] || fail "the answer carried no credential: $(body)"
share=$(body | jq -r '.Share.Id')
guest_id=$(body | jq -r '.Guests[0].UserId')
echo "share $share, guest $guest, account $guest_id"

say "OBSERVATION 1: at and past the session ceiling (#56)"
# The ceiling is written onto the account the plugin made, and it is the server
# that enforces it. What the suite shows is that the number is written. What this
# shows is that the server turns away the session past it and leaves the ones
# already running alone.
tokens=()
for index in $(seq 1 "$ceiling"); do
  status=$(call POST /Users/AuthenticateByName "guest-$index" "" "{\"Username\":\"$guest\",\"Pw\":\"$guest_password\"}")
  echo "session $index -> $status"
  [ "$status" = "200" ] || fail "session $index of $ceiling was refused, and the ceiling is $ceiling: $(body)"
  tokens+=("$(body | jq -r '.AccessToken')")
done

status=$(call POST /Users/AuthenticateByName "guest-past-the-ceiling" "" "{\"Username\":\"$guest\",\"Pw\":\"$guest_password\"}")
echo "session $((ceiling + 1)) -> $status"
[ "$status" != "200" ] || fail "the session past the ceiling of $ceiling was let in"

for index in "${!tokens[@]}"; do
  status=$(call GET /Users/Me "guest-$((index + 1))" "${tokens[$index]}")
  echo "session $((index + 1)) after the refusal -> $status"
  [ "$status" = "200" ] || fail "session $((index + 1)) was disturbed by the refusal of the one past the ceiling"
done
echo "OK: $ceiling sessions ran, the next was turned away, and the ones already running were not disturbed"

guest_token="${tokens[0]}"

say "OBSERVATION 2: the cap on a real server (#65)"
# The two legs docs/bitrate-cap.md chose. The interception leg lowers what a
# playback information request asked for; the refusal leg turns away a request
# for bytes above the ceiling in force, which is the client that never asks
# politely and the one the seam tests cannot reach.
#
# Asked as the guest's own account and not the operator's. A caller naming
# somebody else's account on this route is refused by the server before anything
# this plugin does is reached: 403, with `Error processing request: Forbidden` in
# the server's log, which the first run of this step read as something about the
# cap.
status=$(call POST "/Items/$item/PlaybackInfo?maxStreamingBitrate=$above" "guest-1" "$guest_token" \
  "{\"UserId\":\"$guest_id\",\"MaxStreamingBitrate\":$above,\"AutoOpenLiveStream\":false}")
echo "PlaybackInfo asking for $above -> $status"
echo "what the server reported:"
body | jq -c '{sources: [.MediaSources[]? | {Bitrate, SupportsDirectPlay, SupportsDirectStream, TranscodingUrl}]}' 2>/dev/null || body
[ "$status" = "200" ] || fail "the playback information request was not answered: $status $(body)"

# The case has to be a real one. A clip whose own bitrate is under the ceiling
# would let every assertion below pass while nothing was ever capped, so what the
# server says the source carries is compared against the ceiling before anything
# is concluded from the two requests that follow.
carried=$(body | jq -r '[.MediaSources[]? | .Bitrate // empty] | .[0] // empty')
echo "the server says the source carries $carried, and the ceiling is $cap"
[ -n "$carried" ] || fail "the server reported no bitrate for the source, so there is nothing to compare the ceiling against"
[ "$carried" -gt "$cap" ] || fail "the clip carries $carried and the ceiling is $cap, so the ceiling is above the item and this observation would be about nothing"

# The interception leg, as far as this can see it. The filter lowers the ceiling
# the request asked for; what the server does with a lowered ceiling and no
# device profile is its own, and where it answers with the source rather than a
# transcode plan there is no lowered number in the answer to read. That is said
# rather than asserted around.
plan=$(body | jq -r '[.MediaSources[]? | .TranscodingUrl // empty] | .[0] // empty')
if [ -n "$plan" ]; then
  echo "the server answered with a transcode plan: $plan"
else
  echo "NOT OBSERVED: the server answered with the source rather than a transcode plan, so this answer carries no lowered ceiling to read. The request carried no device profile, which is what a client sends to make the server plan one."
fi

status=$(call GET "/Videos/$item/stream?static=true&videoBitRate=$above" "guest-1" "$guest_token")
echo "stream asking for $above, the ceiling being $cap -> $status"
[ "$status" = "404" ] || fail "a stream request above the ceiling was not refused: $status"

status=$(call GET "/Videos/$item/stream?static=true&videoBitRate=$((cap / 2))" "guest-1" "$guest_token")
echo "stream asking for $((cap / 2)) -> $status"
[ "$status" != "404" ] || fail "a stream request inside the ceiling was refused too, so the refusal is not about the ceiling"
echo "OK: the request above the ceiling was refused and the one inside it was not"

say "every observation this script makes was made"
