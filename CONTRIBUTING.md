# Contributing to Elsa Foundation

Elsa Foundation is the transitional Elsa 4 foundation workspace. Contributions are welcome, and you can work with any editor; AI tools and subscriptions are optional.

## Choose a starting point

- To run the backend from source on macOS, use the [backend source quickstart](docs/contributing/backend-source-quickstart.md). For a prebuilt Workbench and Studio stack, follow the [Docker quickstart](docs/docker-hub-quickstart.md).
- For a contributor question, use [Foundation Discussions Q&A](https://github.com/elsa-workflows/elsa-foundation/discussions/categories/q-a).
- To report an actionable bug or propose a feature, open a [Foundation issue](https://github.com/elsa-workflows/elsa-foundation/issues). Small fixes may arrive directly as pull requests; substantial feature requests should start with an issue so the scope can be aligned before implementation.

If you are unsure where to start, ask in Q&A and check open issues and pull requests for work without an existing claim. For issue-backed or split work, coordinate overlapping scope before editing; the [issue tracker guide](docs/agents/issue-tracker.md) explains how to check and record a claim.

## Plan feature work

For substantial feature work, align the scope in an issue before implementation. Feature work follows the repository's existing Spec Kit sequence: **specification → plan → tasks → implementation**. This is a human-readable artifact path; no AI tool is required. Review each artifact before moving to the next step:

1. Create `specs/NNN-feature-name/spec.md` from the [spec template](.specify/templates/spec-template.md). Describe user scenarios, acceptance criteria, and how each scenario will be verified. Follow the [spec lifecycle](docs/reference/spec-lifecycle.md) when choosing a number and status.
2. Create `plan.md` from the [plan template](.specify/templates/plan-template.md). Its Constitution Check is a gate before research and must be rechecked after design; resolve gate findings before implementation proceeds.
3. Create `tasks.md` from the [tasks template](.specify/templates/tasks-template.md), with concrete file paths, implementation work, and the tests required by the feature and repository gates. The template is a scaffold: its generic example text does not waive applicable tests or constitution requirements.
4. Review the spec, plan, and task list together, then implement the agreed tasks. Record validation in the spec's quickstart or other task-specific evidence and in the pull request.

The [Specs guide](specs/README.md) explains the artifact roles. Applicable quality rules remain in the [framework constitution](.specify/memory/constitution-framework.md) and [Elsa constitution](.specify/memory/constitution.md). Small fixes still follow the normal review and testing requirements; this guide creates no spec or test waiver.

## Work from a fork

If you do not have write access to the Elsa organization, fork this repository. The backend source quickstart clones the upstream repository, so `origin` points at Elsa. Keep it unchanged and check it with `git remote -v`. If your fork remote is not already configured, add it as a separate remote named `fork`:

```bash
git remote add fork https://github.com/YOUR-ACCOUNT/elsa-foundation.git
git fetch origin
git switch -c contributing/my-fix origin/main
```

Replace `YOUR-ACCOUNT` with your GitHub username. Make and validate your change on the new branch, then commit it and push it to your fork with `git push -u fork contributing/my-fix`. If you cloned your fork instead, push the topic branch to that fork's remote. Open a draft pull request from your fork branch against Elsa Foundation's `main`; do not push a contributor branch to the organization repository. Link the related issue when there is one, describe the user-visible change, and list the checks you ran and any relevant checks you could not run.

Maintainers working in the organization repository use a feature/work-unit branch and follow [the repository's claim guidance](AGENTS.md#concurrent-work-claims) for issue-backed or split work.

The repository is [MIT licensed](LICENSE). Pull requests may show the existing `license/cla` check from [Microsoft GitHub Policy Service](https://github.com/apps/microsoft-github-policy-service); follow the check's status and any instructions it provides. This guide does not add a separate signing process or infer an exemption.

## Validate and record the change

Use the narrowest build and test suites that cover your change while iterating. A feature's `specs/<feature>/quickstart.md`, the [developer solution filters](docs/reference/developer-solution-filters.md), and the [backend E2E guide](e2e-tests/README.md) point to task-specific validation. Record what you ran; if a check is unavailable, say it was not run.

Focused checks do not replace the repository's merge gate. See the [Merge Gate](docs/skills/catalog.md#merge-gate) for the required build, affected suites, architecture and maps checks, diff review, and hosted-check evidence; behavioral changes also need the bite-proof described there. For orientation beyond the task at hand, see the [architecture tour](docs/architecture-tour.md).

## After opening a pull request

When the change is ready for review, mark the draft PR ready. Respond to review requests, update the branch, and rerun and report checks affected by the changes. Keep the PR in draft while it is still in progress. A maintainer can merge only after the required review and gates pass; do not merge past a red or missing required check.
