#!/usr/bin/env bash
# Refuses text this tree has decided may not appear in its C# sources (#16).
#
# Every invariant here is a pattern over source text, which is a crude instrument
# on purpose: it catches the mistake that looks ordinary in review, and it cannot
# reason about types or flow. Where a pattern would have to reason, the invariant
# is left out rather than written badly, and what is left out is said in the
# comment above it.
#
# Usage:
#   enforce-greppable-invariants.sh [path ...]
#
# With no path it scans the plugin and test sources. A path is taken as given, so
# the same code can be run over a fixture that violates one invariant on purpose,
# which is how each one is shown to bite without shipping a violation to prove it.
#
# INVARIANT_SKIP is a comma-separated list of invariant ids to leave unchecked.
# Nothing in CI sets it except the proof, which uses it to show that the fixture
# a given invariant refuses goes green the moment that invariant is removed.
set -euo pipefail

default_paths=("Jellyfin.Plugin.ShareLinks" "Jellyfin.Plugin.ShareLinks.Tests")

if [ "$#" -gt 0 ]; then
  paths=("$@")
else
  paths=("${default_paths[@]}")
fi

for path in "${paths[@]}"; do
  if [ ! -e "$path" ]; then
    echo "::error::${path} does not exist, so nothing was scanned. Fail closed rather than report a clean run over an empty set."
    exit 1
  fi
done

skip="${INVARIANT_SKIP:-}"

skipped() {
  case ",${skip}," in
    *",$1,"*) return 0 ;;
    *) return 1 ;;
  esac
}

violations=0

# Runs one invariant. $1 id, $2 the PCRE, $3 the sentence a reader gets when it
# bites. grep exit codes: 0 a match, 1 none, anything else a broken scanner, and
# a broken scanner is failed closed rather than read as a clean tree.
check() {
  local id="$1" pattern="$2" message="$3" rc=0 hits

  if skipped "$id"; then
    echo "skipped   ${id} (INVARIANT_SKIP)"
    return 0
  fi

  hits=$(grep -rnP --include='*.cs' "$pattern" -- "${paths[@]}") || rc=$?

  case "$rc" in
    0)
      echo "REFUSED   ${id}"
      printf '%s\n' "$hits" | sed 's/^/    /'
      echo "::error::${id}: ${message}"
      violations=1
      ;;
    1)
      echo "ok        ${id}"
      ;;
    *)
      echo "::error::${id}: grep exited ${rc}. The scanner is broken, so the tree is not being judged and this fails closed."
      exit 1
      ;;
  esac
}

echo "Scanning: ${paths[*]}"

# A token in a log line is a token in a log file, a log shipper and whatever
# retains them, and it outlives the share it belongs to. The pattern refuses a
# token-shaped name reaching a logging call as a VALUE - a structured-logging
# placeholder or an interpolation, or an argument after the format string. Prose
# mentioning the word token in a message is not refused, because a message that
# cannot say what it is about is not worth the invariant.
check "token-not-logged" \
  'Log(Trace|Debug|Information|Warning|Error|Critical)\([^)]*(\{[^}]*([Tt]oken|[Ss]ecret)[^}]*\}|,\s*[A-Za-z_.]*([Tt]oken|[Ss]ecret)[A-Za-z_.]*\s*[,)])' \
  "a logging call takes a token or secret as a value. Log the share identifier instead; it names the same record and is not a credential."

# An ordinary comparison over a secret returns as soon as two bytes differ, and
# the time it took is a measurement of how much of the secret was right. The
# refusal is over the comparison forms, not over the types, so a name carrying
# Token, Secret or Hash is the whole signal available to a pattern.
check "token-compared-in-constant-time" \
  '(([A-Za-z_.]*([Tt]oken|[Ss]ecret|[Hh]ash)[A-Za-z_.]*)\s*(==|!=)|(==|!=)\s*[A-Za-z_.]*([Tt]oken|[Ss]ecret|[Hh]ash)[A-Za-z_.]*|\.(Equals|SequenceEqual)\(\s*[A-Za-z_.]*([Tt]oken|[Ss]ecret|[Hh]ash))' \
  "a token, secret or hash is compared with an ordinary comparison, which returns early and leaks how much of it was right. Use CryptographicOperations.FixedTimeEquals."

# System.Random is seeded from a source an attacker can reason about and is
# documented as unsuitable for anything security-sensitive. This plugin has no
# use for it at all, so the refusal is over the whole source rather than over a
# token path that would have to be identified by name. Guid.NewGuid is
# deliberately NOT in this set: it is not a token source here either, but it is a
# reasonable identifier for a share record, and refusing it would trade a real
# invariant for a false positive nobody would keep.
check "token-randomness-is-cryptographic" \
  '(new\s+Random\s*\(|Random\.Shared|System\.Random\b)' \
  "a non-cryptographic random source is used. Token material comes from RandomNumberGenerator."

# A route reachable without authentication is the whole design undone: sharing is
# for invited guests who sign in, and there are no anonymous public links. The
# attribute is the one line that would make a route anonymous, so the pattern is
# exact rather than clever.
check "route-is-not-anonymous" \
  '\[\s*AllowAnonymous\s*(\(\s*\))?\s*\]' \
  "a route is marked AllowAnonymous. Every route this plugin serves is reached by a caller the server has already identified."

if [ "$violations" -ne 0 ]; then
  echo "::error::One or more invariants were violated. Each is a rule this tree decided on; changing one is a change to the rule, not to the lint."
  exit 1
fi

echo "Every invariant held."
