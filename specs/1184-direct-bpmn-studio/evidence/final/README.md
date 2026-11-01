# Final qualification and requirement map (T036–T037)

## Requirement map

Each functional requirement and success criterion maps to its evidence, or to an owner deferral, with its actual result. Component-only or partial items are marked as such. "Actual" means real hosts and browsers with whole saved stores; "component" means unit, component or library proof.

| Item | Result | Evidence |
|---|---|---|
| FR-001 profile and refusal | Passed (actual + component) | [Core formats](../../../../Essentials/src/Activities/Bpmn/Core/README.md#declared-model-formats), [API boundaries](../../../../Essentials/src/Activities/Bpmn/Api/README.md#operational-boundaries), [interchange](../interchange/README.md), [publication](../publication/README.md) |
| FR-002 authoring in Studio | Passed (actual) | [authoring](../authoring/README.md), [human P1](../human/README.md), [claims authoring](../operations/README.md) |
| FR-003 direct publication distinct from native | Passed (actual) | [inspection](../inspection/README.md): zero native wrapper rows |
| FR-004 scoped bounded discovery after restart | Passed (actual) | [catalog](../catalog/README.md), [human P1](../human/README.md) fresh-browser discovery |
| FR-005 inspection | Passed (actual) | [inspection](../inspection/README.md), [ETL navigation](../etl/navigation.md), [cleared-state](../etl/cleared-state/README.md) |
| FR-006 exact claims, fenced profile | Passed (actual) | [claims](../claims/README.md): single owner across restart, two signed-in subjects racing three times on the real host, predecessor-binary refusal; [operations](../operations/README.md) in Studio |
| FR-007 replay and conflicts | Passed (actual) | [human P1](../human/README.md), [ETL recovery](../etl/actual-recovery.md) (lost accepted response), [claims](../claims/README.md), [operations](../operations/README.md); cancellation replay after a real restart and a conflicting reuse in `BpmnHumanClaimUpgradeTests` |
| FR-008 native work identity, no new scheduler | Passed (actual) | [ETL recovery](../etl/actual-recovery.md), [compatibility](../compatibility/README.md) |
| FR-009 ETL pipeline selection | Passed (actual) | [ETL](../etl/README.md), [ETL recovery](../etl/actual-recovery.md) (v1 pins kept after v2 published) |
| FR-010 held failure and exact retry | Passed (actual) | [ETL retry](../etl/retry.md), [ETL recovery](../etl/actual-recovery.md) |
| FR-011 BPMN ↔ native ↔ ETL navigation | Passed (actual) | [ETL navigation](../etl/navigation.md), [cleared-state](../etl/cleared-state/README.md) |
| FR-012 interruption and settlement truth | Passed (actual) | [operations](../operations/README.md) in Studio; real-host claim/cancel and completion/timer in both orders; retry/cancellation race below, with an admitted replacement settled under a winning cancellation |
| FR-013 capability- and authorization-aware actions | Passed (component + actual) | [claims Studio](../claims/README.md#studio-t032--component-proof), [authoring](../authoring/README.md), [operations](../operations/README.md) other-owner refusal |
| FR-014 scope changes and durable links | Component; actual links only | Durable run/ETL links and fresh-session discovery are actual ([human P1](../human/README.md), [ETL navigation](../etl/navigation.md)). Switching backend or account during a pending operation is proved by component tests only |
| FR-015 runtime-only and admission-off | Passed (actual) | [compatibility](../compatibility/README.md), [inspection](../inspection/README.md); claim work stays claimable, completable and cancellable after a real restart with admission off (`BpmnHumanClaimUpgradeTests`) |
| FR-016 existing compatibility; prior writers captured | Passed (actual) | [compatibility](../compatibility/README.md); on the claim writer, the whole hosting project (all `Legacy*Upgrade*` native families and composites) and the [pre-claim](../preclaim/README.md) captures pass; the saved-Studio families are repeated below |
| FR-017 same semantics engine | Passed (actual) | [compatibility](../compatibility/README.md) shared traces; the shared-profile hosting families pass again on the claim writer |
| FR-018 editor quality, keyboard, themes | Passed (actual) | [human P1](../human/README.md); the claim controls are reached by keyboard and checked in both themes at 1280 and 320px in the repeated [operations](../operations/README.md) journey |
| FR-019 real evidence at declared source | Passed | Every actual entry above names its product source and whole stores |
| FR-020 statuses follow evidence | Ongoing | PR3603, #2907, #2892 and Project62 comments; wider ETL/agent/program acceptance stays open |
| FR-021 operational documentation | Passed | [API README](../../../../Essentials/src/Activities/Bpmn/Api/README.md#human-work-claims) claims, boundaries and at-least-once external effects |
| SC-001 P1 Studio journey | Passed (actual) | [human P1](../human/README.md) |
| SC-002 BPMN → ETL journey | Passed (actual) | [ETL recovery](../etl/actual-recovery.md), [cleared-state](../etl/cleared-state/README.md) |
| SC-003 races in both orders | Passed (actual) | Claim contention: two subjects race on the real host. Claim × cancellation and claimed completion × timer in both orders on the real host. Retry × cancellation in both orders below |
| SC-004 frozen saved-Studio families | Passed (actual) | [compatibility](../compatibility/README.md) |
| SC-005 unavailable/unauthorized/runtime-only | Partial | Actual: runtime-only and admission-off, absent capabilities, second-subject refusal. Component only: unauthorized and cross-tenant disclosure, read-only/source-owned editing and backend switching during a pending operation. The owned Workbench has one tenant and seeds only administrators |
| SC-006 exact-head gates and resulting main | Pending | Recorded at merge |

Owner deferrals that remain outside this feature: Orleans and clustered placement, the task-runner host, a full operator console or inbox, assignment teams and timed leases, arbitrary token or history alteration, and live migration.

## Retry and cancellation race (SC-003)

`BpmnServiceRetryCancellationTests` drives the production Workbench. It publishes an always-faulting native worker (the primitive Fault activity) under `HoldForRetry` with a two-attempt budget, and waits for the actual held incident.

- **Cancel first:** the execution settles Cancelled; the original failure is retained as Retired; the later retry is refused as `execution-terminal`; and no replacement worker ever exists.
- **Retry first:** the retry is accepted and a cancellation follows immediately. Exactly one replacement attempt exists. Whichever wins is truthful: Cancelled with the replacement NotAdmitted or Cancelled, and the cancel accepted; or Faulted with the replacement Faulted, and the cancel refused. The physical worker count equals the admitted attempts. The exact retry replays without another attempt, and a fresh retry is refused.

Both orders passed three consecutive runs. The first version wrongly assumed the replacement worker is always admitted; the actual host showed the cancellation retiring its start before admission, and the assertion now reads the persisted attempts.

## Candidate qualification (T036)

The candidate is `d808f7bf2`: the branch with main `b392aef0a` merged cleanly. Results at load averages of 10–19 on 15 cores:

| Check | Result |
|---|---|
| Locked-mode CI restore; full `Elsa.Server.slnx` Release build | Passed, 0 errors |
| BPMN / Interchange / Architecture | 575 / 196 / 609 passed, 0 failed or skipped |
| Runtime API / ETL / Runtime EF (BPMN filter) | 217 / 1,033 / 86 passed |
| Real host: claims, pre-claim captures, direct, authored publication, Runtime-only | 9 passed |
| Retry and cancellation race (above) | 2 × 3 runs passed |
| Generated solution-filter check; evidence size guard; evidence restore controls | Passed |
| Studio workspace build / lint / typecheck | Passed. The first typecheck failed only because the new `Elsa.Studio.Backends` workspace package from main was not yet installed locally; it passed after a frozen-lockfile install. |
| Studio workflows / ETL suites | 3,092 / 106 passed |

**ApiCompat** (`Microsoft.DotNet.ApiCompat.Tool` 10.0.403, all references resolved) found no breaking change against each last-published baseline:

- `Elsa.Workflows.Runtime.Core` 4.0.61-preview (`8dcbf2345`).
- `Elsa.Activities.Bpmn.Core` 4.0.2-preview (`12c8e54df`).

Reversed controls report the additions as removals (2 and 19 differences), which proves the comparison sees them. Under constitution §4.2 every delta is compatible expansion: default interface members, a constructor overload, new types, optional init properties and a new constant. That is Minor. Before GA the packages stay on the single 4.0 line (§E5), and the calculator advances the patch; no version-line or exception change is requested. PR3410's version exception is not inherited.

**Not established here:** exact-head required CI (it is queued on the candidate), independent full-PR review, and resulting-main verification.
