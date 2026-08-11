#!/usr/bin/env bash
# Refuses a coverage report in which one of the types that decide access has
# fallen below the line coverage recorded for it (#80).
#
# A number over the whole assembly is mostly a measure of how much boilerplate it
# has. This judges named types only: the token path, the store, and the routines
# that decide whether a share resolves and what a guest may then do. Everything
# else in the assembly is deliberately outside what this refuses.
#
# It takes the report as an argument rather than running the tests itself, so the
# same code can be run against a report nobody produced, which is how the check is
# shown to bite without having to delete a test to prove it.
set -euo pipefail

report="${1:-}"
floors="${2:-.github/coverage-floor.txt}"

if [ -z "$report" ]; then
  echo "usage: $0 <coverage.cobertura.xml> [floor-file]" >&2
  exit 2
fi

if [ ! -f "$report" ]; then
  echo "::error::The coverage report $report is missing. Fail closed: a check that cannot read a measurement has not made one."
  exit 1
fi

if [ ! -f "$floors" ]; then
  echo "::error::$floors is missing. Fail closed: without the recorded floors there is nothing to judge against."
  exit 1
fi

# One line per type: the name as the report spells it, and the percentage below
# which the check refuses. Comments and blank lines are dropped.
recorded=$(grep -vE '^[[:space:]]*(#|$)' "$floors" || true)
if [ -z "$recorded" ]; then
  echo "::error::$floors names no type at all. An empty floor file is not a passing check, it is an unasked question."
  exit 1
fi

# The measured value for a type is the LOWEST line rate across the report's entry
# for it and the entries for the state machines the compiler writes out of its
# async methods. Lowest rather than an average, because an average lets a fully
# covered small member pay for an uncovered branch in a large one, and because it
# needs no weighting to be honest.
measured=$(awk '
  match($0, /<class name="[^"]+"/) {
    name = substr($0, RSTART + 13, RLENGTH - 14)
    if (match($0, /line-rate="[0-9.]+"/)) {
      rate = substr($0, RSTART + 11, RLENGTH - 12) * 100
      if (!(name in lowest) || rate < lowest[name]) {
        lowest[name] = rate
      }
    }
  }
  END {
    for (name in lowest) {
      printf "%s %.2f\n", name, lowest[name]
    }
  }
' "$report")

if [ -z "$measured" ]; then
  echo "::error::$report holds no class at all. That is not full coverage, it is an empty report."
  exit 1
fi

status=0
echo "Line coverage against the recorded floor:"

while read -r type floor; do
  [ -z "$type" ] && continue

  # The type itself and anything the compiler nested inside it. The nested names
  # carry a method name, so a renamed method changes them; that direction fails
  # closed here only when the type disappears entirely, which is the case worth
  # refusing.
  actual=$(printf '%s\n' "$measured" \
    | awk -v want="$type" '{
        if ($1 == want || index($1, want "/") == 1) {
          if (found == 0 || $2 < low) { low = $2 }
          found = 1
        }
      }
      END { if (found) printf "%.2f", low }')

  if [ -z "$actual" ]; then
    printf '  %-24s %s\n' "$type" "NOT IN THE REPORT"
    echo "::error::$type has a recorded floor and does not appear in $report. Fail closed: a type that is not measured is not covered."
    status=1
    continue
  fi

  if awk -v a="$actual" -v f="$floor" 'BEGIN { exit !(a + 0 < f + 0) }'; then
    printf '  %-24s %6s%%  floor %s%%  REFUSED\n' "$type" "$actual" "$floor"
    echo "::error::$type is at $actual% line coverage and its floor is $floor%."
    status=1
  else
    printf '  %-24s %6s%%  floor %s%%\n' "$type" "$actual" "$floor"
  fi
done <<EOF
$recorded
EOF

if [ "$status" -ne 0 ]; then
  echo "Coverage is below the floor on at least one type that decides access."
  exit 1
fi

echo "Every type that decides access is at or above its recorded floor."
