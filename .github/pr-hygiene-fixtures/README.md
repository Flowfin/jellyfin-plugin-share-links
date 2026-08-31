# PR-hygiene fixtures

Inputs that no project compiles and no tracker reads. Each directory here is
named after one verdict the `PR Hygiene` workflow takes over a pull request
itself, and holds what that verdict is watched biting on.

`violation/` carries what the check has to catch. `near-miss/` carries the
correct thing next to it, which has to stay green: a check that reds - or nags
about - a legitimate pull request is a check somebody turns off.

A fixture named `landed-commit-<sha>` comes from that commit on this board rather
than from anybody's recollection of it, and the proof script refuses it if it has
drifted - so a fixture cannot move towards the pattern and end up proving the
pattern against itself. Every other fixture is written rather than landed, and is
here for a shape the landed ones do not carry.

Both proofs run in the workflow rather than being described here:

    bash .github/scripts/prove-the-negated-closing-keyword-refusal-bites.sh
    bash .github/scripts/prove-the-version-bump-warning-bites.sh

Both need the full history, because of the comparisons above. In a shallow clone
those legs fail and say so rather than passing quietly.

## negated-closing-keyword

`.github/scripts/refuse-a-negated-closing-keyword.sh`, for #308. A tracker reads
`close #81` and closes 81 whatever stands in front of it, so a sentence written
to say an issue is NOT being finished finishes it. The script's own header
carries what the shape is, what was measured to choose it, and what it cannot
see.

One of the near misses is the repair somebody wrote by hand after the third
instance: the keyword ends a heading and the number opens the paragraph under
it, which is two blocks and is read by neither the tracker nor this check.

## version-bump-and-the-fragments

`.github/scripts/warn-on-a-version-bump-that-moves-no-fragment.sh`, for #326.
`docs/RELEASING.md` asks that the change raising `version` in `build.yaml` delete
the fragments that went into that version. Doing one and not the other releases
those fragments again under the next version, or releases a version whose notes
were never written, and neither is visible at the tag.

Each fixture here is a directory rather than a file, because the check judges a
change rather than a text: `changed-files.txt` is the changed-path list, and
`build.yaml.patch` is the diff of that one file, present only when it is in the
change. That is what the workflow writes out of the pull request.

The violation from this board is `landed-commit-30311fe4090a`, and what it is
worth needs saying exactly. That change raised `version` from `0.0.0.0` to
`0.1.0.0` and moved nothing under `changelog.d`, which is the shape this check
warns about - and it was not a mistake, because `changelog.d` did not exist yet
at that commit. So it is proof that the shape occurs here and that the warning
fires on real bytes, and it is not proof that anybody has yet made this mistake.
Nobody has: no version has been raised since. That empty population is the whole
reason #326 gave the check the warning tier.

The near miss to read first is `the-line-below-the-version-changed`. It raises
`targetAbi` and leaves `version` alone, so the diff carries the `version:` line
as context - and a check that grepped the patch for the field rather than for an
ADDED line would nag every change that touches a neighbouring field in
`build.yaml`. That is the one-character mistake, and the proof reds when it is
made.
