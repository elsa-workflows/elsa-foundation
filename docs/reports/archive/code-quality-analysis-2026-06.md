# Code Quality Analysis — 2026-06

Status: findings inventory from an automated, agent-driven code-quality pass over `src/Elsa`
and `tests/`. Each finding was verified against source before filing. This report is an
inventory plus a sequenced implementation plan; it is **not** the active queue (see
`AGENTS.md` program-goal routing).

Two findings from the initial pass were **discarded after verification** and are recorded
here so they are not re-raised:

- *"Static `SemaphoreSlim` in `EFCoreSaveCommand<TDbContext,TEntity>` is shared across entity
  types."* — Incorrect. In C#, each closed generic type gets its own static field, so the
  semaphore is already per-`TEntity`. The in-source comment documents this intent.
- *"`#nullable enable` is missing across Events/Mediator/Pipelines."* — Incorrect. `Nullable`
  is enabled at the project level (`<Nullable>enable</Nullable>` in every `.csproj`).

## Environment blockers (why implementation was not executed in-session)

- **No .NET SDK** is installed in the analysis environment (`net10.0` project; `dotnet` is not
  on `PATH` and no SDK exists on disk). No change could be compiled or tested.
- **Issue #253** reports the runtime scheduler solution currently **does not compile**.
- Together these gate the implement → self-review → PR → merge loop: code changes here could
  not be build- or test-verified, so they were not made or merged. Tracking issues and this
  plan are the deliverables; implementation should run in an environment with the SDK and a
  green build.

## Findings → issues

| Issue | Type | Area | Summary |
|---|---|---|---|
| [#270](https://github.com/elsa-workflows/elsa-foundation/issues/270) | Bug | Caching | `CancellationTokenSource` leaked in `ChangeTokenSignalInvoker.TriggerTokenAsync` (cancel without dispose). |
| [#271](https://github.com/elsa-workflows/elsa-foundation/issues/271) | Bug | Serialization | `PolymorphicObjectConverter` swallows deserialization failures and returns `default!` (silent null). |
| [#272](https://github.com/elsa-workflows/elsa-foundation/issues/272) | Improvement | Mediator/Events | Three near-identical pipeline builders with drifted APIs; extract a generic base. |
| [#273](https://github.com/elsa-workflows/elsa-foundation/issues/273) | Improvement | Events | Dispatcher failure policy + subscriber failure classification unimplemented (constitution §2.6 gap). |
| [#274](https://github.com/elsa-workflows/elsa-foundation/issues/274) | Improvement | Runtime | Scheduler work handlers duplicate deserialize/validate/factory logic; extract a base. (Gated on #253.) |
| [#275](https://github.com/elsa-workflows/elsa-foundation/issues/275) | Improvement | Activities | `FlowchartExecutionEngine` (795 lines; ~200-line method) — decompose by responsibility. |
| [#276](https://github.com/elsa-workflows/elsa-foundation/issues/276) | Improvement | Secrets | `Secret`/`SecretVersion` timestamps use inline `DateTimeOffset.UtcNow`; route through `TimeProvider`. |
| [#277](https://github.com/elsa-workflows/elsa-foundation/issues/277) | Improvement | Secrets | Duplicate lifecycle-policy visibility guard across 5 methods in `DefaultSecretManager`. |
| [#278](https://github.com/elsa-workflows/elsa-foundation/issues/278) | Task | Caching | Caching module has zero unit tests. |
| [#279](https://github.com/elsa-workflows/elsa-foundation/issues/279) | Improvement | Misc | Nits batch: `IRequestHandler` nullable constraint; mediator reflection inconsistency; Event vs Command logging asymmetry; test stub in `src/`; `async void` timer modernization. |

## Dependency / file-overlap map

- **Caching** — #270 and #278 touch the same module; #278 verifies #270. → **stack: #270 then #278.**
- **Secrets** — #276 and #277 both modify `DefaultSecretManager.cs`. → **stack: #276 then #277.**
- **Mediator/Events** — #272 (builders), #273 (strategies/context/handler), and #279 items
  #2/#3 (invokers, `EventPipeline`/`CommandPipeline`) overlap within Events/Mediator. → **one
  stream, sequential: #272 → #279(#2,#3) → #273.**
- **Independent / disjoint modules** — #271 (Serialization), #275 (Activities/Flowchart),
  #274 (Workflows/Runtime), #279 #1 (Mediator contract, one line) and #279 #4
  (Runtime/JavaScript) touch disjoint files and can run in parallel worktrees.
- **Global gate** — #253 (build) plus a working test suite are prerequisites for the
  refactors (#274, #275) and for verifying any change.

## Sequenced plan

**Wave 0 — prerequisite (blocking everything):** land #253 so the solution compiles and tests
run; confirm a green build in an SDK-equipped environment.

**Wave 1 — quick, isolated, high-confidence (parallel):**
- #270 → #278 (Caching stream)
- #271 (Serialization)
- #279 #1 and #279 #4 (trivial, isolated)

**Wave 2 — module-local, medium effort (parallel across modules, sequential within):**
- Secrets stream: #276 → #277
- Mediator/Events stream: #272 → #279(#2,#3)

**Wave 3 — architectural / high-effort (after green tests):**
- #273 — agree the failure-policy/classification contract with the maintainer first (touches a
  constitution gate), then implement.
- #274 — after #253 lands.
- #275 — pure refactor; do only with the test suite green.

## Parallelization (one branch + git worktree per stream — never share a working tree)

| Stream | Issues | Module(s) | Parallel-safe? |
|---|---|---|---|
| A | #270 → #278 | Caching | yes (disjoint) |
| B | #271 | Serialization | yes |
| C | #276 → #277 | Secrets | yes |
| D | #272 → #279(#2,#3) → #273 | Mediator/Events | yes vs others; **sequential within** |
| E | #279 #1, #279 #4 | Mediator contract, Runtime/JS | yes (trivial) |
| F | #275 | Activities/Flowchart | yes; **after green tests** |
| G | #274 | Workflows/Runtime | **after #253** |

Streams A–G touch disjoint top-level modules and so can run as concurrent git worktrees without
colliding on the index or build outputs. Within stream D the items share Events/Mediator files
and must be stacked.

## Tracking board

The repository's GitHub MCP tooling exposes issues/PRs/branches but **no Projects v2
(project board) API**, so a board could not be created programmatically in-session. Suggested
manual setup: a board with columns **Todo → In progress → In review → Done**, seeded with
#270–#279. This report can serve as the interim tracking surface.
