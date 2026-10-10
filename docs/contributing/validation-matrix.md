# Contributor validation matrix

Use this matrix to choose focused checks while iterating. It summarizes existing guidance; it does not replace either repository's merge gates. Record the exact commit revision, checks as passed, failed, or not run, and links to evidence for that revision. For a paired change, identify both repository revisions. You supply the checks you run; GitHub Actions supplies only its configured jobs. If required infrastructure is unavailable, mark the evidence not run and use the issue or pull request to agree who will supply it before merge. Do not assume an unconfigured hosted job or reviewer commitment.

## Existing merge gates

- **Foundation:** the [Merge Gate](../skills/catalog.md#merge-gate) still requires the solution build, affected whole test projects, architecture guard, generated-maps check, diff review, green required checks on the PR head, and a PR evidence comment. A behavioral claim also requires the gate's red-then-green bite-proof. Follow the applicable [rebuilt-server E2E guidance](../../AGENTS.md#backend-e2e-tests).
- **Studio:** use the [Studio contribution guide](https://github.com/elsa-workflows/elsa-foundation-studio/blob/main/CONTRIBUTING.md#local-checks-and-hosted-checks) and current PR branch rules. Its CI and Docker PR jobs are described there; the Packages workflow does not run on pull requests.

## By change area

### Documentation

- **Inner loop:** review the rendered Markdown and links; run `git diff --check`. Follow any task-specific documentation check.
- **Evidence:** report the relevant local review and let the configured repository PR checks run. A docs-only change does not make a behavioral bite-proof applicable, and it does not waive the existing merge gates.

### Backend behavior

- **Inner loop:** build and test the affected project or an appropriate [solution filter](../reference/developer-solution-filters.md). For Workbench startup and its REST smoke test, use the [backend source quickstart](backend-source-quickstart.md); use the [E2E guide](../../e2e-tests/README.md) to select other suites and their process-ownership rules.
- **Evidence:** Foundation PR CI builds the full solution and runs its configured test jobs, including selected EF suites. When changing design, publishing, runtime, activity, stimulus, or scheduling behavior, run the relevant E2E suite against a rebuilt server as described in [AGENTS.md](../../AGENTS.md#backend-e2e-tests). Record the affected suites and the required bite-proof; Actions only supplies jobs already configured for the PR.

### Studio UI

- **Inner loop:** use the Node.js and pinned pnpm versions in the Studio contribution guide. Run `pnpm install --frozen-lockfile`, then the affected package's existing test, typecheck, or build script; for example, `pnpm --filter @elsa-workflows/studio-workflows test`. See the [Studio guide](https://github.com/elsa-workflows/elsa-foundation-studio/blob/main/CONTRIBUTING.md#local-checks-and-hosted-checks) for root checks and its linked [CI workflow](https://github.com/elsa-workflows/elsa-foundation-studio/blob/main/.github/workflows/ci.yml) for browser-test setup.
- **Evidence:** Studio PR CI runs lint, typecheck, workspace tests, shuffled Workflows tests, bundle-budget checks, and Chromium tests. Its Docker PR job builds the image without publishing it. For a visible paired source change, use the [Studio source quickstart](https://github.com/elsa-workflows/elsa-foundation-studio/blob/main/docs/contributing/studio-source-quickstart.md) and record the exact pair and browser result when the claim depends on both repositories.

### Persistence

- **Inner loop:** run the affected provider test project as a whole. The [solution-filter guide](../reference/developer-solution-filters.md) identifies the Testcontainers-backed profile; those tests need Docker. Use the E2E guide for persistence behavior exercised through a rebuilt Workbench.
- **Evidence:** Foundation PR CI selects affected EF suites and runs the Secrets/PostgreSQL composition job. The broader Testcontainers integration workflow is nightly or manually dispatched, not a required PR check. Do not use its existence as evidence for an affected suite that did not run; identify and coordinate any additional required infrastructure-dependent check.

### Cross-repository change

- **Inner loop:** run the relevant checks in each changed repository. When the behavior depends on a Studio/Workbench pair, follow both source quickstarts and record both exact revisions.
- **Evidence:** each PR's CI checks its own repository head; there is no general paired-branch CI job. Provide an exact-pair result for a claim that depends on the pair. Two independent green CI runs do not prove that combined behavior.
