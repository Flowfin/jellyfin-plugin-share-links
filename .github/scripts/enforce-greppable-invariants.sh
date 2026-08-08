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

# The line a file carries to say that it is the routine token bytes come from.
# The exemption is a marker rather than a path written here, because the paths
# this lint is handed include the fixture directories, and a fixture that draws
# bytes on purpose has to be able to say so without the lint knowing its name. A
# marker is also a line somebody sees in a diff, which a name buried in a script
# is not.
one_routine_marker='draws token bytes: this file is the one routine (#120)'

# Runs an invariant that is about how many files do a thing rather than about
# whether any file does it, so it cannot be a pattern the way the checks above
# are. $1 id, $2 the PCRE that finds a call into the generator, $3 the sentence a
# reader gets when it bites.
#
# Two arms, because a second caller arrives in two ways. It draws bytes without
# declaring itself, or it declares itself as well and there are then two routines
# where the rule allows one. A marker nothing draws behind is refused too, so a
# marker cannot be laid down in one change and used in the next.
#
# What this cannot see: a call made through reflection, a helper in a compiled
# dependency, or an alias the source gives the type. It reads the text of the
# tree, like every other invariant here.
check_sole_caller() {
  local id="$1" pattern="$2" message="$3" rc=0 callers declared undeclared unused count

  if skipped "$id"; then
    echo "skipped   ${id} (INVARIANT_SKIP)"
    return 0
  fi

  # Both sides of this comparison are lists of file names, so both are asked for
  # as file names. Taking them off numbered lines instead means cutting a name
  # out of "path:line:text", and no expression does that safely: a colon is legal
  # in a path and is the first character of a Windows drive prefix, so the cut
  # lands in the wrong place and yields nothing. An empty caller list then agrees
  # with an empty declared list and this invariant reports a clean run over a
  # file it had just matched, which is the failure the lint exists against turned
  # on the lint itself.
  callers=$(grep -rlP --include='*.cs' "$pattern" -- "${paths[@]}") || rc=$?
  if [ "$rc" -gt 1 ]; then
    echo "::error::${id}: grep exited ${rc}. The scanner is broken, so the tree is not being judged and this fails closed."
    exit 1
  fi

  callers=$(printf '%s\n' "$callers" | sort -u)
  declared=$( { grep -rlF --include='*.cs' -- "$one_routine_marker" "${paths[@]}" || true; } | sort -u)

  undeclared=$(comm -23 <(printf '%s\n' "$callers" | sed '/^$/d') <(printf '%s\n' "$declared" | sed '/^$/d'))
  unused=$(comm -13 <(printf '%s\n' "$callers" | sed '/^$/d') <(printf '%s\n' "$declared" | sed '/^$/d'))
  count=$(printf '%s\n' "$declared" | sed '/^$/d' | wc -l | tr -d '[:space:]')

  if [ -n "$undeclared" ] || [ -n "$unused" ] || [ "$count" -gt 1 ]; then
    echo "REFUSED   ${id}"
    if [ -n "$undeclared" ]; then
      printf '%s\n' "$undeclared" | sed 's|^|    draws token bytes and does not declare itself: |'
    fi
    if [ "$count" -gt 1 ]; then
      printf '%s\n' "$declared" | sed '/^$/d' | sed 's|^|    declares itself the one routine: |'
    fi
    if [ -n "$unused" ]; then
      printf '%s\n' "$unused" | sed 's|^|    declares itself the one routine and draws nothing: |'
    fi
    echo "::error::${id}: ${message}"
    violations=1
  else
    echo "ok        ${id}"
  fi
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

# Expiry is the core behaviour and it is a function of time, so a type that reads
# the machine clock directly can only be tested by sleeping or by not testing the
# boundary, which is where the bugs are (#36). The clock arrives as TimeProvider,
# and TimeProvider.System is refused for the same reason the raw property is: it
# is the machine clock wearing the seam's name.
#
# The refusal has no exemption for a composition root today, because nothing in
# the tree reads a clock at all and an exemption with no user is a hole nobody is
# watching. The day the root genuinely needs one, widening this pattern is a
# change to the rule, argued in the issue that needs it.
check "clock-comes-from-the-seam" \
  '((DateTime|DateTimeOffset)\s*\.\s*(Now|UtcNow|Today)|TimeProvider\s*\.\s*System)' \
  "the machine clock is read directly. Take TimeProvider and call GetUtcNow, so a test can stand on either side of an expiry boundary without sleeping."

# Token material comes from one routine, and one routine is a property of the
# whole tree rather than of any single line in it (#120). A second type drawing
# its own bytes is not wrong the way the invariants above are wrong: each call is
# correct on its own, and what is lost is the single place where the length, the
# encoding and the source are decided and can be changed once. The check counts
# rather than matches, which is why it is not written as a pattern above.
check_sole_caller "token-bytes-come-from-one-routine" \
  '\bRandomNumberGenerator\s*\.' \
  "the cryptographic generator is drawn from outside the one routine that owns it, or more than one file claims to be that routine. Call ShareTokens.Mint, or argue the second routine in an issue and move the marker."

if [ "$violations" -ne 0 ]; then
  echo "::error::One or more invariants were violated. Each is a rule this tree decided on; changing one is a change to the rule, not to the lint."
  exit 1
fi

echo "Every invariant held."
