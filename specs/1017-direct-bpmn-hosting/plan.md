# Implementation Plan: Direct and embedded BPMN production hosting

**Branch**: `agent/codex/2898-direct-bpmn-hosting` | **Date**: 2026-10-28 | **Spec**: [spec.md](spec.md)

**Input**: `specs/1017-direct-bpmn-hosting/spec.md`, clean independent specification review at `10af302d89ba10d3c91d8064d6c3c14b1123cf9c`.

## Summary

Deliver #2898's genuine direct BPMN execution and equivalent native embedding through the existing fenced runtime authority. Add only the material/state/dispatch participation missing from the native-shaped implementation. Keep the released interpreter, existing native DTOs and legacy BPMN adapter. A new `RunBpmn` native adapter invokes the same direct-model lifecycle; a pinned native `RunEtl` workflow supplies real service work. Qualify cold-process races, actual pipeline cancellation, supported human/incident commands and existing saved-execution/Studio compatibility.

This is an implementation design awaiting independent review. It does not assert neutral public-API stability or completed runtime acceptance. [Research decisions](research.md), [state model](data-model.md), [contracts/profile](contracts/hosting.md), [validation sequence](quickstart.md).

## Technical Context

**Language/Version**: Existing C#/.NET repository defaults; SDK 10.0.300 from `global.json`. Follow each existing project's target framework and centrally managed package versions.

**Primary Dependencies**: Released Bpmn.Model/Bpmn.Semantics 0.5.0, existing payload 1.10.0, CShells, runtime actor/scheduler/checkpoint/outbox, EF persistence family and existing ETL/native publication contracts. No new actor framework or task-runner dependency.

**Storage**: Existing RuntimeDbContext and artifact/coordination/ownership/queue/outbox/marker stores. Add typed opaque model-state/accepted-input persistence to the same checkpoint transaction and versioned model material to the existing artifact family. No independent BPMN commit authority. SQLite qualifies the actual hosting/upgrade journey; all affected first-party EF schemas and common provider semantics receive their own tests. ETL keeps its existing authoritative stores and wire.

**Testing**: Existing xUnit risk-based suites plus a dedicated real-host BPMN hosting test project in the integration tier. Native/EF/material/payload compatibility tests and safeguard-removal controls; actual owned Workbench/Studio sessions with portable durable captures.

Test cadence follows ADR0080/Elsa's deliberate absence of a blanket TDD gate: add focused behavior tests with their implementation and demonstrate meaningful failing safeguard-removal controls before final review. No new owner cadence decision is needed for this existing program.

**Target Platform**: Existing built-in execution host on supported .NET server platforms; local acceptance on macOS, required CI on its configured platforms. Runtime-only execution consumes immutable artifacts and installed runtime modules, without Design storage.

**Project Type**: Modular libraries, runtime/API consumers, native activity adapter and integration qualification.

**Performance Goals**: None; ADR0080 retired benchmark gates. No throughput or physical-memory-reclamation claims.

**Constraints**: Frozen authored fingerprint and legacy v1/v2 state; exact scope/tenant/authority and invocation/attempt identities; atomic decision/fence/receipt/outbox; at-least-once physical effects; model-owned semantics; native composite-specific fault behavior.

**Scale/Scope**: One existing issue/PR, five specified journeys. Minimal supported direct profile and operational commands, not complete BPMN conformance, task administration or #2907 direct-host Studio completion.

## Constitution Check

Loaded governing framework 6.1.0, Elsa 6.3.0 and the contributor review checklist against baseline `d3ebb980e`. The draft/provisional provisions retain their status. Pre-design check passes with the boundaries below; repeat after implementation against actual restored dependencies/public surfaces.

| Gate | Design disposition / verification |
|---|---|
| Framework §2.1–2.4, §2.18; Elsa E2.1 | Existing Workflows.Runtime and Activities domains only. New Activities.Bpmn.Core contains contracts/DTOs for actual host/API/model consumers, no Bpmn.Model/Semantics/EF implementation. No Foundation.Core or universal graph. |
| §2.5–2.6, §2.11 | Scoped drivers/services. Existing singleton mailbox bridge opens scopes. Model registrations use a named Sequential initialization event, one contributor aggregator, immutable registry and startup duplicate/protocol checks; no hand-rolled anonymous bus or overlapping first-match handlers. Replacement ports diagnose duplicates. |
| §2.7, §2.9–2.10, §2.20 | Bpmn library behind BPMN adapter; provider-neutral CQS contracts. Existing consumer RuntimeDbContext owns EF participation. No runtime/API feature depends on concrete provider; host selects persistence. |
| §2.14, §2.16, §2.23.3 | Separate BPMN contracts, implementation and API consumption shapes. Every public member serves host/model/provider consumers; implementation internal sealed, same-domain test friendship only. New feature ConfigureServices is public nonsealed/virtual. |
| §2.21–2.23 | Keep valid behavior tests; actual crash/race/upgrade tests and meaningful controls; no markdown/source-text tests. Catalog/README/index changes in the same unit. Contextual domain exceptions at boundaries, preserving caller cancellation. |
| Framework §4; Elsa E5 | Additive overload/default-refusing capabilities; exact old wire/fingerprint when new material is absent. Track API deltas and compare with released baseline; computed versions, no literal project versions. |
| Elsa E2.2/E2.6 | Native DTO unchanged; new model material is runtime-only and content-addressed. Native and model artifacts retain complete pin/dependency/deletion guards. Prove a clean runtime-only consumer without Design data. |
| Elsa E2.8/E2.9 provisional; E6 | Frozen authoring stays unchanged; no generic ActivityNode child/edge fields or implicit active migration. Existing native activity catalog versions stay. Review new type names against R1–R8. |
| Elsa E7 | New real-host project has exactly one integration-tier entry; existing projects retain their tiers. Maps/discovery/test-tier checks run. |

Post-design assessment: no constitutional exception or new root-domain amendment is proposed. Public neutral contracts remain provisional as product extension promises until the real native/BPMN consumers pass. No rule is waived by the clean specification review.

## Project Structure

### Documentation (this feature)

`spec.md`, `plan.md`, `research.md`, `data-model.md`, `contracts/hosting.md`, `quickstart.md`, `review-notes.md`, then reviewed `tasks.md`. Durable runtime receipts and captures go in `evidence/`; keep concise receipts with raw source-bound assets, not repeated narrative ledgers.

### Source Code (repository root)

| Path | Responsibility |
|---|---|
| `Essentials/src/Workflows/Runtime/Core/` | Additive material/model-state/receipt/dispatch contracts and named registration event; no model semantics. |
| `Essentials/src/Workflows/Runtime/Elsa.Workflows.Runtime/` | Shared scoped selection, ownership mechanics, native adapter, model queue/commit/retention integration. |
| `Essentials/src/Workflows/Runtime/Persistence/EntityFrameworkCore/` | Model material/state/input participation, physical read validation, CAS/fences, additive four-engine migrations and provider capability. |
| `Essentials/src/Activities/Bpmn/Core/` (new) | Actual public BPMN runtime command/result/artifact/profile contracts, without interpreter dependency. |
| `Essentials/src/Activities/Bpmn/Elsa.Activities.Bpmn/` | Shared evaluation component, existing native adapter, direct model driver, human/timer/worker bindings and `RunBpmn` native adapter. |
| `Essentials/src/Activities/Bpmn/Api/` (new) | Authorized direct start/inspect/task-complete/cancel/incident-retry command/query consumption. |
| `Essentials/src/Activities/DispatchWorkflow/Runtime/` and runtime contributor contracts | Reuse/lift exact pinned native-work start, cancellation and typed terminal delivery through an explicit model/native bridge; no fabricated native parent. |
| `Essentials/apps/Elsa.Workbench/` | Compose the opt-in model hosting/API and owned qualification fixture; existing modules retained. |
| `Essentials/tests/Activities/Bpmn/Hosting/Tests/` (new) | Real-process and operational/API acceptance, integration tier. |
| Existing BPMN, Runtime, EF, DispatchWorkflow, ETL and Architecture test projects | Focused behavior/compatibility/provider coverage; retain native wire fixtures and public constructor probes. |

New packable projects justify actual dependency/deployment consumers and ship API tracking, README and relevant catalogs. Do not put a provider dependency in the new API/contract projects. `Extensions/Etl` remains unchanged unless a demonstrated integration defect requires a focused fix and its relevant tests.

## Implementation and verification order

### Gate A — actual current-writer captures

Before the first affected writer edit, build/run the baseline source in an owned host and capture normal-command-produced native/BPMN pending state, including partial parallel work, waits, owed callback and native-parent outcomes. Include Balanced/Durable where the changed path differs. For touched signal delivery, capture source before recipient selection, installed empty/populated snapshot, listener rearm and recipient acceptance before acknowledgement. For touched ledger/evaluation, include multi-instance/listener rearm, active compensation and called-workflow ordered control. Retain actual Studio-published material, export/layout and immutable pins. Record source/build/config identity and whole consistent stores; do not relabel historical #2735 assets.

Gate A is passed only when a portable manifest maps each affected writer to actual captures and baseline observations, with the frozen authoring fingerprint verified. No source edit bypasses this ordering. Existing owned hosts may be reused only with verified source/assembly/config identity; never stop the user's hosts.

### Gate B — smallest shared production seam

1. Add versioned model material through the existing artifact provider while retaining native DTO constructors, hashes, physical incarnation and closure-checked root leases. Add default-refusing provider capability. Native reads remain native; model reads never invent RootActivity.
2. Add optional model-state/input-receipt checkpoint participation and EF typed rows; all state, accepted input, consumed scheduler claim, owed outbox, marker and current fence commit atomically. Empty/absent model participation preserves the exact historical fingerprint shape. Initial model-bearing commits force durable flush; unsupported coalescing/provider routes refuse rather than drop a participant.
3. Add model dispatch selection inside the existing partition-bound scope before native lifecycle handlers. Selection follows verified immutable material or committed execution protocol, not a caller-controlled model hint. Native consumer delegates existing behavior; direct consumer uses the same mailbox, ownership service and durable queue/claims. Extract shared ownership mechanics rather than duplicating acquisition/heartbeat/release. Unknown protocol/kind fails closed, never Noop-acknowledges.
   Add one authoritative lifecycle-read seam used by dispatch/drain selection and RuntimeResumptionService. Its native adapter preserves the existing terminal-stop/purge policy. Model lifecycle distinguishes Running/WaitingIncident, TerminalPendingSettlement and TerminalSettled; the model path never projects a fake native WorkflowExecutionState. Terminal model progression is closed, but exact retained cancellation acknowledgements, result-consumption proofs, parent-delivery acknowledgements and identical command replay remain admissible for settlement. Recovery must retain/route those inputs rather than native-purge them; purge only proven obsolete progression work under the model policy, never unclassified or unavailable work. Keep execution/material/source retention until settlement ends.
4. Demonstrate native execution plus direct BPMN start/wait/resume, real restart, receipt replay and stale-owner refusal. This is the first production milestone; later gates remain required for #2898 closure.

### Gate C — real bindings and races

Implement model-owned work handles/attempts, human completion, duration timers, cancellation, incident/retry and supported inspection. Scope/tenant/authority is verified before retained-result disclosure. Preserve matching/conflicting replay and exact current invocation/attempt checks.

Technical failure policy is pinned per service binding: default FinalFault feeds the interpreter and cannot be retried after semantic terminality. Explicit HoldForRetry with a bounded attempt budget retains the exact active semantic work token/state, records failed-attempt evidence and enters nonterminal WaitingIncident before feeding a terminal fault to the interpreter. Authorized retry creates one new physical attempt/child identity for that retained work; it does not restart or revive the process. Cancellation/timer interruption can resolve the still-active work while held. Budget exhaustion follows FinalFault. Test actual direct and RunBpmn failure → restart → retry → completion, duplicate retry, immutable old failure evidence, rejected old results and final-fault retry refusal. Business errors retain their modeled disposition and do not enter this technical retry policy.

Use the same atomic state/receipt boundary for competing inputs. Extend the existing first-delivery snapshot protocol with exact model recipients and provider qualification; preserve its native cohorts/wire. A new listener cannot receive an old retry. Work committed by a valid owner may still be delivered physically after ownership change; it does not authorize a stale new decision.

Qualify both human/timer and worker/timer acceptance orders with real process restarts, obsolete results and meaningful controls. Do not infer exact model task admission from native `ExpectedBookmarkResume`, whose shape explicitly requires native activity-attempt state.

### Gate D — native service bridge and embedding

Use a real published native workflow containing `RunEtl` as the service worker. Retain the exact worker artifact pin and input/result contract in model material. Deterministic identity derives from scope/model execution/semantic work handle/attempt; retries cannot create another logical child or pipeline. The bridge validates an authoritative committed model-work source and current invocation, including at native admission; cancellation cannot race a preflight-only check and strand a new child. Extend the existing atomic checkpoint precondition mechanism as necessary so source acceptance/cancellation and native admission are ordered durably under the same Runtime store authority.

Capture typed `RunEtl` output at native terminal acceptance; workflow Completed is not sufficient to distinguish BusinessError. A native checkpoint enricher retains model-result outbox responsibility and the exact destination/attempt/pin; dispatch acknowledgement is not consumption proof. Fault/cancel paths retain their own result, incidents and owed delivery. Propagate model/parent interruption through real native cancellation to ETL cancellation, including before admission. Preserve already-terminal ETL truth and record permitted physical in-flight effects.

`RunBpmn` captures its pinned model invocation and result wait in the ordinary native checkpoint. Start/cancel are durable intents with cancellation-before-admission handling; completion is acknowledged only by exact retained native consumption or proven terminal responsibility. Direct and new embedded invocations use the same model driver. Preserve legacy BpmnProcess v1/v2 state and native child ledger through the evaluation-only extraction. Test all three native composites and pending parent delivery.

### Gate E — qualified profile, actual upgrade and Studio

Run the full scenario/outcome matrix in [quickstart](quickstart.md), resume Gate A's actual stores on the changed host, and exercise the existing Studio journey. Prove runtime-only recovery with no Design storage. Publish profile cuts and provider-specific evidence separately. Qualify capability admission-off rollback while retaining accepted-work readers/workers; old binaries are not allowed to open new write families without schema/readability checks.

Runtime migrations must preserve Gate A's applied migration IDs and content. The current `generate-module-migrations.sh` regenerates Initial and deletes old sets, so its default mode cannot be used for this upgrade. Add a narrowly selected incremental mode for Runtime model hosting through the existing tooling/context factories, retain all existing Runtime Initial files, and document/test this program's saved-execution preservation requirement. Do not globally freeze unrelated modules or mark #1976 delivered. Prove actual old-database migration plus fresh installs on each affected provider, and guard against the regeneration path deleting the retained chain.

### Gate F — review and delivery

Map every FR/SC and #2898 criterion to actual evidence. Remove unused duplication only with build/test proof and retained legacy readers. Update APIs, catalogs, README, solution/discovery/tier metadata and terminal spec status in the implementation PR. Run three independent complete-PR lenses within six rounds, fix findings in new commits, then exact-head required checks and normal protected merge under standing authorization. Verify resulting main and reconcile issue/PR/Project62. Keep #2907 and the program open for their own remaining evidence.

## Complexity Tracking

No governance exception requested. Extra surface is confined to concrete missing material/state/dispatch and the two actual native/BPMN bridge consumers. Avoid new general buses, provider umbrellas, public engine abstractions without consumers, copied checkpoint engines or repeated artifact ledgers.


### Adopted template-main compatibility

The real legacy recovery journey exposed regenerated Workflows Design Initial migrations after main931123c7e. Retain Design history as part of the saved-execution/authoring boundary, using the [EF tooling exception and narrowly scoped known-cumulative baseline bridge](../../tools/ef/README.md). Reuse the existing provider migration/history seam. Four explicit `EfMigrationIncludes` declarations record the known baseline relationship; the shared binding installs compatibility before pooling freezes options. This introduces migration metadata, without a new execution-model abstraction. Qualify both actual pre-template stores and the actual October writer's saved template/native wait, then repeat affected Studio behavior. This integration repair does not complete the release-wide #1976 policy or #2907 direct-model Studio.
