# Native compensation: qualification and release readiness

**Program:** [Native compensation #3035](https://github.com/elsa-workflows/elsa-foundation/issues/3035) · **Feature:** [#3048](https://github.com/elsa-workflows/elsa-foundation/issues/3048) (M5) · **Spec:** [261](../../specs/261-compensation-qualification/spec.md) · **Outline:** [release-readiness contract](../../specs/261-compensation-qualification/contracts/release-readiness.md)

**Status:** sections 1–4, 7 (the PR 2 findings) and 8 written by spec 261 PR 2 (tasks T527–T529). PR 3 completes sections 5–7 (T533) and lists this record in the reports index.

## 1. Scope and candidate

This record qualifies the integrated candidate. No package was released, no image was published and nothing was deployed; releases and deployment are outside this program by owner decision.

| Milestone | Spec | Merged |
|---|---|---|
| M1 Reservation journey that survives restart during recovery | [250](../../specs/250-native-compensation/spec.md) | [#3089](https://github.com/elsa-workflows/elsa-foundation/pull/3089) |
| M2 Uncertain effects, retries and intervention | [251](../../specs/251-compensation-uncertainty/spec.md) | [#3113](https://github.com/elsa-workflows/elsa-foundation/pull/3113), [#3117](https://github.com/elsa-workflows/elsa-foundation/pull/3117) |
| M3 Nesting, repetition and confirmation | [258](../../specs/258-compensation-nesting/spec.md) | [#3126](https://github.com/elsa-workflows/elsa-foundation/pull/3126), [#3127](https://github.com/elsa-workflows/elsa-foundation/pull/3127) |
| M4 Operator actions in Studio | [260](../../specs/260-compensation-operator-actions/spec.md) | [#3136](https://github.com/elsa-workflows/elsa-foundation/pull/3136), [#3140](https://github.com/elsa-workflows/elsa-foundation/pull/3140) |
| M5 Backoff, the replay-safe warning, qualification | [261](../../specs/261-compensation-qualification/spec.md) | PR 1 [#3152](https://github.com/elsa-workflows/elsa-foundation/pull/3152) (open when this was written); PR 2 (this change); PR 3 to follow |

**Candidate revision.** Spec 261 PR 2 at commit `d7c0ab201`, which is PR 1 (`c69192425`, on `main` at `b555bd846`) plus PR 2's code. Studio is in the same repository, so one revision names both. The later PR 2 commits change documentation only. Every result below ran on that revision unless its row says otherwise. Results that only CI can produce (the server-provider rows, CI on the PR head, and the post-merge runs) are marked **pending** and are recorded on #3048 when they exist; a row is not claimed until its result is in.

## 2. Qualification matrix

Rows are the [qualification matrix](../../specs/261-compensation-qualification/contracts/qualification-matrix.md). "Local" is a Release build on the candidate, run by the author on one Linux machine (4 cores); **B&T**, **EF**, **Studio**, **Arch** and **Maps** are the CI jobs the matrix names.

| Row | Claim | Revision | Job or command | Result | Notes |
|---|---|---|---|---|---|
| Q1 | Every M1–M5 journey in each mode on SQLite | `d7c0ab201` | Local: `dotnet test Essentials/tests/Activities/Compensation/Tests -c Release` | 427/427 | Includes BK0–BK6, the provider subset on SQLite (21) and every journey below that runs in this project. B&T on the PR head: pending. |
| Q2 | Restart injection, every documented point | `d7c0ab201` | As Q1 | Included in Q1 | Spec 250 P1–P4, spec 251 points 1–4, spec 258 points 1–4, spec 260 O8 1–3, spec 261 backoff points 1–3. |
| Q3 | Real process kill through REST | `d7c0ab201` | Local: `dotnet test Essentials/tests/Workbench/Tests -c Release --filter FullyQualifiedName~WorkbenchCompensation` | 6/6 | Includes the backoff kill while a retry waits, and W6, which now also asserts that the promotion preflight is ready with only warnings (see section 7). |
| Q4 | PostgreSQL in each mode | `d7c0ab201` | Local: provider subset with `ELSA_COMPENSATION_EF_REQUIRE_NATIVE_PROVIDERS=1` against a PostgreSQL 16.15 server (`ELSA_COMPENSATION_EF_POSTGRESQL_TEST_CONNECTION_STRING`) | 21/21 | Docker was unavailable, so this ran against an installed PostgreSQL 16 server rather than the suite's `postgres:16-alpine` container. CI: `EF container suites (compensation)` on `3214f9242` ([run 39546015477](https://github.com/elsa-workflows/elsa-foundation/actions/runs/39546015477/job/119044528563)), with the required-provider switch: 63/63 across the three providers, 0 skipped. |
| Q5 | SQL Server in each mode | `3214f9242` | CI: `EF container suites (compensation)`, [run 39546015477](https://github.com/elsa-workflows/elsa-foundation/actions/runs/39546015477/job/119044528563), `ELSA_COMPENSATION_EF_REQUIRE_NATIVE_PROVIDERS=1` | Passed (63/63 for the three providers, 0 skipped) | |
| Q6 | MySQL in each mode | `3214f9242` | CI: `EF container suites (compensation)`, [run 39546015477](https://github.com/elsa-workflows/elsa-foundation/actions/runs/39546015477/job/119044528563), `ELSA_COMPENSATION_EF_REQUIRE_NATIVE_PROVIDERS=1` | Passed (with Q4 and Q5) | |
| Q7 | In-memory stores: development and tests, no restart guarantee | `d7c0ab201` | Local: `CompensationInMemorySmokeTests` | 10/10 | J1, N1, O2h in each mode, and a second host generation is refused. |
| Q8 | Scope state upcast v1→v5, resumed live | `d7c0ab201` | Local: `CompensationUpcastJourneyTests` and the payload upcast unit tests | 4/4 (U1–U4), unit tests in Q1 | |
| Q9 | Serialization of wire contracts | `d7c0ab201` | Local: journal projector and alteration tests in Q1; S6 on PostgreSQL in Q4 | Passed | S6 also passed on SQL Server and MySQL in Q5 and Q6. |
| Q10 | Executable pinning in each mode | `d7c0ab201` | Local: `A_republish_mid_recovery_keeps_the_handlers_of_the_pinned_executable` (now a theory), BK5 | 3/3, BK5 in Q1 | |
| Q11 | Retention | `d7c0ab201` | Local: `CompensationRetentionJourneyTests` R1–R4; `J1_a_completed_scope_keeps_no_journal`; `Elsa.Workflows.Runtime.Api.Tests` (run deletion through the API) | 12/12; in Q1; 207/207 | R10 settled with no defect ([research R10 outcome](../../specs/261-compensation-qualification/research.md#r10-outcome-t524-pr-2)). |
| Q12 | Studio authoring, publish, run, inspect (live) | `d7c0ab201` | Local pair: `ELSA_STUDIO_URL=http://localhost:5089 npx playwright test --config tests/live/playwright.live.config.ts compensation-journey.spec.ts` | 1/1 | Repaired (#3142). Lists the four replay-safe warnings and publishes. Axe clean at 1200 and 390 px on the designer and the Warnings section. |
| Q13 | Studio nesting and Confirm view (live) | `d7c0ab201` | Local pair: `nesting-journey.spec.ts` | 1/1 | New. Axe clean at both widths. |
| Q14 | Studio operator recovery (live) | `d7c0ab201` | Local pair: `operator-actions-journey.spec.ts` | 2/2, reader step not run | The reader step needs a read-only account (`ELSA_STUDIO_READONLY_USER`); the Workbench has no API to create one, so it was skipped. The backoff step is new. |
| Q15 | Studio states, dialogs and axe (mocked) | `d7c0ab201` | Local: `pnpm --recursive --workspace-concurrency=1 test`; `playwright test compensation-scope.spec.ts workflow-publication-review.spec.ts` | Vitest 3,261/3,261 (Workflows 2,284); browser 89/89 | No Studio source changed in PR 2. Studio CI on the PR head: pending. |
| Q16 | Negative controls | `d7c0ab201` | By hand, each reverted | See section 8 | |
| Q17 | Architecture, maps, current head | `d7c0ab201` | Local: architecture tests, `Elsa.Maps.Generator -- check`, full solution build | 584/584; check passed; 0 errors | CI on the PR head and the post-merge runs: pending (#3048 comment). |
| Q18 | Oracle | — | — | **Not qualified** | |
| Q19 | Multi-node placement (`runtime-distributed`) | — | — | **Not qualified** | |
| Q20 | Compensation inside a Studio test run beyond 30 minutes | `d7c0ab201` | Local: `R4_a_test_run_that_waits_past_its_window_stays_resumable` | 3/3 | Settled: a plain test run keeps waiting and stays resumable; a trigger test run is cancelled. Documented in the guide. |

The live pair was the Workbench (`Essentials/apps/Elsa.Workbench`, Release, `http://localhost:5095`) and Studio (`Studio/src/apps/Elsa.Studio.Web`, `http://localhost:5089`), both built from `d7c0ab201`, driven by Chromium 1194 (the installed Playwright expects a newer build, so the run pointed `launchOptions.executablePath` at the installed one).

## 3. Supported combinations

| Combination | Rows | State |
|---|---|---|
| SQLite, Durable / Balanced / Ephemeral | Q1–Q3, Q7–Q11 | Qualified on the candidate |
| PostgreSQL, Durable / Balanced / Ephemeral | Q4 (provider subset), plus Q1 semantics | Qualified (local PostgreSQL 16 and the CI container run) |
| SQL Server, Durable / Balanced / Ephemeral | Q5 (provider subset), plus Q1 semantics | Qualified (CI container run) |
| MySQL, Durable / Balanced / Ephemeral | Q6 (provider subset), plus Q1 semantics | Qualified (CI container run) |
| Studio authoring, publish, run, inspect, nested view, operator recovery | Q12–Q15 | Qualified on the candidate, except the M4 reader step (not run) |
| One host process, restarts included | Q3 | Qualified on the candidate |

The release-readiness outline drafted the last row as "one Workbench process, or several processes over one store". No compensation journey runs several processes over one store, so that part moved to section 4.

## 4. Not qualified

| Combination | Why | To qualify |
|---|---|---|
| In-memory stores | No durability: a restart loses the run, a waiting scope and any scheduled retry | Not applicable; development and tests only (Q7) |
| Oracle | No provider in CI ([scoping report](ef-core-oracle-scoping-2026-08.md)) | A provider suite plus the subset |
| Several host processes over one store | The runtime's claims and fences are proven by the runtime suites; no compensation journey runs with two live processes | The subset with two hosts over one store |
| Multi-node placement (`runtime-distributed`) | No compensation journey with placement and handover | The subset under the distributed placement fixture |
| A trigger-started Studio test run that waits longer than 30 minutes | Its window's end cancels the run, and cancellation never compensates (research R10) | Not applicable; use a published run |
| Live browser journeys in CI | Opt-in by design (spec 261 defaults) | A CI job that starts a Workbench and a Studio |

## 5. Known limits

Written by PR 3 (T533) from the [draft list](../../specs/261-compensation-qualification/contracts/release-readiness.md#known-limits-draft). PR 2 adds two, already in the guide: deleting a run that has not finished cancels it first (so a waiting recovery is never undone), and a plain test run that waits past its window keeps running until an operator acts.

## 6. Open follow-ups

Written by PR 3 (T533). PR 2 closes [#3142](https://github.com/elsa-workflows/elsa-foundation/issues/3142) and [#3130](https://github.com/elsa-workflows/elsa-foundation/issues/3130).

## 7. Review findings

The M5 review of the M1–M4 runtime and Studio code (T527, FR-521), and what the qualification runs found. PR 3 completes this section with anything filed.

| Finding | Where | Resolution |
|---|---|---|
| The promotion preflight Studio's **Review & publish** reads counted the replay-safe warnings as blocking `draft-validation` issues (`isReady: false`), so no workflow with a replay-safe compensated step could be published from Studio. Promotion itself and REST publication ignored warnings, which is why PR 1's W6 test passed. Found by the first live run of the M1 journey. | `Essentials/src/Workflows/Design/Api/Endpoints/Drafts/PromotionPreflight/Endpoint.cs` | Fixed in PR 2: the preflight counts and lists errors only, as promotion and the portfolio count do. Unit test and the W6 Workbench assertion; negative control below. |
| `permissions.canManage` on the diagnostics settings was `true` for every caller (#3130). | `RuntimeDiagnosticsSettingsService`, `RuntimeDiagnosticsPermissions` | Fixed in PR 2 (T525). |
| FR-519 says a running or waiting run cannot be deleted. That holds for the provider-fenced store (`NotTerminal`), but `DELETE runtime/workflows/instances/{id}` cancels a running run first and then deletes it, which never compensates. | `WorkflowInstanceDeletionService` | Documented in the guide's current limits; no code change, since the API behaviour is deliberate and tested. |
| The publication review's **Publish** button fails colour contrast: `#006cac` on `#d0e9f8` is 4.46:1, under the 4.5:1 AA minimum. Not compensation UI. | `.wf-publication-footer .wf-primary-action` (Workflows module CSS) | Not fixed: outside spec 261's scope. Reported for filing. |
| A Studio tab reloaded a few dozen times in a row fails requests with `net::ERR_INSUFFICIENT_RESOURCES` and then registers no modules ("No Studio route is registered"). Seen only when a live journey polled by reloading one tab for minutes. | Studio shell under repeated reloads | The live journeys poll long waits in fresh tabs (`readScopeInFreshTab`). Reported for triage, in case one connection per load is leaked. |

## 8. Evidence

**Negative controls.** PR 1's controls N501–N506 are recorded in the [quickstart](../../specs/261-compensation-qualification/quickstart.md#results-pr-1-run-once-by-hand-each-reverted). PR 2's controls are recorded there too; each was run once by hand and reverted:

| Control | Removed | Failing tests |
|---|---|---|
| N507 | The promotion preflight's warning filter | Design API 1 of 176 (the new warnings-only test); Workbench W6 |
| N508 | The diagnostics service's per-caller grant (always `canManage: true`) | Runtime API 4 of 207 (reader, denied grant, no rule, run-only rule) |
| N509 | The version 1 upcast's operation key rule | 3: U1 and the two v1 payload tests |
| N510 | Retained runs as executable retention roots | R3 in each mode (3) |
| N511 | The outbox's `NotBefore` availability (N501), against the provider subset | S7 in each mode (3), and R3 in each mode (3) |
| N512 | The in-memory store's one-generation guard | 1: the refused-second-generation test |

**Live journeys.** Section 2, Q12–Q14: four journeys passed and one (the reader step) was skipped, in 3.0 minutes on `d7c0ab201`.

**Studio.** On `d7c0ab201`: `pnpm -r typecheck` and `pnpm lint` pass; Vitest 3,261 tests in 268 files pass; the mocked browser specs for the compensation scope and the publication review pass 89/89 on Chromium 1194. The live journeys are not in a typechecked project; checked on their own (`tsc` with the Studio base config), they have no error but the unresolved `process` global every live journey shares, and ESLint passes on them.

**CI.** The PR 2 head's `Build & test`, `Studio CI`, `Architecture guards` and `Generated maps fresh`, the dispatched `EF container suites` run (for Q4–Q6), and the post-merge `main` CI and nightly EF run on the merge commit are recorded as a comment on #3048 once they exist. This record links that comment rather than being edited for status.
