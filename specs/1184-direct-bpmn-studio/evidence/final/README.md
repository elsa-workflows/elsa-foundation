# Final qualification and requirement map (T036–T037)

## Requirement map

Every functional requirement and success criterion maps to demonstrated evidence or to an existing owner deferral. "Actual" means real hosts and browsers with whole saved stores; "component" means unit, component or library proof.

| Item | Result | Evidence |
|---|---|---|
| FR-001 profile and refusal | Passed (actual + component) | [Core formats](../../../../Essentials/src/Activities/Bpmn/Core/README.md#declared-model-formats), [API boundaries](../../../../Essentials/src/Activities/Bpmn/Api/README.md#operational-boundaries), [interchange](../interchange/README.md), [publication](../publication/README.md) |
| FR-002 authoring in Studio | Passed (actual) | [authoring](../authoring/README.md), [human P1](../human/README.md), [claims authoring](../operations/README.md) |
| FR-003 direct publication distinct from native | Passed (actual) | [inspection](../inspection/README.md): zero native wrapper rows |
| FR-004 scoped bounded discovery after restart | Passed (actual) | [catalog](../catalog/README.md), [human P1](../human/README.md) fresh-browser discovery |
| FR-005 inspection | Passed (actual) | [inspection](../inspection/README.md), [ETL navigation](../etl/navigation.md), [cleared-state](../etl/cleared-state/README.md) |
| FR-006 exact claims, fenced profile | Passed (actual) | [claims](../claims/README.md): single owner, second-subject refusal, predecessor-binary refusal; [operations](../operations/README.md) |
| FR-007 replay and conflicts | Passed (actual) | [human P1](../human/README.md), [ETL recovery](../etl/actual-recovery.md) (lost accepted response), [claims](../claims/README.md), [operations](../operations/README.md) |
| FR-008 native work identity, no new scheduler | Passed (actual) | [ETL recovery](../etl/actual-recovery.md), [compatibility](../compatibility/README.md) |
| FR-009 ETL pipeline selection | Passed (actual) | [ETL](../etl/README.md), [ETL recovery](../etl/actual-recovery.md) (v1 pins kept after v2 published) |
| FR-010 held failure and exact retry | Passed (actual) | [ETL retry](../etl/retry.md), [ETL recovery](../etl/actual-recovery.md) |
| FR-011 BPMN ↔ native ↔ ETL navigation | Passed (actual) | [ETL navigation](../etl/navigation.md), [cleared-state](../etl/cleared-state/README.md) |
| FR-012 interruption and settlement truth | Passed (actual) | [operations](../operations/README.md) timer and cancellation of claimed work; retry/cancellation race below |
| FR-013 capability- and authorization-aware actions | Passed (component + actual) | [claims Studio](../claims/README.md#studio-t032--component-proof), [authoring](../authoring/README.md), [operations](../operations/README.md) other-owner refusal |
| FR-014 scope changes and durable links | Passed (component) | [ETL navigation](../etl/navigation.md), [human P1](../human/README.md) |
| FR-015 runtime-only and admission-off | Passed (actual) | [compatibility](../compatibility/README.md), [inspection](../inspection/README.md); claim work with admission off is component proof in [claims](../claims/README.md) |
| FR-016 existing compatibility; prior writers captured | Passed (actual) | [compatibility](../compatibility/README.md), [pre-claim](../preclaim/README.md) controls pass unchanged on the claim writer |
| FR-017 same semantics engine | Passed (actual) | [compatibility](../compatibility/README.md) shared traces |
| FR-018 editor quality, keyboard, themes | Passed (actual for P1) | [human P1](../human/README.md) keyboard/themes/320px; new claim controls not re-checked for themes/keyboard (limit) |
| FR-019 real evidence at declared source | Passed | Every actual entry above names its product source and whole stores |
| FR-020 statuses follow evidence | Ongoing | PR3603, #2907, #2892 and Project62 comments; wider ETL/agent/program acceptance stays open |
| FR-021 operational documentation | Passed | [API README](../../../../Essentials/src/Activities/Bpmn/Api/README.md#human-work-claims) |
| SC-001 P1 Studio journey | Passed (actual) | [human P1](../human/README.md) |
| SC-002 BPMN → ETL journey | Passed (actual) | [ETL recovery](../etl/actual-recovery.md), [cleared-state](../etl/cleared-state/README.md) |
| SC-003 races in both orders | Passed | Claims: [concurrent race and orders](../claims/README.md), [operations](../operations/README.md); timer/cancel × claim both orders; retry/cancellation both orders below |
| SC-004 frozen saved-Studio families | Passed (actual) | [compatibility](../compatibility/README.md) |
| SC-005 unavailable/unauthorized/runtime-only | Passed (component + actual) | [compatibility](../compatibility/README.md), [authoring](../authoring/README.md), [catalog](../catalog/README.md) |
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
