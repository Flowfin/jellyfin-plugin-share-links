#!/usr/bin/env bash
# Warns when a change raises `version` in build.yaml and moves no release-note
# fragment (#326).
#
# `docs/RELEASING.md` asks for the two together: step 2 raises `version` in
# `build.yaml`, and the paragraph after step 5 says the fragments that went into
# that version are deleted in the same change. A change that does one and not the
# other ships a version whose fragments will be released again under the next
# one, or a version whose notes were never written. Neither is visible at the
# tag: `changelog.d` is assembled without complaint either way, so the mistake is
# discovered by an operator reading the wrong notes.
#
# Usage:
#   warn-on-a-version-bump-that-moves-no-fragment.sh <change-directory>
#
# The directory describes ONE change and holds:
#
#   changed-files.txt      one changed path per line, required
#   build.yaml.patch       the unified diff of build.yaml, present only when
#                          build.yaml is in the change
#   build.yaml.patch-missing
#                          written instead when build.yaml is in the change and
#                          the forge returned no patch for it
#
# Nothing here reaches the network. The caller writes the change out and hands
# over a directory, which is what lets the warning be shown firing on a change
# nobody has to make.
#
# THE TIER IS WARN AND THIS SCRIPT NEVER REDS FOR THE CONVENTION. Decided on
# #326: the shape is near enough certain to be right when it fires, and it has
# never run - its first firing would be on the first release of this plugin,
# which is the worst moment to find out that a new gate has an edge nobody
# anticipated. A warning annotates, is seen, and costs nothing if it is wrong.
#
# WHAT PROMOTES IT TO FAIL, so that nobody has to remember: it moves to the
# failing tier once it has fired correctly on a real version bump - a pull
# request that raised `version` in `build.yaml` on this board, on which this
# check's verdict was read and found right. Until that has happened the
# population it would judge is empty, which is the whole reason it is here at
# WARN. Promoting it is a one-line change to the workflow step that calls it,
# plus this paragraph saying which pull request fired it.
#
# IT DOES RED WHEN IT COULD NOT JUDGE, and that is not the convention firing. A
# check pointed at a directory that is not there reports a clean run over
# nothing and reads exactly like one that ran, so that case fails closed. The one
# thing it will not do is red a pull request for the shape it is about.
#
# WHAT IT CANNOT SEE. It reads a file list and one diff. A version raised in a
# change that also touches `changelog.d/README.md` and no fragment is caught,
# because the README is not a fragment; a version raised beside a fragment that
# belongs to some other version is not, because which fragments went into a
# version is not written anywhere a check could read. It is a floor rather than
# a guarantee.
set -euo pipefail

if [ "$#" -ne 1 ]; then
  echo "::error::This check takes exactly one argument, the directory describing the change. It was handed $#, so nothing was judged and this fails closed rather than reporting a clean run over nothing."
  exit 1
fi

change="$1"

if [ ! -d "$change" ]; then
  echo "::error::${change} is not a directory, so the change was not read. Fail closed rather than report a clean run over a path that is not there."
  exit 1
fi

changed_files="$change/changed-files.txt"
if [ ! -f "$changed_files" ]; then
  echo "::error::${changed_files} is not there, so no changed path was read. A judgement over an empty file list passes every time and looks exactly like one that ran."
  exit 1
fi

patch="$change/build.yaml.patch"
patch_missing="$change/build.yaml.patch-missing"

# THE VERSION LINE HAS TO BE ADDED, NOT MERELY PRESENT. A unified diff carries
# the surrounding lines with a leading space, so `version: "0.1.0.0"` appears in
# the patch of every change that touches a neighbouring field - `targetAbi`, say,
# which sits four lines away. Reading the patch for the field rather than for an
# ADDED line is the one-character mistake this check is most likely to be written
# with, and `near-miss/the-line-below-the-version-changed` is that change.
version_added=0
if [ -f "$patch" ] && grep -qE '^\+[[:space:]]*version:' -- "$patch"; then
  version_added=1
fi

# A fragment is a file in `changelog.d` that is not the convention document.
# `changelog.d/README.md` says what a fragment looks like; it is not one, and a
# change that raises the version while editing only the README has moved no
# note.
fragments_moved=$(
  grep -cE '^changelog\.d/(.*/)?[^/]+$' -- "$changed_files" 2>/dev/null || true
)
readme_only=$(
  grep -cE '^changelog\.d/README\.md$' -- "$changed_files" 2>/dev/null || true
)
fragments_moved=$((fragments_moved - readme_only))

if [ -f "$patch_missing" ]; then
  echo "VERDICT: cannot-judge"
  echo "::warning::build.yaml is in this change and the forge returned no diff for it, so whether \`version\` was raised was not read. The rule this check holds is in docs/RELEASING.md: the change that raises the version deletes the fragments that went into it. Check it by hand on this one."
  exit 0
fi

if [ "$version_added" -eq 0 ]; then
  echo "VERDICT: clean"
  echo "No line adding \`version:\` to build.yaml is in this change, so the fragment rule does not apply to it."
  exit 0
fi

if [ "$fragments_moved" -gt 0 ]; then
  echo "VERDICT: clean"
  echo "This change raises \`version\` in build.yaml and moves ${fragments_moved} fragment(s) under changelog.d with it."
  exit 0
fi

echo "VERDICT: warn"
echo "::warning::This change adds a \`version:\` line to build.yaml and moves no fragment under changelog.d. docs/RELEASING.md asks for both in one change: the fragments that went into a version are deleted as it is raised. Leaving them behind releases them again under the next version; raising the version with none there releases a version whose notes were never written. changelog.d/README.md is not a fragment and does not satisfy this."
exit 0
