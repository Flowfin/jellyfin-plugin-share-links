#!/usr/bin/env bash
# Proves that `warn-on-a-version-bump-that-moves-no-fragment.sh` fires on the
# change shape it is about, stays quiet on the changes next to it, and stays at
# the tier it was given (#326).
#
# The step that runs the check on a pull request going green says nothing about
# whether the shape would have been caught. This check is at WARN, so it exits 0
# whatever it finds, and a version of it that had stopped matching altogether
# would go green in exactly the same way and would keep doing so until the first
# release - the one run where finding out costs something. So the warning is
# watched firing here, on a change that landed on this board rather than on a
# paraphrase of one, and the near misses are watched staying quiet.
#
# THE TIER IS A PROPERTY AND IS PROVEN RATHER THAN DESCRIBED. Leg 2 asserts that
# a violation exits 0. A check that quietly grew a `exit 1` would red the first
# release on a convention nobody had watched fire, which is the thing #326
# decided against.
#
# It reaches no network and changes nothing. Every subject is a directory of
# files.
set -euo pipefail

here="$(cd "$(dirname "$0")" && pwd)"
root="$(cd "$here/../.." && pwd)"
check="$here/warn-on-a-version-bump-that-moves-no-fragment.sh"
fixtures="$root/.github/pr-hygiene-fixtures/version-bump-and-the-fragments"
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT

failures=0
say() { printf '\n===== %s =====\n' "$*"; }
fail() {
  printf '::error::%s\n' "$1"
  failures=$((failures + 1))
}

say "there are fixtures to judge at all"
# A leg that walks an empty directory reports success, which looks exactly like a
# leg that ran. Count first.
violations=$(find "$fixtures/violation" -mindepth 1 -maxdepth 1 -type d | sort)
nearmisses=$(find "$fixtures/near-miss" -mindepth 1 -maxdepth 1 -type d | sort)
if [ -z "$violations" ]; then
  fail "there is no violation fixture, so nothing below proves the warning fires."
fi
if [ -z "$nearmisses" ]; then
  fail "there is no near-miss fixture, so nothing below proves the check can tell the shape from the change beside it."
fi
printf 'violation fixtures:\n%s\n' "$violations" | sed "s|$fixtures/||"
printf 'near-miss fixtures:\n%s\n' "$nearmisses" | sed "s|$fixtures/||"

say "leg 1: every violation is warned about, and the warning is an annotation"
while IFS= read -r fixture; do
  [ -n "$fixture" ] || continue
  name=$(basename "$fixture")
  set +e
  out=$(bash "$check" "$fixture" 2>&1)
  code=$?
  set -e

  if ! printf '%s\n' "$out" | grep -qx 'VERDICT: warn'; then
    fail "${name}: the check did not warn about it. What it said is below."
    printf '%s\n' "$out" | sed 's/^/    /'
    continue
  fi
  if ! printf '%s\n' "$out" | grep -q '^::warning::'; then
    fail "${name}: it reached the warn verdict and emitted no annotation, so nobody reading the pull request would see it."
    continue
  fi
  echo "warned: ${name}"
done <<<"$violations"

say "leg 2: a violation exits 0, which is the tier this check was given"
# #326 gave it the warning tier and wrote the condition that promotes it into the
# check's own header. Until that condition is met a violation must annotate and
# never red, and this is that being a property rather than a sentence.
while IFS= read -r fixture; do
  [ -n "$fixture" ] || continue
  name=$(basename "$fixture")
  set +e
  bash "$check" "$fixture" >/dev/null 2>&1
  code=$?
  set -e
  if [ "$code" -ne 0 ]; then
    fail "${name}: the check exited ${code}. It is at the warning tier and must annotate rather than red, until the promotion condition in its header is met."
    continue
  fi
  echo "annotated without reding: ${name}"
done <<<"$violations"

say "leg 3: every near miss is left alone"
while IFS= read -r fixture; do
  [ -n "$fixture" ] || continue
  name=$(basename "$fixture")
  set +e
  out=$(bash "$check" "$fixture" 2>&1)
  code=$?
  set -e
  if [ "$code" -ne 0 ]; then
    fail "${name}: the check exited ${code} on a change it has nothing to say about. What it said is below."
    printf '%s\n' "$out" | sed 's/^/    /'
    continue
  fi
  if ! printf '%s\n' "$out" | grep -qx 'VERDICT: clean'; then
    fail "${name}: the check did not read it as clean. A warning on a correct release change is a warning somebody learns to scroll past. What it said is below."
    printf '%s\n' "$out" | sed 's/^/    /'
    continue
  fi
  echo "left alone: ${name}"
done <<<"$nearmisses"

say "leg 4: the check says so when it could not judge, rather than reading clean"
# The forge returns no diff for some files. That is not the convention being
# satisfied, and a check that reported `clean` there would be silent on exactly
# the change it exists for.
cannot="$work/the-forge-returned-no-diff"
mkdir -p "$cannot"
printf 'build.yaml\n' >"$cannot/changed-files.txt"
: >"$cannot/build.yaml.patch-missing"
set +e
out=$(bash "$check" "$cannot" 2>&1)
code=$?
set -e
if [ "$code" -ne 0 ]; then
  fail "handed a change whose build.yaml diff is missing, the check exited ${code}. Not being able to read a diff is not the convention being broken."
elif ! printf '%s\n' "$out" | grep -qx 'VERDICT: cannot-judge'; then
  fail "handed a change whose build.yaml diff is missing, the check did not say it could not judge. What it said is below."
  printf '%s\n' "$out" | sed 's/^/    /'
else
  echo "said it could not judge: a change whose build.yaml diff is missing"
fi

say "leg 5: the check fails closed rather than reporting a clean run over nothing"
for bad_case in "no argument at all" "a directory that is not there" "a directory with no file list"; do
  case "$bad_case" in
    "no argument at all")
      set +e
      out=$(bash "$check" 2>&1)
      code=$?
      set -e
      ;;
    "a directory that is not there")
      set +e
      out=$(bash "$check" "$work/there-is-no-such-change" 2>&1)
      code=$?
      set -e
      ;;
    "a directory with no file list")
      empty="$work/an-empty-change"
      mkdir -p "$empty"
      set +e
      out=$(bash "$check" "$empty" 2>&1)
      code=$?
      set -e
      ;;
  esac
  if [ "$code" -eq 0 ]; then
    fail "handed ${bad_case}, the check reported success. A check pointed at nothing reads exactly like one that ran and found nothing."
    printf '%s\n' "$out" | sed 's/^/    /'
  else
    echo "failed closed: ${bad_case}"
  fi
done

say "leg 6: the landed fixtures are the landed bytes"
# Without this a fixture is somebody's recollection of a change, and a
# recollection that has drifted towards the pattern proves the pattern against
# itself.
while IFS= read -r fixture; do
  [ -n "$fixture" ] || continue
  name=$(basename "$fixture")
  case "$name" in
    landed-commit-*) ;;
    *) continue ;;
  esac
  sha=${name#landed-commit-}

  if ! git -C "$root" cat-file -e "${sha}^{commit}" 2>/dev/null; then
    fail "${name}: commit ${sha} is not in this clone, so the fixture could not be compared against the change it claims to come from. A shallow checkout is the usual reason."
    continue
  fi

  if ! diff -u <(git -C "$root" show --name-only --format= "$sha") "$fixture/changed-files.txt" >"$work/names.diff"; then
    fail "${name}: its file list is not the file list of commit ${sha}. The difference is below."
    sed 's/^/    /' "$work/names.diff"
    continue
  fi
  if ! diff -u <(git -C "$root" show --format= "$sha" -- build.yaml) "$fixture/build.yaml.patch" >"$work/patch.diff"; then
    fail "${name}: its build.yaml diff is not the diff commit ${sha} made to that file. The difference is below."
    sed 's/^/    /' "$work/patch.diff"
    continue
  fi
  echo "landed: ${name} is the change commit ${sha} made"
done <<<"$violations"

say "verdict"
if [ "$failures" -gt 0 ]; then
  echo "$failures of the properties above did not hold"
  exit 1
fi
echo "the warning fired on every violation without reding, stayed quiet on every near miss, said so when it could not judge, and failed closed when it was handed nothing"
