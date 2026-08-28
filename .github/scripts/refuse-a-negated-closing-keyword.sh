#!/usr/bin/env bash
# Refuses a closing keyword that a negation stands in front of (#308).
#
# A tracker reads `close #81` and closes 81. It pays no attention to the word
# `not` in front of it, so a sentence written to explain why a change does NOT
# finish an issue closes that issue, and the text and the tracker then say
# opposite things. Writing the negation more clearly does not help, because the
# parser reads neither word.
#
# Usage:
#   refuse-a-negated-closing-keyword.sh <file> [file ...]
#
# Each file is one text to judge: a pull-request body, or one commit message.
# Nothing here reaches the network - the caller writes the texts out and hands
# them over as files, which is what lets the refusal be shown biting on the text
# of a landed instance without anybody having to close an issue to prove it.
#
# WHAT THE SHAPE IS, and it is a decision rather than the only reading (#308
# names three). This refuses a closing keyword IMMEDIATELY followed by an issue
# reference, with a negation standing at most three words in front of it and no
# sentence end and no blank line in between. It is the narrow reading with the
# adjacency made precise, and the precision is what it is worth: over the 377
# commit messages on the mainline and the 178 pull-request bodies this board has
# had, it refuses six, every one of the six is a real instance, and of the 125
# that carry a closing keyword and a number the other 119 go through untouched.
# The commands behind those four numbers are in the pull request that landed
# this file.
#
# A window counted in words alone was tried first and is not what this does. At
# three words it also refused `Closes #4`, `Closes #140` and `Closes #199`,
# each of them a correct closing line whose PRECEDING SENTENCE happened to
# contain a negation, and the count of those grows with the window. What
# separates them is the sentence end, so the sentence end is in the pattern.
#
# The wide reading - any closing keyword in a change that does not intend to
# close - is not decidable from the text and is not attempted.
#
# WHAT THIS CANNOT SEE. It reads text. A negation carried by a word this
# vocabulary does not hold, or one standing four words away, or one in the
# sentence before, walks past. It is a floor rather than a guarantee, in the
# same sense as the greppable invariants beside it: it holds the shape that has
# actually landed here, six times in the texts this board still holds, and it
# will not catch a shape nobody has written yet.
set -euo pipefail

if [ "$#" -eq 0 ]; then
  echo "::error::No text was handed to this check, so nothing was judged. A check that reports a clean run over an empty set is worse than no check, so this fails closed."
  exit 1
fi

for text in "$@"; do
  if [ ! -f "$text" ]; then
    echo "::error::${text} is not a file, so it was not read. Fail closed rather than report a clean run over a path that is not there."
    exit 1
  fi
done

# The negations that have actually stood in front of one of these keywords, plus
# the ordinary spellings beside them. `n't` is listed on its own because the
# lookbehind ends the word at the apostrophe, so `doesn't` reaches this as `t`
# preceded by `n'`.
negation="(?<![A-Za-z])(?:not|n't|never|no|none|neither|nor|without|nothing|cannot)(?![A-Za-z])"

# At most three words between the negation and the keyword, and a word here is
# letters with an optional trailing comma. A full stop, a semicolon or a colon
# is NOT a word character, so a sentence boundary ends the reach; and each gap
# admits at most one newline, so a blank line - a paragraph break, a heading
# ending - ends it too. Both bounds are the difference between this and a window
# counted in words, and both were measured rather than supposed.
between="(?:[ \t\r]*\n?[ \t\r]*[A-Za-z'-]+,?){0,3}[ \t\r]*\n?[ \t\r]*"

# The keywords a tracker acts on. Every spelling of each, because the tracker
# takes every spelling of each.
keyword="(?:close[sd]?|fix(?:e[sd])?|resolve[sd]?)"

# What the tracker will resolve to an issue: a bare number, the GH- form, a
# cross-repository reference, or the full URL.
reference="(?:#[0-9]+|GH-[0-9]+|[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+#[0-9]+|https?://github\.com/[^/[:space:]]+/[^/[:space:]]+/issues/[0-9]+)"

# The scan is case-blind, because the tracker is: `Close`, `close` and `CLOSE`
# all act. A pattern that read only the lower-case spelling would walk past the
# capitalised one, which is what a sentence written at the start of a line or by
# an editor that capitalises for you produces.
#
# One newline is allowed between the keyword and the reference for the same
# reason as above: a commit message is wrapped at a column, so the pair is split
# by a line ending that means nothing. Two would be a block break, which the
# tracker does not read across either - `## What this does not close` followed by
# a paragraph opening `#96 stays open.` is the repair somebody wrote here, and it
# has to stay green.
pattern="${negation}${between}\b${keyword}\b:?[ \t\r]*\n?[ \t\r]*${reference}"

refused=0

for text in "$@"; do
  hits=0

  # The whole file is one record, so a match may cross the line ending a wrap
  # put inside it. That costs the line number, which `grep` can no longer give
  # for a record that is the whole file, so it is derived from the byte offset
  # instead: the line a hit starts on is the number of newlines before it plus
  # one. A refusal naming no line sends the reader to a body to hunt for it.
  while IFS= read -r -d '' record; do
    offset="${record%%:*}"
    match="${record#*:}"
    line=$(($(head -c "$offset" -- "$text" | wc -l) + 1))
    number=$(printf '%s' "$match" | grep -oP '[0-9]+$')
    flattened=$(printf '%s' "$match" | tr '\n\r' '  ' | tr -s ' ')

    if [ "$hits" -eq 0 ]; then
      echo "REFUSED   ${text}"
    fi
    printf '    %s:%s: issue #%s, in "%s"\n' "$text" "$line" "$number" "$flattened"
    hits=$((hits + 1))
  done < <(grep -zboPi "$pattern" -- "$text" || true)

  if [ "$hits" -eq 0 ]; then
    echo "ok        ${text}"
  else
    refused=$((refused + hits))
  fi
done

if [ "$refused" -ne 0 ]; then
  echo "::error::${refused} closing keyword(s) stand behind a negation, and each one closes the issue it is written to say is not being closed. Reword so the number is not preceded by a keyword - naming the issue on its own line, or writing that it stays open, both carry the meaning and neither fires."
  exit 1
fi

echo "No closing keyword stands behind a negation in the $# text(s) read."
