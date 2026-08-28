# PR-hygiene fixtures

Texts that no project compiles and no tracker reads. Each directory here is named
after one refusal the `PR Hygiene` workflow makes over a pull request's own text,
and holds the bytes that refusal is watched biting on.

`violation/` carries what the refusal has to refuse. `near-miss/` carries the
correct writing next to it, which has to stay green: a check that reds a
legitimate closing line is a check somebody turns off, and this whole family
exists because a body explaining why a change does NOT finish an issue is the
body most likely to be parsed as finishing it.

A file named `landed-commit-<sha>.txt` is a literal excerpt of that commit's
message on this board, and the proof script refuses it if it is not - so a
fixture cannot drift towards the pattern and end up proving the pattern against
itself. Every other file is written rather than landed, and is here for a shape
the landed ones do not carry.

The proof runs in the workflow rather than being described here:

    bash .github/scripts/prove-the-negated-closing-keyword-refusal-bites.sh

It needs the full history, because of the excerpt comparison above. In a shallow
clone that leg fails and says so rather than passing quietly.

## negated-closing-keyword

`.github/scripts/refuse-a-negated-closing-keyword.sh`, for #308. A tracker reads
`close #81` and closes 81 whatever stands in front of it, so a sentence written
to say an issue is NOT being finished finishes it. The script's own header
carries what the shape is, what was measured to choose it, and what it cannot
see.

One of the near misses is the repair somebody wrote by hand after the third
instance: the keyword ends a heading and the number opens the paragraph under
it, which is two blocks and is read by neither the tracker nor this check.
