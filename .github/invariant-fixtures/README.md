# Invariant fixtures

Each directory here is named after one invariant in
`.github/scripts/enforce-greppable-invariants.sh` and holds two C# files that no
project compiles.

`violation/` breaks that one invariant and nothing else. `near-miss/` carries the
same names and the same words with the correct code, and has to stay green.

The `invariants` workflow runs three legs over every directory: the violation is
refused, and refused by that invariant alone; the same bytes are clean once that
invariant is removed; and the near miss is accepted. It also refuses an invariant
that has no directory here, because an invariant nothing proved would otherwise
be skipped in silence.

An invariant that refuses for more than one reason splits `violation/` into one
subdirectory per reason, and the workflow runs a fourth leg over each of them: on
its own, that subdirectory has to be refused by this invariant and by no other,
and to go clean when the invariant is removed. Without it the first three legs
pass on the whole directory while one reason quietly stops refusing anything,
because the other reasons keep the fixture red and nothing says which one did it.

A fixture is worth what its near miss is worth. A violation that could not have
been missed proves less than one somebody would actually write, and a near miss
that is nothing like the violation proves nothing at all.

One of these files cannot spell the thing its invariant refuses. Writing the type
name out in a comment, to explain the rule, reddened the file: the checker refused
its own documentation. The invariant is over the text of the tree, and a comment
is text.
