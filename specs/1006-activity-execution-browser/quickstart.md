# Validation and delivery evidence

## Reproduce the main acceptance journey

Build the affected Runtime API/provider projects and Studio Workflows module using the normal build-slot queue. Use `e2e-tests/README.md` for composed hosts; restart only owned processes.

Open Looping run `14ecwWkZIFk`. Select Do, expand Child executions, and open its fourth Body occurrence. Select Write Line: its captured input is `Current: 4`. Move the execution scope to the tenth occurrence while retaining Write Line: its captured input becomes `Current: 10`. Refresh and reload the exact link; the same execution and parent scope must remain selected.

## Executed validation, 26 October 2026

Local whole-project commands (all use the projects' normal Debug configuration):

| Project/check | Command | Result |
|---|---|---|
| Runtime | `dotnet test Essentials/tests/Workflows/Runtime/Tests/Elsa.Workflows.Runtime.Tests.csproj --no-restore` | 2,610 passed |
| EF inspection and runtime persistence | `dotnet test Essentials/tests/Workflows/Runtime/Persistence/EntityFrameworkCore/Tests/Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests.csproj --no-restore` | 1,009 passed |
| HTTP API | `dotnet test Essentials/tests/Workflows/Runtime/Api/Tests/Elsa.Workflows.Runtime.Api.Tests.csproj --no-restore` | 208 passed |
| Architecture | `bash tools/architecture/restore-ci-project-graph.sh`, then `dotnet test Essentials/tests/Architecture/Tests/Elsa.Architecture.Tests.csproj --no-restore` | 590 passed |
| Studio Workflows | `pnpm --dir Studio --filter @elsa-workflows/studio-workflows test` | 2,513 passed, 184 files |
| Studio types/build | `pnpm --dir Studio --filter @elsa-workflows/studio-workflows typecheck` and `build` | Passed, existing bundle budgets retained |
| Style | Targeted ESLint on changed TS/TSX; Stylelint on `activityExecutionBrowser.css` | Passed |
| Maps | `dotnet run --project tools/maps/Elsa.Maps.Generator -- check` | Passed |
| Hosts | Affected Workbench and Studio Web builds | Passed |

`--list-tests` comparison: EF 1,009 and API 208 equal execution counts. Runtime lists 2,599 entries and executes 2,610 because three existing theories expand at execution: encryption predicate 1→8, non-rule failure 1→4, refused commit 1→2. Architecture lists 581 and executes 590 because collectible API artifacts expand 1→6 and capability declarations 1→5. TRX method-family comparison found no listed method omitted.

The new tests cover backend filtering before pagination, exact parents and transitive ancestry, repeated ordinary executable nodes, nested loops, parallel branches, reusable invocation contexts, retries versus distinct iteration identities, tied timestamps and stable hash sequences, anchors/counts, tenant and authorization isolation, sensitive metadata redaction, cursor binding, missing evidence, unsupported capability, stale requests, denied evidence clearing, interrupted ancestry, and newer navigation intents.

Causal checks: removing denied-history evidence clearing made its UI regression fail; restoring it passed. Bypassing the ancestor predicate made the nested/parallel/reusable Runtime regression fail (1 failed); restoring it passed the whole Runtime suite. Neither mutation remains in source.

## Composed browser proof

Private proof hosts used this worktree's rebuilt assets and a SQLite backup of the existing local Workbench data. Original user hosts and their database were left running and unchanged.

- Do has one execution, with ten recorded Body occurrences.
- Fourth scope occurrence with Write Line selected showed `Current: 4`; tenth showed `Current: 10`.
- Moving between scope occurrences retained Write Line and selected its corresponding exact execution.
- Refresh and exact execution deep-link reload restored the parent chain and captured value.
- Classic Light and Dark were inspected; 390×844 layouts and the scrollable scope picker were checked. Escape closed the picker and returned focus to its trigger. Temporary viewport/theme changes were restored.

## Remaining environmental gate

The PostgreSQL, MySQL and SQL Server provider smoke scenarios were extended, but all three container executions were skipped because Docker is unavailable locally. They are **not** counted as passing; required provider-mode CI must execute them. SQLite and actual HTTP permission/redaction tests passed locally.

Own review completed with three independent lenses (correctness, acceptance and standards). Findings about scope cancellation, late selections, denial clearing, ordering precision, iteration identity, reusable layout navigation and API compatibility were fixed before the final clean review. See issue/PR evidence for review transitions and CI status.
