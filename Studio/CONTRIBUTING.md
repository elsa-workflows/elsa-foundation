# Contributing to Elsa Foundation Studio

Elsa Foundation Studio is the modular React studio shell hosted by ASP.NET Core. This repository owns the Studio shell, modules, and frontend SDK; the [README](README.md) describes the architecture and current developer commands.

## Choose and align work

Current actionable Studio work is tracked in [Elsa Foundation GitHub Issues](https://github.com/elsa-workflows/elsa-foundation/issues). Before starting, check the issue's comments and open pull requests, then follow Foundation's [issue-tracker guide](../docs/agents/issue-tracker.md) and [concurrent-work claim rules](../AGENTS.md#concurrent-work-claims). [Foundation #2501](https://github.com/elsa-workflows/elsa-foundation/issues/2501) records the consolidation. Existing Studio issue links below are historical references; this does not reparent old issues.

A small, concrete fix may be proposed directly in a pull request. For a substantial feature, describe the desired behavior in a Foundation issue first and align its scope there; a pull request is not a feature-request substitute. General questions belong in the shared [Foundation Q&A](https://github.com/elsa-workflows/elsa-foundation/discussions/categories/q-a).

To report a suspected security vulnerability, use the [private security reporting guidance](SECURITY.md).

For conduct expectations and reporting, see the [Code of Conduct](CODE_OF_CONDUCT.md).

AI tools are optional. Their use does not change the applicable review or quality expectations.

## Plans and artifacts

For Spec Kit feature work, follow the feature directory identified by the issue, ticket, or branch, as described in [AGENTS.md](AGENTS.md#project-conventions). Read its existing plan and the related spec or task artifacts when relevant. The [spec](.specify/templates/spec-template.md), [plan](.specify/templates/plan-template.md), and [task](.specify/templates/tasks-template.md) templates are available when aligned work needs new artifacts. A small fix does not need a new feature document set by default.

## Local checks and hosted checks

For frontend work, use Node.js 22 and the pnpm version declared in [`package.json`](package.json) (`pnpm@11.9.0`), matching the current PR workflow. Run workspace commands from `Studio/` (or use `pnpm --dir Studio` from the repository root). Install the workspace with:

```bash
pnpm install --frozen-lockfile
```

For a paired source-built Studio and Workbench walkthrough, see [Make a visible Studio source edit](docs/contributing/studio-source-quickstart.md). Its evidence section describes the tested source-edit and browser path, toolchain, and explicit verification limits.

Run the affected workspace's existing test, typecheck, or build script as appropriate. For example, `pnpm --filter @elsa-workflows/studio-workflows test` runs that package's test script; use the package name and scripts from its `package.json` for other modules. The root scripts in [`package.json`](package.json) provide broader checks such as `pnpm lint`, `pnpm typecheck`, `pnpm test`, `pnpm test:browser`, and `pnpm check:bundle:workflows`.

For a change-by-change view of focused checks and evidence, see Foundation's [shared contributor validation matrix](https://github.com/elsa-workflows/elsa-foundation/blob/main/docs/contributing/validation-matrix.md).

The active hosted PR jobs are in Foundation's [Studio CI](../.github/workflows/studio-ci.yml). They run lint, typecheck, workspace unit tests, the Workflows bundle budget, Chromium browser tests, and .NET/package qualification, including a Studio image build without publishing. The three extra shuffled Workflows passes run in [Studio test order checks](../.github/workflows/studio-order-check.yml) nightly, on demand, and on PRs that change that workflow. Check the live pull request and repository branch rules for its exact checks and requirements.

The opt-in `tests/live/unified-execution-journey.spec.ts` qualifies both live views against normal Studio and Workbench hosts. Save an owned Sequence with Event followed by Fault, using the ordinary Event delivery editor and the normal capture/redaction policy. Set `ELSA_STUDIO_URL` and `ELSA_UNIFIED_EXECUTION_DEFINITION_ID`, then run `pnpm test:live unified-execution-journey.spec.ts --max-failures=1`. The journey starts two Test Runs, delivers harmless sample data, and signs out the configured test user to verify that both views withdraw inaccessible evidence. Use a dedicated test account/environment: signing out revokes that user's other sessions too. Existing live-test authentication environment variables are documented in `tests/live/support/journey.ts`. No publication or capture-policy changes are performed. Use Playwright's HTML reporter if you need to retain the attached screenshots and execution identities for passing tests.

### Current .NET setup verification

[`Directory.Packages.props`](Directory.Packages.props) now pins `ConsoleLogStreaming.AspNetCore` `1.1.0`. [Studio #579](https://github.com/elsa-workflows/elsa-foundation-studio/issues/579) records the correction of the earlier preview-package restore failure, with a Studio.Web restore using isolated package-cache, HTTP-cache, and CLI-home paths, a scoped host build, and affected console-stream/host tests. Installed SDK packs remained available during those checks.

The source-paired Studio/Workbench setup, visible edit, and browser workflow are recorded in the [Studio source quickstart](docs/contributing/studio-source-quickstart.md). Broader contributor acceptance remains tracked in [Studio #566](https://github.com/elsa-workflows/elsa-foundation-studio/issues/566), including the reference-backend option, mismatched Studio/Workbench versions, fresh-OS setup, and first-time contributor trials.

If GitHub displays a `license/cla` check on your pull request, follow the instructions in that check.

## Open a pull request from a fork

For an external contribution, use **Fork** on the [Elsa Foundation repository](https://github.com/elsa-workflows/elsa-foundation) to create a copy in your GitHub account. Clone your fork so `origin` points to your account. Replace `YOUR-ACCOUNT` with your GitHub account name before running the clone command, then add the upstream repository and branch from its current `main`:

```bash
git clone https://github.com/YOUR-ACCOUNT/elsa-foundation.git
cd elsa-foundation
git remote add upstream https://github.com/elsa-workflows/elsa-foundation.git
git fetch upstream
git switch -c docs/my-change upstream/main
```

Choose a branch name that describes your change; `docs/my-change` is an example. Make the change, run the relevant checks, and stage only the paths you changed (replace the example path below) before committing:

```bash
git add path/to/your-change
git commit -m "Describe the change"
git push -u origin docs/my-change
```

On GitHub, open a **draft** pull request from `YOUR-ACCOUNT:docs/my-change` to `elsa-workflows/elsa-foundation:main`. Include a short summary, link the Foundation issue when applicable, and report the checks you ran. Mark the draft ready when the change is reviewable. Respond to review requests and rerun and report affected checks after changes.

## License

Elsa-owned material in this repository is licensed under the [MIT License](LICENSE). Third-party code, assets, and dependencies retain their own license terms and notices.
