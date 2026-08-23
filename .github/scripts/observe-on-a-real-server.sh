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

# Both of them, because the second is what the guest must not reach and a
# library holding one item cannot tell a confinement that works from one that was
# never asked a question.
item=""
other=""
for attempt in $(seq 1 90); do
  status=$(call GET "/Items?userId=$operator_id&includeItemTypes=Movie&recursive=true&fields=MediaSources&sortBy=SortName" operator "$token")
  if [ "$status" = "200" ]; then
    item=$(body | jq -r '.Items[0].Id // empty')
    other=$(body | jq -r '.Items[1].Id // empty')
    if [ -n "$item" ] && [ -n "$other" ]; then
      echo "both items appeared after $((attempt * 2))s"
      body | jq -r '.Items[] | "  \(.Id) \(.Name) bitrate=\(.MediaSources[0].Bitrate // "unknown")"'
      break
    fi
  fi
  sleep 2
done
[ -n "$item" ] && [ -n "$other" ] || fail "the two movies did not both appear in the library within 180 seconds"
[ "$item" != "$other" ] || fail "the library answered with one item twice, so there is nothing to be refused"

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
# The link the operator sends, taken out of the answer rather than assembled
# here, because what observation 5 opens has to be the bytes this plugin hands
# over and not a second construction of them.
link=$(body | jq -r '.Link')
[ "$link" != "null" ] && [ -n "$link" ] || fail "the answer carried no link: $(body)"
echo "share $share, guest $guest, account $guest_id"
echo "link $link"

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

say "OBSERVATION 3: what the guest cannot reach (#44, #47, #52)"
# The widening the whole plugin exists against, asked of a real server rather
# than of a filter holding a fabricated authorization context. Whether the server
# runs the filter at all is the server's own pipeline ordering, and nothing in
# the suite reaches it.
status=$(call GET "/Items/$item?userId=$guest_id" "guest-1" "$guest_token")
echo "the item the share names -> $status"
[ "$status" = "200" ] || fail "the guest could not reach the item their own share names: $status $(body)"

status=$(call GET "/Items/$other?userId=$guest_id" "guest-1" "$guest_token")
echo "the other item in the same library -> $status"
[ "$status" = "404" ] || fail "the guest reached an item no share of theirs names: $status"

status=$(call GET "/Items?userId=$guest_id&includeItemTypes=Movie&recursive=true" "guest-1" "$guest_token")
echo "a listing that would enumerate the library -> $status"
[ "$status" = "404" ] || fail "the guest enumerated the library: $status"
echo "OK: the shared item was reached, the other one was not, and the listing was refused"

say "OBSERVATION 4: the routes a guest is refused by the server (#47)"
# Three lines of docs/negative-capabilities.md say the refusal belongs to the
# server's own authorization over its own routes, and that no test in this
# repository can hold them. That is true of a test. It is not a reason to leave
# the claim unread: what this plugin contributes is an account that is not an
# administrator, and whether the server then refuses is a question a running
# server answers.
#
# Every route below is one the server itself gates behind elevation, read out of
# its own source at the version this runs against rather than assumed. A route
# gated behind ordinary authentication is a different claim and is printed below
# instead of asserted.
#
# What is asserted is that the answer is not the resource. Which refusal a server
# gives is the server's own, so a run demanding a particular status would red on a
# server that chose the other for a reason that has nothing to do with this
# plugin.
for route in \
  "/ScheduledTasks" \
  "/Plugins/$plugin/Configuration" \
  "/System/Info/Storage" \
  "/System/Logs" \
  "/ShareLinks/Shares"; do
  status=$(call GET "$route" "guest-1" "$guest_token")
  echo "$route -> $status"
  [ "$status" != "200" ] || fail "the guest was served $route, which the server gates behind elevation"
done
echo "OK: none of the five answered the guest with the resource"

# And one that is not gated behind elevation upstream, printed rather than
# asserted. An invited guest is a signed-in account on the operator's server, so
# everything the server gives an ordinary account it gives them. This is the
# nearest route to the administrator surface that is not part of it.
status=$(call GET "/System/Configuration" "guest-1" "$guest_token")
echo "/System/Configuration -> $status"
if [ "$status" = "200" ]; then
  echo "NOT REFUSED, AND NOT A DEFECT OF THIS PLUGIN: the server gates reading its configuration behind ordinary authentication rather than behind elevation, so an invited guest reads it like any signed-in account. Writing it is gated behind elevation. docs/limits.md carries this where an operator meets it."
fi

say "OBSERVATION 5: the link opened in a real browser (#75)"
# The refusal `docs/refused-tests.md` names first is a test that starts a real
# server and drives a browser. That refusal is about the suite, which may reach
# neither. This job already brings a server up, so the browser is the one
# instrument left that reads what a guest meets when they click what an operator
# sent them, and two claims in the tree are about nothing else:
# `ShareLinksGuestController.TheItemsAddress` says its address was not measured
# against a running web client, and `docs/operator-guide.md` says the guest signs
# in and then opens the link.
#
# Two legs before the browser, because they are cheap and they say which half of
# the answer the browser is showing.
#
# The link carrying no identity at all. Asserted rather than recorded: the whole
# design rests on the server identifying the caller, so a link that resolves for
# a request carrying nothing is the threat model gone.
status=$(curl -sS -o /tmp/observation-body -w '%{http_code}' "$link")
echo "the link with no identity at all -> $status"
[ "$status" != "302" ] || fail "the link resolved for a caller the server never identified"

# And the same link carrying the guest's own token in a header the server reads,
# which is the path the route tests drive. Made once here so that whatever the
# browser does can be read against a request that is known to resolve.
status=$(call GET "${link#"$base"}" "guest-1" "$guest_token")
echo "the link carrying the guest's token -> $status"
[ "$status" = "302" ] || fail "the link did not resolve for the guest it names: $status"

# The browser lives beside its own pinned dependency rather than beside this
# script, so what drives it is the version `.github/browser/package-lock.json`
# records and not whatever is installed on the machine. A hand run installs it
# the way the workflow does:
#
#   npm --prefix .github/browser ci
#   npx --prefix .github/browser playwright install --with-deps chromium
browser="$(dirname "$0")/../browser"
if [ ! -d "$browser/node_modules/playwright" ]; then
  fail "the browser this leg drives is not installed. The two commands are in the comment above this line."
fi

OBSERVE_BASE="$base"   OBSERVE_LINK="$link"   OBSERVE_GUEST="$guest"   OBSERVE_CREDENTIAL="$guest_password"   OBSERVE_ITEM="$item"   OBSERVE_OTHER_ITEM="$other"   node "$browser/observe-in-a-browser.mjs"

say "every observation this script makes was made"
