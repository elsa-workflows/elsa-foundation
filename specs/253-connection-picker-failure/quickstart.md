# Quickstart: Connection picker load-failure explanation

This guide lists the bounded checks and delivery gates for implementation. These commands have not been run during specification preparation.

## Prerequisites

- The Studio JavaScript dependencies are installed.
- The Connections client package and Studio Playwright browser dependencies are available.
- The .NET SDK and restored architecture-guard graph are available for the applicable guard and map checks.

## Focused component regression (T004)

The Connections package uses Vitest. Run the new direct picker regression from the repository root:

```bash
pnpm --dir Extensions/Connections/studio/Elsa.Studio.Connections/Client exec vitest run src/__tests__/connectionPickerEditor.test.tsx
```

Expected coverage: a rejected metadata load shows generic accessible guidance and keeps selection disabled; Retry exposes loading; a succeeding retry restores choices without replacing the stored reference. This is new direct component coverage; the current Connections client suite does not render the picker directly.

## Affected package and canonical Studio checks (T005-T006)

Run the complete Connections client suite and build through its package scripts:

```bash
pnpm --dir Extensions/Connections/studio/Elsa.Studio.Connections/Client test
pnpm --dir Extensions/Connections/studio/Elsa.Studio.Connections/Client build
```

Then run the canonical Studio workspace typecheck and lint:

```bash
pnpm --dir Studio typecheck
pnpm --dir Studio lint
```

## Browser accessibility and layout proof (T007)

Reuse `Studio/tests/browser/connection-syntax-layout.spec.ts`; it is the existing browser layout seam, not a pre-existing direct picker component test. Run its metadata-only journey at one worker:

```bash
pnpm --dir Studio exec playwright test tests/browser/connection-syntax-layout.spec.ts --workers=1
```

Expected coverage: failure guidance is exposed accessibly, keyboard users can retry and recover, and the layout works at narrow and desktop widths in light and dark themes. Fixtures contain Connection descriptions only and make no request for a stored secret value.

## Architecture and generated maps (T008)

Root runs the complete architecture guard suite serially and reconciles discovered, executed, passed, failed, and skipped counts. Run the authoritative freshness check as well; do not run a full-solution local build for this UI change:

```bash
bash tools/architecture/restore-ci-project-graph.sh
dotnet test Essentials/tests/Architecture/Tests/Elsa.Architecture.Tests.csproj -c Release
dotnet run --project tools/maps/Elsa.Maps.Generator -- check
```

Adding spec 253 changes the generated spec inventory/status input. If the check reports stale maps, deliberately refresh only the relevant maps layer:

```bash
bash tools/maps/generate-maps.sh
dotnet run --project tools/maps/Elsa.Maps.Generator -- check
```

Review generated findings before continuing. Stage every changed generated-map path explicitly, including `docs/maps/manifest.json` if it changed. A green `check` is the freshness evidence; do not infer freshness from the manifest alone.

## Review and delivery gates (T009-T012)

- Review the full diff for scope, DRY, and repository/constitution compliance. Re-read #3102 comments and open PRs immediately before committing.
- Commit with the Elsa Workflows GitHub App identity, push the issue branch, open a PR with `Fixes #3102`, comment `pr: <url>` on the issue, request optional review with `@elsa-workflows-bot review`, and attach the PR to this task.
- Obtain independent correctness, repository/constitution, and spec/issue reviews. Run a clean self-review loop on the PR diff, fixing findings in new commits and reviewing each new head (maximum six rounds). Require all required checks green on the exact latest reviewed head, no failed check, and no human hold, changes-requested review, open review thread, or `blocked`/`needs:decision` label. Under the Oct 18 standing authorization, Codex may self-merge after those gates; Code Review is optional and must not delay the merge.
- Before a standing self-merge, add the `self-merged` label and post the required audit comment with review rounds, findings fixed, remaining low notes, and reviewed SHA. Record the audit comment URL in the merge commit.
- After merge, verify resulting-main CI and generated-maps gates, update issue/project status with the result, and release the claim with completion evidence.

## Evidence boundary

The component and browser fixture checks prove the editor's behavior for the exercised fixture. They do not establish that a real Workbench permission-denied journey was reproduced.
