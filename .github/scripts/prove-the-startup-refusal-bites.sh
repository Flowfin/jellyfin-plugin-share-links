#!/usr/bin/env bash
# Proves that `refuse-a-startup-error.sh` refuses what it names, and accepts the
# lines beside it that are not its subject.
#
# The log of a green run carries neither of the two things that rule refuses, so
# a run over it looks exactly like a rule that never fires. Every refusal is
# therefore shown to bite against a log written to carry one fault, and every
# near miss is required to be accepted, because the cost of this rule is a red
# nightly for a line that belongs to another project.
#
# It reads files. No server is started and no container is involved.
set -euo pipefail

here="$(cd "$(dirname "$0")" && pwd)"
refuse="$here/refuse-a-startup-error.sh"
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT

failures=0
say() { printf '\n===== %s =====\n' "$*"; }

# A log in the shape the pinned server writes, carrying the load line and the two
# error lines a green run really produced on both boots: the session past the
# ceiling, which the observations provoke on purpose, and a cancelled request out
# of the browser leg.
clean="$work/clean.log"
cat >"$clean" <<'LOG'
[22:11:44] [INF] [1] Main: Jellyfin version: 10.11.11
[22:11:44] [INF] [6] Emby.Server.Implementations.Plugins.PluginManager: Loaded assembly Jellyfin.Plugin.ShareLinks, Version=0.1.0.0, Culture=neutral, PublicKeyToken=null from /config/plugins/ShareLinks/Jellyfin.Plugin.ShareLinks.dll
[22:11:55] [INF] [16] Jellyfin.Plugin.ShareLinks.ShareLinksAdminController: Share 6341407c created for item 4fba0473, expiring 09/02/2026, 1 account(s) invited
[22:11:56] [ERR] [19] Jellyfin.Api.Middleware.ExceptionMiddleware: Error processing request: [172.17.0.1] User is at their maximum number of sessions. URL POST /Users/AuthenticateByName.
[22:12:00] [ERR] [6] Jellyfin.Api.Middleware.ExceptionMiddleware: Error processing request: A task was canceled. URL GET /UserViews.
[22:12:01] [WRN] [6] Emby.Server.Implementations.Library.LibraryManager: Library folder /media/movies is inaccessible or empty, skipping
LOG

judge() {
  local label="$1" file="$2" out code
  set +e
  out=$(bash "$refuse" "$file" "$label" 2>&1)
  code=$?
  set -e
  printf '%s\n' "$out" >"$work/last-output"
  return $code
}

bites() {
  local what="$1" file="$2" expected="$3"
  if judge "under test" "$file"; then
    printf '::error::%s\n' "the rule accepted a log carrying $what"
    failures=$((failures + 1))
    return
  fi
  if ! grep -qF "$expected" "$work/last-output"; then
    printf '::error::%s\n' "the rule refused a log carrying $what, but not for that reason. What it said is below."
    cat "$work/last-output"
    failures=$((failures + 1))
    return
  fi
  echo "refused: $what"
}

accepts() {
  local what="$1" file="$2"
  if judge "under test" "$file"; then
    echo "accepted: $what"
    return
  fi
  printf '::error::%s\n' "the rule refused $what, which is not its subject. What it said is below."
  cat "$work/last-output"
  failures=$((failures + 1))
}

say "the log a green run writes is accepted"
accepts "a boot that came up clean, with the two errors a real run produces" "$clean"

say "each refusal bites, on its own fault"

cp "$clean" "$work/fatal.log"
echo '[22:12:05] [FTL] [1] Main: Error while starting server.' >>"$work/fatal.log"
bites "a fatal" "$work/fatal.log" "which is the server not coming up"

cp "$clean" "$work/plugin-error.log"
echo '[22:12:06] [ERR] [7] Jellyfin.Plugin.ShareLinks.GuestConfinementFilter: the store could not be read' >>"$work/plugin-error.log"
bites "an error from this plugin" "$work/plugin-error.log" "this plugin wrote an error"

: >"$work/empty.log"
bites "an empty log" "$work/empty.log" "which is not a server that came up clean"

grep -vF 'Loaded assembly Jellyfin.Plugin.ShareLinks' "$clean" >"$work/foreign.log"
bites "the log of a server that never loaded this plugin" "$work/foreign.log" "it is not the subject"

say "each near miss is accepted"
# The rule's whole cost is here. Every line below is one somebody could mistake
# for its subject, and refusing any of them would red this board's nightly for
# something that is not this board's.

cp "$clean" "$work/sibling-error.log"
echo '[22:12:07] [ERR] [7] Jellyfin.Plugin.Stats.PlaybackReporter: could not write a row' >>"$work/sibling-error.log"
accepts "an error from a sibling plugin, which belongs to another board" "$work/sibling-error.log"

cp "$clean" "$work/plugin-warning.log"
echo '[22:12:08] [WRN] [7] Jellyfin.Plugin.ShareLinks.ShareStore: a share expired while it was being read' >>"$work/plugin-warning.log"
accepts "a warning from this plugin, which is not an error" "$work/plugin-warning.log"

cp "$clean" "$work/mention.log"
echo '[22:12:09] [ERR] [7] Jellyfin.Api.Middleware.ExceptionMiddleware: Error processing request. URL GET /ShareLinks/Guest/abc.' >>"$work/mention.log"
accepts "a server error whose message merely names this plugin's route" "$work/mention.log"

say "verdict"
if [ "$failures" -gt 0 ]; then
  echo "$failures of the properties above did not hold"
  exit 1
fi
echo "every refusal bit on its own fault, and every near miss was accepted"
