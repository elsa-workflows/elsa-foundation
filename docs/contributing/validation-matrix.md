# Contributor validation matrix

Use this matrix to choose focused checks while iterating. It summarizes existing guidance; it does not replace either repository's merge gates. Record the exact commit revision, checks as passed, failed, or not run, and links to evidence for that revision. For a paired change, identify both repository revisions. You supply the checks you run; GitHub Actions supplies only its configured jobs. If required infrastructure is unavailable, mark the evidence not run and use the issue or pull request to agree who will supply it before merge. Do not assume an unconfigured hosted job or reviewer commitment.

## Existing merge gates

- **Foundation:** the [Merge Gate](../skills/catalog.md#merge-gate) still requires the solution build, affected whole test projects, architecture guard, generated-maps check (the committed solution filters), diff review, a green `Build & test` check on the PR head, and a PR evidence comment. A behavioral claim also requires the gate's red-then-green bite-proof. Follow the applicable [rebuilt-server E2E guidance](task-paths.md#backend-e2e-tests).
- **Studio:** use the [Studio contribution guide](../../Studio/CONTRIBUTING.md#local-checks-and-hosted-checks) and current PR branch rules. Its CI jobs are described there; the Packages workflow does not run on pull requests.

## By change area

### Documentation

- **Inner loop:** review the rendered Markdown and links; run `git diff --check`. Follow any task-specific documentation check.
- **Evidence:** report the relevant local review and let the configured repository PR checks run. On a docs-only PR, `Build & test` takes a fast path (Python guards and the catalog link check, no .NET build); see [Merging](merging.md#pr-evidence) for which paths count. A docs-only change does not make a behavioral bite-proof applicable, and it does not waive the existing merge gates.

### Backend behavior

- **Inner loop:** build and test the affected project or an appropriate [solution filter](../reference/developer-solution-filters.md). For Workbench startup and its REST smoke test, use the [backend source quickstart](backend-source-quickstart.md); use the [E2E guide](../../e2e-tests/README.md) to select other suites and their process-ownership rules.
- **Evidence:** Foundation PR CI builds the full solution and runs its configured test jobs, including selected EF suites. When changing design, publishing, runtime, activity, stimulus, or scheduling behavior, run the relevant E2E suite against a rebuilt server as described in [Backend e2e tests](task-paths.md#backend-e2e-tests). Record the affected suites and the required bite-proof; Actions only supplies jobs already configured for the PR.

### Studio UI

- **Inner loop:** use the Node.js and pinned pnpm versions in the Studio contribution guide. Run `pnpm install --frozen-lockfile` from `Studio/`, then the affected package's existing test, typecheck, or build script; for example, `pnpm --filter @elsa-workflows/studio-workflows test`. See the [Studio guide](../../Studio/CONTRIBUTING.md#local-checks-and-hosted-checks) for root checks and Foundation [CI](../../.github/workflows/ci.yml) for browser-test setup.
- **Evidence:** Foundation PR CI runs Studio lint, typecheck, workspace tests and bundle-budget checks when Studio changed, inside `Build & test`. The Chromium browser tests, Studio .NET build and composed UX acceptance run in `CI`'s nightly schedule and on `gh workflow run ci.yml --ref <branch> -f suite=studio`; dispatch them before merging a change that needs browser proof. The three extra shuffled Workflows test passes run nightly and on demand (`-f suite=studio-order`). For a visible paired source change, use the [Studio source quickstart](../../Studio/docs/contributing/studio-source-quickstart.md) and record the exact checkout revision and browser result.

### Persistence

- **Inner loop:** run the affected provider test project as a whole. The [solution-filter guide](../reference/developer-solution-filters.md) identifies the Testcontainers-backed profile; those tests need Docker. Use the E2E guide for persistence behavior exercised through a rebuilt Workbench.
- **Evidence:** Foundation PR CI runs no Testcontainers suite. The EF container matrix and the Secrets/PostgreSQL composition job run in `CI`'s nightly schedule and on manual dispatch only (#2632), not as a required PR check. Run the affected provider suite locally, or dispatch `CI` on the branch, and record the result; do not use the nightly's existence as evidence for an affected suite that did not run.

### Kernel prototype

- **Inner loop:** build and run the standalone kernel model under `prototypes/kernel-model` as its README describes; it has its own projects and no server consumer.
- **Evidence:** `Kernel prototype gate` (workflow `CI`, not a required check) runs on a non-draft PR that changes `prototypes/kernel-model/**`, `tools/ci/kernel_prototype_gate.py` or `.github/workflows/ci.yml`, and reports as skipped otherwise. The larger sizes run in the `Kernel prototype nightly` jobs of `CI`'s nightly schedule; dispatch `CI` with `suite=kernel-gate` or `suite=kernel-nightly` to run either on a branch.

### Cross-repository change

- **Inner loop:** run the relevant checks in each changed repository. When the behavior depends on a Studio/Workbench pair, follow both source quickstarts and record both exact revisions.
- **Evidence:** each PR's CI checks its own repository head; there is no general paired-branch CI job. Provide an exact-pair result for a claim that depends on the pair. Two independent green CI runs do not prove that combined behavior.
