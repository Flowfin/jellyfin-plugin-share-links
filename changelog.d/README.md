# Release-note fragments

A published release carries notes written for the operator installing it. They
are assembled from the files in this directory by
`.github/scripts/assemble-release-notes.sh`, which the publish route runs before
it creates the release.

## Why this is not the commit list

The forge will build notes from the commits between two tags, and that is what
this repository used to publish. A commit list is a record of the work: it
answers what landed, in the words the people who landed it used. The notes an
operator reads answer a different question - what is different on their server
after they upgrade, and whether anything is expected of them. Those are two
documents, so they have two sources, and only one of them is switched on here.
Decided on #89 on 2026-08-11 and again on 2026-08-24.

## What a fragment looks like

One file per thing worth telling an operator, named `<issue>.<kind>.md`:

```
changelog.d/136.added.md
```

`<issue>` is the issue the change belongs to, and it is printed after the entry
so a reader can follow it. `<kind>` is one of `security`, `added`, `changed`, `fixed`, `removed`. That is
the order the headings appear in as well.

The body is prose for somebody who has not read this repository. Write what they
will notice and what they have to do, not what the diff did:

```
An expired share now disappears from the list on the configuration page instead
of staying there greyed out.
```

Line breaks inside a fragment are yours to write for the diff; the assembler
joins them into one entry, so a paragraph wrapped over four lines is one bullet.

## What happens at release

`docs/RELEASING.md` is the process. The fragments for a version are deleted in
the same change that raises `version` in `build.yaml`, so the directory holds
what is unreleased and nothing else, and the notes for a version that shipped
live in that release rather than here.

## What refuses a mistake

The assembler refuses a fragment whose name carries no issue or an unknown kind,
refuses an empty one, and refuses a release with no fragments at all rather than
publishing a version whose notes say nothing. `ReleaseNotesTests` makes the same
two name checks on every run of the suite, so a bad fragment reds a pull request
rather than a tag, and compares the kinds this page lists against the kinds the
assembler accepts, because a heading documented in one place and accepted in the
other is the drift that puts an entry nowhere.
