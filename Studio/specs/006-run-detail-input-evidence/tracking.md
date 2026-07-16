# Delivery Tracking: Run Detail Input Evidence Workbench

**Last updated**: 2026-07-16

| Slice | State | Issue | PR | Evidence |
|---|---|---|---|---|
| PRD / parent | Published | [Studio #389](https://github.com/elsa-workflows/elsa-foundation-studio/issues/389) | — | `spec.md`, requirements checklist |
| S1 Foundation contract | Implemented and verified | [Foundation #673](https://github.com/elsa-workflows/elsa-foundation/issues/673) | Pending | Source Reference sidecar, structured endpoint, catalog identity, hash invariance |
| S2 Foundation runtime provenance/security | Implemented and verified | [Foundation #673](https://github.com/elsa-workflows/elsa-foundation/issues/673) | Pending | Per-input results, all three phases, replay dedup/sequence, permission split |
| S3 Studio paired input evidence | Implemented and verified | [Studio #390](https://github.com/elsa-workflows/elsa-foundation-studio/issues/390) | Pending | ReferenceKey union, compact/expanded rows, renderer fallback, protected states |
| S4 Studio workbench layout | Implemented and verified | [Studio #391](https://github.com/elsa-workflows/elsa-foundation-studio/issues/391) | Pending | Full-height route, Run-specific sizing, responsive modes, Chromium proof |
| S5 Integration | Ready for review | Tracked by parent | Pending | Focused/full affected suites, typecheck, lint, build, and browser proof recorded below |

## Board status

No project-board mutation capability has been detected in the available GitHub connector. Issues and PRs remain the authoritative tracking surface unless a board capability becomes available.

## Worktree notes

- Studio branch: `codex/run-detail-input-evidence`
- Foundation branch: `codex/run-detail-input-evidence`
- A Foundation Groundwork schema lock was present at the start and later disappeared through concurrent workspace activity; this feature did not modify it.
- Preserve the unrelated concurrent Studio module-manifest migration, architecture/presentation docs, hosting tests, feature attributes, and handler/service-extension removals that appeared mid-run.

## Review log

| Iteration | Scope | Result |
|---|---|---|
| 0 | PRD and plan | Interview decisions captured; parent and slice issues published with `ready-for-agent` |
| 1 | Independent design analysis | Corrected artifact/source placement, wire compatibility, permission boundaries, replay safety, exact layout thresholds, and browser-test seam before implementation |
| 2 | Root integration | Kept authored JSON on the immutable Source Reference sidecar, kept compiled behavior on the executable, and split source/evidence authorization |
| 3 | Runtime correctness/security | Replaced fail-fast materialization with per-input results; retained successful evidence; added safe failed rows, incident correlation, replay deduplication, and per-input sequence allocation |
| 4 | Studio compatibility/accessibility | Aligned canonical `evaluationPhase`, `evaluationSequence`, and `accessState` while accepting preview aliases; isolated contributed renderer failures and bounded source output; verified focus/resize behavior |
| 5 | Geometry and release proof | Added real-browser desktop/medium/narrow geometry, checked bundle budgets, reran affected suites, and preserved unrelated worktree changes |

## Validation log

| Stage | Command | Result |
|---|---|---|
| Studio baseline | `pnpm --filter @elsa-workflows/studio-workflows test` (bundled Node PATH) | 49 files, 570 tests passed before implementation |
| Foundation build | `dotnet build src/Elsa/Activities/Runtime/Elsa.Activities.Runtime.csproj --no-restore` and Runtime API build | Passed, 0 warnings/errors |
| Foundation publishing | `dotnet test tests/Elsa/Workflows/Publishing/Api/Tests/Elsa.Workflows.Publishing.Api.Tests.csproj --no-restore` | 156 passed |
| Foundation runtime | `dotnet test tests/Elsa/Workflows/Runtime/Tests/Elsa.Workflows.Runtime.Tests.csproj --no-restore` | 922 passed |
| Foundation activity runtime | `dotnet test tests/Elsa/Activities/Runtime/Tests/Elsa.Activities.Runtime.Tests.csproj --no-restore` | 182 passed |
| Foundation Runtime API | `dotnet test tests/Elsa/Workflows/Runtime/Api/Tests/Elsa.Workflows.Runtime.Api.Tests.csproj --no-restore` | 32 passed |
| Foundation catalog API | `dotnet test tests/Elsa/Activities/Design/Api/Tests/Elsa.Activities.Design.Api.Tests.csproj --no-restore` | 5 passed |
| Studio focused Workflows | Package-local Vitest for domain client, executable graph, input union, paired evidence, and layout | 5 files, 43 passed |
| Studio expression modules | Package-local JavaScript and Liquid Vitest | 7 passed |
| Studio typecheck | `pnpm typecheck` | 17 workspace projects passed |
| Studio lint | ESLint on changed TS/TSX plus Stylelint on changed CSS | 0 errors; 2 pre-existing SDK empty-interface warnings; CSS clean |
| Studio build/budget | `pnpm --filter @elsa-workflows/studio-workflows build` | Passed; primary 27.11 kB gzip, CSS 20.78 kB gzip, authenticated total 60.98 kB gzip |
| Browser geometry | `pnpm exec playwright test tests/browser/workflow-run-detail-layout.spec.ts` | Chromium: 1 passed; desktop fills to console, medium drawer, narrow exclusive view |
| Studio broad Workflows | Package-local `pnpm test` with no competing builds | 51 files, 584 passed |

## Environment notes

- The repository-wide `pnpm lint` target was stopped after 11 minutes because it recursively scanned ignored `.claude/worktrees`; changed-file ESLint and CSS lint completed cleanly.
- A root-level ad hoc Vitest invocation likewise discovered `.claude/worktrees`; authoritative reruns were executed from each owning package so only the current worktree was tested.
