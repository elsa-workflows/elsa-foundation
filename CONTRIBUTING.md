# Contributing to Elsa Foundation

Elsa Foundation is the transitional Elsa 4 foundation workspace. Contributions are welcome, and you can work with any editor; AI tools and subscriptions are optional.

## Choose a starting point

- To run the backend from source on macOS, use the [backend source quickstart](docs/contributing/backend-source-quickstart.md). For a prebuilt Workbench and Studio stack, follow the [Docker quickstart](docs/docker-hub-quickstart.md).
- To report a suspected security vulnerability, use the [private security reporting guidance](SECURITY.md).
- For conduct expectations and reporting, see the [Code of Conduct](CODE_OF_CONDUCT.md).
- For a contributor question, use [Foundation Discussions Q&A](https://github.com/elsa-workflows/elsa-foundation/discussions/categories/q-a).
- To report an actionable bug or propose a feature, open a [Foundation issue](https://github.com/elsa-workflows/elsa-foundation/issues). Small fixes may arrive directly as pull requests; substantial feature requests should start with an issue so the scope can be aligned before implementation.

If you are unsure where to start, ask in Q&A and check open issues and pull requests for work without an existing claim. For issue-backed or split work, coordinate overlapping scope before editing; the [issue tracker guide](docs/agents/issue-tracker.md) explains how to check and record a claim.

## Create a topic branch

If you do not have write access to the Elsa organization, fork this repository on GitHub. The backend source quickstart clones the upstream repository, so `origin` points at Elsa. Keep it unchanged and check your remotes with `git remote -v`. Replace `YOUR-ACCOUNT` below with your GitHub username. Run the first command only if your fork is not already configured as a remote:

```bash
git remote add fork https://github.com/YOUR-ACCOUNT/elsa-foundation.git
git fetch origin
git switch -c contributing/my-fix origin/main
```

If your fork remote has another name, use that name when pushing later. If you cloned your fork instead of following the quickstart, create your topic branch from an up-to-date copy of Elsa Foundation's `main` and push it to your fork's remote.

Maintainers, including Sipke, always publish feature/work-unit branches in the organization repository and open PRs from those branches. Maintainer work never uses fork PRs. Contributors without organization write access use the fork workflow above. These defaults also apply when using agents; no local preference setup or workflow selection is required before publication. Follow [the repository's claim guidance](AGENTS.md#concurrent-work-claims) for issue-backed or split work.

## Plan feature work

For substantial feature work, align the scope in an issue before implementation. Feature work follows the repository's existing Spec Kit sequence: **specification → plan → tasks → implementation**. This is a human-readable artifact path; no AI tool is required. Review each artifact before moving to the next step:

1. Create `specs/NNN-feature-name/spec.md` from the [spec template](.specify/templates/spec-template.md). Describe user scenarios, acceptance criteria, and how each scenario will be verified. Follow the [spec lifecycle](docs/reference/spec-lifecycle.md) when choosing a number and status.
2. Create `plan.md` from the [plan template](.specify/templates/plan-template.md). Its Constitution Check is a gate before research and must be rechecked after design; resolve gate findings before implementation proceeds.
3. Create `tasks.md` from the [tasks template](.specify/templates/tasks-template.md), with concrete file paths, implementation work, and the tests required by the feature and repository gates. The template is a scaffold: its generic example text does not waive applicable tests or constitution requirements.
4. Review the spec, plan, and task list together, then implement the agreed tasks. Record validation in the spec's quickstart or other task-specific evidence and in the pull request.

The [Specs guide](specs/README.md) explains the artifact roles. Applicable quality rules remain in the [framework constitution](.specify/memory/constitution-framework.md) and [Elsa constitution](.specify/memory/constitution.md). Small fixes still follow the applicable specification, review, and testing requirements.

## Validate and record the change

Use the narrowest build and test suites that cover your change while iterating. A feature's `specs/<feature>/quickstart.md`, the [developer solution filters](docs/reference/developer-solution-filters.md), and the [backend E2E guide](e2e-tests/README.md) point to task-specific validation. Record what you ran; if a check is unavailable, say it was not run.

For a change-by-change view of focused checks and evidence, see the [contributor validation matrix](docs/contributing/validation-matrix.md).

Focused checks do not replace the repository's merge gate. See the [Merge Gate](docs/skills/catalog.md#merge-gate) for the required build, affected suites, architecture and maps checks, diff review, and hosted-check evidence; behavioral changes also need the bite-proof described there. For orientation beyond the task at hand, see the [architecture tour](docs/architecture-tour.md).

## Open a pull request

After making and validating the change, review your diff and commit the intended files. For contributor fork work, push the topic branch with `git push -u fork contributing/my-fix`, substituting your fork remote and branch names if they differ. For maintainer work, push the topic branch to the organization remote instead. Open a draft pull request from that branch against Elsa Foundation's `main`. Link the related issue when there is one, describe the user-visible change, and list the checks you ran and any relevant checks you could not run.

The repository is [MIT licensed](LICENSE). Pull requests may show the existing `license/cla` check from [Microsoft GitHub Policy Service](https://github.com/apps/microsoft-github-policy-service); follow the check's status and any instructions it provides. This guide does not add a separate signing process or infer an exemption.

When the change is ready for review, mark the draft PR ready. Respond to review requests, update the branch, and rerun and report checks affected by the changes. Keep the PR in draft while it is still in progress. A maintainer can merge only after the required review and gates pass; do not merge past a red or missing required check.

Branches and pull requests in the organization repository have a lifecycle: a branch without an open PR is archived and deleted by the weekly sweep when its work is already in `main` or it has had no commit for 14 days, and a PR without activity for 21 days is marked stale and then closed. See [Repository hygiene](docs/contributing/repository-hygiene.md).
