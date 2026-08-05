# Parity ledger

One row for every workflow file on the default branch of
`iderex/jellyfin-plugin-sso`, which is the gate this repository is being brought
level with. The list is taken from a listing rather than from memory:

    gh api repos/iderex/jellyfin-plugin-sso/contents/.github/workflows --jq '.[].name'

Every row reads adopted or declined. Deferred counts as declined for now and
names the milestone that revisits it. A row that reads adopted names either the
issue that lands it or the file in this repository that already has.

Re-run the listing before trusting this file. A row cannot appear here for a
workflow that was added over there afterwards, and nothing in this repository
notices that.

| Workflow | Verdict | Why, and where |
| --- | --- | --- |
| `build.yml` | Adopted in substance | Its only trigger is `on: workflow_call`, so it is a reusable workflow the others call rather than a check of its own. What matters is that something builds, and `ci.yaml` does. |
| `codeql.yml` | Adopted, landed | `.github/workflows/codeql.yml`, by #14 |
| `dco.yml` | Adopted, landed | `.github/workflows/dco.yml` |
| `dependency-review.yml` | Adopted, landed | `.github/workflows/dependency-review.yml` |
| `dotnet.yml` | Adopted, landed | `.github/workflows/ci.yaml`, by #12 |
| `e2e-login.yml` | Declined | There is no interactive login flow of the plugin's own to drive, and the guest path is covered by seam level tests instead. |
| `fuzz.yml` | Adopted | #19. The token in a share link is the one attacker-controlled input worth fuzzing, and it does not exist yet. |
| `manifest-freshness.yml` | Deferred to M11 | There is nothing published to keep fresh until the release milestone. |
| `nightly-betas.yml` | Deferred to M11 | Same reason. |
| `opengrep.yml` | Adopted | #16, which ports the invariant lint and seeds it with this plugin's invariants. |
| `prettier.yml` | Adopted | #17. Nothing here covers HTML, JavaScript, YAML, JSON or Markdown today. |
| `pr-hygiene.yml` | Adopted | #15 |
| `publish.yml` | Deferred to M11 | Nothing is published before that milestone. |
| `publish-beta.yml` | Deferred to M11 | Same reason. |
| `publish-failure-alert.yml` | Deferred to M11 | Nothing to alert about until something publishes. |
| `publish-jf12-beta.yml` | Deferred to M11 | Same reason. The 12.0 line is carried, per the answer to decision 1 in #94, so this row is deferred rather than declined. |
| `publish-jf12-stable.yml` | Deferred to M11 | Same reason. |
| `regenerate-manifest.yml` | Deferred to M11 | There is no manifest to regenerate yet. |
| `scorecard.yml` | Adopted, landed | `.github/workflows/scorecard.yml` |
| `stryker-mutation.yml` | Adopted | #20, which measures the suite against the authorization code once there is authorization code. |
| `unicode-guard.yml` | Adopted, landed | `.github/workflows/unicode-guard.yml` |
| `wiki-lint.yml` | Declined | This repository has no wiki, and its documentation lives in the tree where the ordinary checks already see it. |
| `zizmor.yml` | Adopted, landed | `.github/workflows/zizmor.yml` |

## What this ledger is not

It is not a list of the workflows this repository runs. Several files here have no
row above because they came from the plugin template rather than from the gate:
`command-dispatch.yaml`, `command-rebase.yaml`, `sync-labels.yaml`,
`publish.yaml`, `build.yaml` and `test.yaml`. Whether those stay is not a parity
question and is not decided here.

`package.yaml` has no row for a different reason. It is first-party, from #18, and
no file on the listing above corresponds to it.

It also does not say a row marked adopted has landed unless the row names the
file. Adopted with an issue number means owed, not done.

Nothing refuses a stale row. This is a document, and the check that would compare
it against the listing does not exist.
