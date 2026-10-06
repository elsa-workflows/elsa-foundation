# Contributing to Elsa Foundation Studio

Elsa Foundation Studio is the modular React studio shell hosted by ASP.NET Core. This repository owns the Studio shell, modules, and frontend SDK; the [README](README.md) describes the architecture and current developer commands.

## Choose and align work

Actionable Studio work is tracked in [GitHub Issues](https://github.com/elsa-workflows/elsa-foundation-studio/issues). Check an issue's comments and open pull requests before starting; an unassigned issue is not necessarily available. If ownership or scope is unclear, ask in the issue.

A small, concrete fix may be proposed directly in a pull request. For a substantial feature, describe the desired behavior in a Studio issue first and align its scope there; a pull request is not a feature-request substitute. General questions belong in the shared [Foundation Q&A](https://github.com/elsa-workflows/elsa-foundation/discussions/categories/q-a). Keep actionable Studio work in Studio Issues.

AI tools are optional. Their use does not change the applicable review or quality expectations.

## Plans and artifacts

For Spec Kit feature work, follow the feature directory identified by the issue, ticket, or branch, as described in [AGENTS.md](AGENTS.md#project-conventions). Read its existing plan and the related spec or task artifacts when relevant. The [spec](.specify/templates/spec-template.md), [plan](.specify/templates/plan-template.md), and [task](.specify/templates/tasks-template.md) templates are available when aligned work needs new artifacts. A small fix does not need a new feature document set by default.

## Local checks and hosted checks

For frontend work, use Node.js 22 and the pnpm version declared in [`package.json`](package.json) (`pnpm@11.9.0`), matching the current PR workflow. Install the workspace with:

```bash
pnpm install --frozen-lockfile
```

Run the affected workspace's existing test, typecheck, or build script as appropriate. For example, `pnpm --filter @elsa-workflows/studio-workflows test` runs that package's test script; use the package name and scripts from its `package.json` for other modules. The root scripts in [`package.json`](package.json) provide broader checks such as `pnpm lint`, `pnpm typecheck`, `pnpm test`, `pnpm test:browser`, and `pnpm check:bundle:workflows`.

The repository-defined hosted PR jobs are in [CI](.github/workflows/ci.yml) and [Docker Image](.github/workflows/docker.yml). CI runs lint, typecheck, workspace unit tests, shuffled Workflows tests, the Workflows bundle budget, and Chromium browser tests. The Docker workflow builds the Studio image on pull requests. The [Packages workflow](.github/workflows/packages.yml) uses .NET 10 to restore, build, test, and pack on selected branch pushes and releases; it does not run on pull requests. Check the live pull request and repository branch rules for its exact checks and requirements.

### Current .NET setup limitation

The current `main` still pins `ConsoleLogStreaming.AspNetCore` `1.0.0-preview.13` in [`Directory.Packages.props`](Directory.Packages.props). An earlier isolated-cache restore failed for that version ([Foundation #2432](https://github.com/elsa-workflows/elsa-foundation/issues/2432)). Its correction is owned by [Studio #551](https://github.com/elsa-workflows/elsa-foundation-studio/issues/551) / [PR #557](https://github.com/elsa-workflows/elsa-foundation-studio/pull/557), which remains unmerged; the complete source setup and paired workflow are tracked in [Studio #566](https://github.com/elsa-workflows/elsa-foundation-studio/issues/566). The existing `dotnet build`, `dotnet test`, and `dotnet run` commands in README are not a verified cold-cache setup path. Check those issues for current status before relying on a from-source backend run.

If GitHub displays a `license/cla` check on your pull request, follow the instructions in that check.

## Open a pull request from a fork

For an external contribution, use **Fork** on the [repository page](https://github.com/elsa-workflows/elsa-foundation-studio) to create a copy in your GitHub account. Clone your fork so `origin` points to your account. Replace `YOUR-ACCOUNT` with your GitHub account name before running the clone command, then add the upstream repository and branch from its current `main`:

```bash
git clone https://github.com/YOUR-ACCOUNT/elsa-foundation-studio.git
cd elsa-foundation-studio
git remote add upstream https://github.com/elsa-workflows/elsa-foundation-studio.git
git fetch upstream
git switch -c docs/my-change upstream/main
```

Choose a branch name that describes your change; `docs/my-change` is an example. Make the change, run the relevant checks, and stage only the paths you changed (replace the example path below) before committing:

```bash
git add path/to/your-change
git commit -m "Describe the change"
git push -u origin docs/my-change
```

On GitHub, open a **draft** pull request from `YOUR-ACCOUNT:docs/my-change` to `elsa-workflows/elsa-foundation-studio:main`. Include a short summary, link the issue when applicable, and report the checks you ran. Mark the draft ready when the change is reviewable. Respond to review requests and rerun and report affected checks after changes.

## License

Elsa-owned material in this repository is licensed under the [MIT License](LICENSE). Third-party code, assets, and dependencies retain their own license terms and notices.
