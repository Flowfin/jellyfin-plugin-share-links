# Parity ledger

One row for every workflow file on the default branch of
`iderex/jellyfin-plugin-sso`, which is the gate this repository is being brought
level with. The list is taken from a listing rather than from memory:

    gh api repos/iderex/jellyfin-plugin-sso/contents/.github/workflows --jq '.[].name'

Every row reads adopted or declined. Deferred counts as declined for now and
names the milestone that revisits it. A row that reads adopted names either the
issue that lands it or the file in this repository that already has.

Deferred is the weaker of the two and it is worth saying why the beta rows no
longer carry it. A row deferred to a milestone is one somebody comes back to when
that milestone opens; a row declined is one a decision closed. The three beta rows
were deferred because nothing was published yet, and the decision recorded on #89
on 2026-08-11 answered the question underneath them rather than the timing: what
is released is stable, and there is no public prerelease channel. Leaving them
deferred would send whoever opens M11 to build a channel this project decided
against.

Re-run the listing before trusting this file. A row cannot appear here for a
workflow that was added over there afterwards, and nothing in this repository
notices that.

| Workflow                    | Verdict              | Why, and where                                                                                                                                                                                                                                                                                                                                        |
| --------------------------- | -------------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `build.yml`                 | Adopted in substance | Its only trigger is `on: workflow_call`, so it is a reusable workflow the others call rather than a check of its own. What matters is that something builds, and `ci.yaml` does.                                                                                                                                                                      |
| `codeql.yml`                | Adopted, landed      | `.github/workflows/codeql.yml`, by #14                                                                                                                                                                                                                                                                                                                |
| `dco.yml`                   | Adopted, landed      | `.github/workflows/dco.yml`                                                                                                                                                                                                                                                                                                                           |
| `dependency-review.yml`     | Adopted, landed      | `.github/workflows/dependency-review.yml`                                                                                                                                                                                                                                                                                                             |
| `dotnet.yml`                | Adopted, landed      | `.github/workflows/ci.yaml`, by #12                                                                                                                                                                                                                                                                                                                   |
| `e2e-login.yml`             | Declined             | There is no interactive login flow of the plugin's own to drive, and the guest path is covered by seam level tests instead.                                                                                                                                                                                                                           |
| `fuzz.yml`                  | Adopted              | #19. The token in a share link is the one attacker-controlled input worth fuzzing. This row said it did not exist yet; `ShareLinksGuestController` takes it as a path segment and `ShareResolution.Resolve` is the target #19 names. No fuzz job is in `.github/workflows`.                                                                           |
| `manifest-freshness.yml`    | Deferred to M11      | There is nothing published to keep fresh until the release milestone.                                                                                                                                                                                                                                                                                 |
| `nightly-betas.yml`         | Declined             | There is no public prerelease channel for this plugin, decided on #89 on 2026-08-11. A nightly prerelease has nowhere to go.                                                                                                                                                                                                                          |
| `opengrep.yml`              | Adopted, landed      | `.github/workflows/invariants.yml`. #16 is open at this commit and holds what is left of the seeding, so the file existing is not the issue being done.                                                                                                                                                                                               |
| `prettier.yml`              | Adopted              | #17. Nothing here covers HTML, JavaScript, YAML, JSON or Markdown today.                                                                                                                                                                                                                                                                              |
| `pr-hygiene.yml`            | Adopted, landed      | `.github/workflows/pr-hygiene.yml`, by #15                                                                                                                                                                                                                                                                                                            |
| `publish.yml`               | Deferred to M11      | Nothing is published before that milestone.                                                                                                                                                                                                                                                                                                           |
| `publish-beta.yml`          | Declined             | Same decision. What is released is stable, so there is no second channel for this to publish to.                                                                                                                                                                                                                                                      |
| `publish-failure-alert.yml` | Adopted, landed      | The `alert` job in `.github/workflows/publish.yaml`, by #91. Not a file of its own: the separate workflow that shape wants triggers on `workflow_run`, which the zizmor gate here refuses at high severity. It has never fired, so what it does is designed rather than observed, and the clause of #91 asking that it be seen to fire is still owed. |
| `publish-jf12-beta.yml`     | Declined             | Same decision. The 12.0 line is carried, per the answer to decision 1 in #94, and it is carried on the stable channel: what this row was deferred for was the beta half, and that half is decided against.                                                                                                                                            |
| `publish-jf12-stable.yml`   | Deferred to M11      | Same reason.                                                                                                                                                                                                                                                                                                                                          |
| `regenerate-manifest.yml`   | Deferred to M11      | There is no manifest to regenerate yet.                                                                                                                                                                                                                                                                                                               |
| `scorecard.yml`             | Adopted, landed      | `.github/workflows/scorecard.yml`                                                                                                                                                                                                                                                                                                                     |
| `stryker-mutation.yml`      | Adopted, landed      | `.github/workflows/mutation.yml`, by #20. Weekly and on demand rather than per pull request, and the score it breaks on is the one measured at adoption.                                                                                                                                                                                              |
| `unicode-guard.yml`         | Adopted, landed      | `.github/workflows/unicode-guard.yml`                                                                                                                                                                                                                                                                                                                 |
| `wiki-lint.yml`             | Declined             | This repository has no wiki, and its documentation lives in the tree where the ordinary checks already see it.                                                                                                                                                                                                                                        |
| `zizmor.yml`                | Adopted, landed      | `.github/workflows/zizmor.yml`                                                                                                                                                                                                                                                                                                                        |

## What this ledger is not

It is not a list of the workflows this repository runs. Several files here have no
row above because they came from the plugin template rather than from the gate:
`command-dispatch.yaml`, `command-rebase.yaml`, `sync-labels.yaml`,
`publish.yaml`, `build.yaml` and `test.yaml`. Whether those stay is not a parity
question and is not decided here.

`package.yaml` and `headless.yml` have no row for a different reason. Both are
first-party, from #18 and #74, and no file on the listing above corresponds to
either. Between the template list and these two, every workflow file in this
repository at this commit is either in a row or accounted for here:

    ls .github/workflows

It also does not say a row marked adopted has landed unless the row names the
file. Adopted with an issue number means owed, not done.

Nothing refuses a stale row. This is a document, and the check that would compare
it against the listing does not exist.
