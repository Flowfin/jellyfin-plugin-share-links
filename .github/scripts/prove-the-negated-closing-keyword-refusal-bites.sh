#!/usr/bin/env bash
# Proves that `refuse-a-negated-closing-keyword.sh` refuses the text that has
# actually closed an issue on this board, and that it leaves alone the text next
# to it (#308).
#
# The step that runs the check on a pull request going green means today's body
# and today's commit messages are clean. It says nothing about whether the shape
# would have been caught, and a pattern that silently stopped matching passes in
# exactly the same way. So the refusal is watched biting here, on the landed
# bytes rather than on a paraphrase of them, and the near misses are watched
# being accepted, because a check that reds a correct closing line is a check
# somebody turns off.
#
# It reaches no network and closes nothing. Every subject is a file.
set -euo pipefail

here="$(cd "$(dirname "$0")" && pwd)"
root="$(cd "$here/../.." && pwd)"
refuse="$here/refuse-a-negated-closing-keyword.sh"
fixtures="$root/.github/pr-hygiene-fixtures/negated-closing-keyword"
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT

failures=0
say() { printf '\n===== %s =====\n' "$*"; }
fail() {
  printf '::error::%s\n' "$1"
  failures=$((failures + 1))
}

say "there are fixtures to judge at all"
# A leg that walks an empty directory reports success, which looks exactly like
# a leg that ran. Count first.
violations=$(find "$fixtures/violation" -maxdepth 1 -name '*.txt' | sort)
nearmisses=$(find "$fixtures/near-miss" -maxdepth 1 -name '*.txt' | sort)
if [ -z "$violations" ]; then
  fail "there is no violation fixture, so nothing below proves the check bites."
fi
if [ -z "$nearmisses" ]; then
  fail "there is no near-miss fixture, so nothing below proves the check can tell the mistake from the correct line beside it."
fi
printf 'violation fixtures:\n%s\n' "$violations" | sed "s|$fixtures/||"
printf 'near-miss fixtures:\n%s\n' "$nearmisses" | sed "s|$fixtures/||"

say "leg 1: every violation is refused, and the refusal names the file, the line and the number"
while IFS= read -r fixture; do
  [ -n "$fixture" ] || continue
  name=$(basename "$fixture")
  set +e
  out=$(bash "$refuse" "$fixture" 2>&1)
  code=$?
  set -e

  if [ "$code" -eq 0 ]; then
    fail "${name}: the check accepted it. The refusal does not bite."
    continue
  fi

  hits=$(printf '%s\n' "$out" | grep -oP '^\s+\Q'"$fixture"'\E:\K[0-9]+: issue #[0-9]+' || true)
  if [ -z "$hits" ]; then
    fail "${name}: it was refused, but the refusal does not name that file with a line and a number. What it said is below."
    printf '%s\n' "$out" | sed 's/^/    /'
    continue
  fi

  # EVERY hit is read rather than the first one. A text carrying two of the shape
  # is where a check that reported one and stopped looks exactly like a check
  # that found both, and one of these fixtures carries two.
  bad=0
  while IFS= read -r hit; do
    [ -n "$hit" ] || continue
    line=${hit%%:*}
    number=${hit##*#}

    # The line and the number are checked against the fixture rather than taken
    # on the check's word, because a refusal naming a line somebody then cannot
    # find is the failure this whole check exists against, one register over.
    # The named line and the one after it are both read: the pattern allows a
    # single wrap between the keyword and the reference, so the number
    # legitimately sits on the next line.
    if ! sed -n "${line},$((line + 1))p" "$fixture" | grep -qF "#${number}"; then
      fail "${name}: the refusal named line ${line} and issue #${number}, and that number is not on that line of the fixture."
      bad=1
      continue
    fi
    echo "refused: ${name} at line ${line}, issue #${number}"
  done <<<"$hits"
  [ "$bad" -eq 0 ] || continue
done <<<"$violations"

say "leg 2: every near miss is accepted, one at a time and as a set"
while IFS= read -r fixture; do
  [ -n "$fixture" ] || continue
  name=$(basename "$fixture")
  set +e
  out=$(bash "$refuse" "$fixture" 2>&1)
  code=$?
  set -e
  if [ "$code" -ne 0 ]; then
    fail "${name}: the check refused it. It cannot tell the mistake from the correct line beside it. What it said is below."
    printf '%s\n' "$out" | sed 's/^/    /'
    continue
  fi
  echo "accepted: ${name}"
done <<<"$nearmisses"

set +e
# shellcheck disable=SC2086
out=$(bash "$refuse" $nearmisses 2>&1)
code=$?
set -e
if [ "$code" -ne 0 ]; then
  fail "the near misses were refused when handed over together, though each was accepted alone. What it said is below."
  printf '%s\n' "$out" | sed 's/^/    /'
else
  echo "accepted: all of them in one run"
fi

say "leg 3: the check fails closed rather than reporting a clean run over nothing"
set +e
out=$(bash "$refuse" 2>&1)
code=$?
set -e
if [ "$code" -eq 0 ]; then
  fail "handed no file at all, the check reported success. A check pointed at an empty set reads exactly like one that ran and found nothing."
else
  echo "failed closed: no file"
fi

set +e
out=$(bash "$refuse" "$work/there-is-no-such-text.md" 2>&1)
code=$?
set -e
if [ "$code" -eq 0 ]; then
  fail "handed a path that is not there, the check reported success."
else
  echo "failed closed: a path that is not there"
fi

say "leg 4: the landed fixtures are the landed bytes"
# Without this the fixtures are somebody's recollection of what was written, and
# a recollection that has drifted towards the pattern proves the pattern against
# itself. Each fixture named for a commit has to be a literal substring of that
# commit's message on this board.
while IFS= read -r fixture; do
  [ -n "$fixture" ] || continue
  name=$(basename "$fixture")
  case "$name" in
    landed-commit-*) ;;
    *) continue ;;
  esac
  sha=${name#landed-commit-}
  sha=${sha%.txt}

  if ! message=$(git -C "$root" log -1 --format='%B' "$sha" 2>/dev/null); then
    fail "${name}: commit ${sha} is not in this clone, so the fixture could not be compared against the message it claims to come from. A shallow checkout is the usual reason."
    continue
  fi

  # A newline is not a character `[[ ]]` can be asked about inside a pattern, so
  # both sides are flattened onto one line first. The substring test is literal:
  # the quoted expansion inside the pattern is not read as a glob.
  flat_message=$(printf '%s' "$message" | tr '\n' '\001')
  flat_fixture=$(tr '\n' '\001' <"$fixture")
  if [[ $flat_message != *"$flat_fixture"* ]]; then
    fail "${name}: its bytes are not in the message of commit ${sha}, so it is a paraphrase rather than the text that landed."
    continue
  fi
  echo "landed: ${name} is a literal excerpt of ${sha}"
done <<<"$violations"

say "verdict"
if [ "$failures" -gt 0 ]; then
  echo "$failures of the properties above did not hold"
  exit 1
fi
echo "the refusal bit on every landed instance, named the file, the line and the number in each, accepted every near miss, and failed closed when it was handed nothing"
