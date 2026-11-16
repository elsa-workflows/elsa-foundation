# Implementation Plan: Workflow and activity testing

**Branch**: `agent/codex/3588-testing-spec` | **Date**: 2026-11-05 | **Spec**: [spec.md](spec.md)
**Input**: #3588 and #3892. Product decisions are retained; implementation is being extracted as reviewable slices. #3901 remains a backlog proposal. Component proofs do not establish complete product support or acceptance.

## Summary

Add an optional `Elsa.Workflows.Testing` domain that verifies workflow behavior against saved expectations. It owns saved cases/suites, scenario orchestration, results, report/export format and testing contributions. Publishing captures exact runnable candidates; normal runtime interprets them. Runtime gains a narrowly trusted test entry/context boundary and scoped business-clock/effect seams, without a second interpreter or dependency on test authoring.

Follow [research.md](research.md), [data model](data-model.md), [contract](contracts/testing.md) and [UX acceptance](ux-acceptance.md). Review spec → plan/contracts → tasks before production changes. First executable work is bounded normal-runtime witnesses; revise/review any disproved contract before proceeding. Staging does not defer agreed native targets, controlled time, interactions, persistence or CI.

## Technical Context

**Language/Version**: .NET 10/C#, existing React/TypeScript Studio, Python/Playwright qualification.
**Primary Dependencies**: Runtime.Core, Publishing.Core, delivered shared source revisions, mediator/module framework, module-owned HTTP/connections/secrets contracts and existing CLI.
**Storage**: Typed testing adapter over shared source revisions for authoring; separate provider-neutral run/evidence ports with memory and Workbench EF adapters. Durable history is not browser local state.
**Testing**: Risk-based behavior, persistence/concurrency and real normal-runtime tests, complete affected projects, REST e2e, packaged CLI consumer and composed Studio. Greenfield cadence for this feature is implementation followed immediately by behavior tests; changed existing behavior earns causal red/green revert controls. This does not claim framework-mandated TDD.
**Target Platform**: Combined Workbench/Studio and Linux/Windows headless CI. Preserve design-only/runtime-only deployment shapes.
**Project Type**: Optional library modules, API/provider adapter, Studio surfaces and CLI command.
**Performance Goals**: Bounded execution/settlement/inspection and pagination; no throughput/load-testing program.
**Constraints**: Exact identities, trusted tenant/backend authority, finite wall-clock lifetime, no global clock replacement, universal sandbox or unrelated paused-program activation.
**Scale/Scope**: Five P1 user stories; real native leaf/composite/nested/reusable witnesses; model-owned availability; one scenario engine/export format.

## Constitution Check

Current main pins framework v6.1.0 and Elsa v6.5.0; its v6.5.0 amendment admits Weaver, not Testing. The closed #3903 Testing amendment never landed. Consult the current Gate indexes and [review checklist](../../docs/contributing/review-checklist.md). A later production slice must review the optional Testing purpose and advance the then-current Elsa version without replacing existing admissions. This documentation slice amends neither constitution.

- §2.1/§2.6/§2.6.4: `.Core` contains models/contracts/thin guards. Separate authoring discovery and runtime execution contributions. No concrete feature dependency or dependence on registration side effects.
- §2.5/§2.11: public inheritable features with virtual configuration, scoped services, missing/duplicate provider diagnostics.
- §2.6.1: named contribution events, one aggregating handler; owner modules supply support, not a central hardcoded registry.
- §2.9/§2.10: provider-neutral CQS invariants; EF adapter supports consumer DbContext. Append-only evidence/fenced live-run transitions are separate from mutable source documents.
- §2.18: new optional domain purpose: “Verify workflow behavior against saved expectations.” Amend pinned domain list and domain-purpose reference before adding production projects in the same reviewed change. Existing `Elsa.Testing` remains a test grouping.
- §2.22/§2.23.3: real package-author extension contracts are public/API-tracked; implementations internal sealed. Update owning catalogs, root index, handlers/contributors/cadence and cross-domain notes.
- §2.21.1/§2.23.2: preserve ordinary Test Run/Test Drive behavior; risk-based meaningful behavior proofs, never documentation/source-text tests.
- Elsa §E2.2/§E2.6: Runtime references no Design or Testing implementation. Exact artifacts execute without reading drafts/history; compilation stays on authoring/publishing side. Production artifacts gain no test start publication.
- §E2.9 (provisional): cases/results remain separate from business workflow state; `ActivityNode` gains no generic children/edges. No provisional version/state-policy ratification or amendment is implied.
- §E5/§E6/§E7: normal package version line/plain names, public API tracking, solution/maps and correct test tiers; provider/child-host proofs use integration tier.

Post-design: no ratified-gate exception is proposed. The optional domain addition and actual Runtime seam require architecture/diff review during implementation; they are proposed contracts, not already accepted source behavior.

## Project Structure

```text
Essentials/src/Workflows/Testing/
  Core/                         # shared model, contributions and persistence ports
  Elsa.Workflows.Testing/        # admission/orchestration/assertions/reports
  Api/                          # authorized library/candidate/run/report endpoints
  Persistence/EntityFrameworkCore/ # typed source and durable run/evidence adapter
Essentials/tests/Workflows/Testing/{Tests,IntegrationTests}/
Essentials/src/Workflows/Runtime/Core/{Contracts,Models}/
Essentials/src/Workflows/Runtime/Elsa.Workflows.Runtime/Services/
Essentials/src/Workflows/Runtime/Scheduling/
Essentials/src/Activities/{Scheduling,Http}/
Essentials/src/Cli/Elsa.Cli/
Studio/src/essentials/Elsa.Studio.Workflows/Client/src/{testing,workflow-editor}/
Studio/tests/acceptance/
```

Create no empty provider/umbrella project. Each new production project contains its concrete responsibility. Keep `.Core` independently consumable; a separate Studio project needs dependency-cohesion evidence before creation.

## Execution design

### Capture, admission and replay

A publishing-owned candidate port reuses the compiler/content-addressed executable store/retained source references. Capture current unsaved workflow editor content without saving its business draft. A typed source-revision adapter saves/captures exact case revisions with trusted scope. Suite admission captures exact members once. Persist immutable manifest/scope/idempotency fingerprint and candidate/dependency retention before dispatch.

The code editor's unapplied JSON buffer is separate from applied designer state. Pending/invalid buffer changes block capture until explicit Apply or Reset, preserving edits and focus. Qualify autosave-disabled unsaved input capture against a readback proving the business draft was not saved or substituted.

Compatibility compares occurrence ID, model, declaring structural path and contract, allowing authored configuration changes for regression testing. Do not compare the old entire node content hash as an identity gate: that would reject every intended changed-candidate test. Record both configurations. Contract/path changes require deliberate rebind. Replay uses retained original material or reports unavailable, never current replacement content.

Owner choice: authored suites reference logical members and use their latest saved revisions by default. Admission and export each resolve/capture those revisions once with concurrency validation, then retain a fixed ordered exact-revision manifest. Later edits do not change a running run, an export or historical replay; a CI bundle never re-resolves latest on the server.

### Native occurrence entry and context

Retain the full pinned executable. A trusted explicit test-entry contract tied to `WorkflowTestScope` selects the admitted node; never overload `TriggerNodeId`, change production artifact root or copy a subtree into a standalone definition. Normal scheduler/activity pipeline starts that node and treats its completion as the selected boundary. Its composite owns ordinary child scheduling. No containing-parent invocation exists to schedule siblings/successors.

Setup provides typed workflow inputs, scope-addressed variables, explicit prior-output fixtures and optional visible input overrides. A test-only overlay supplies them during normal input materialization, with exact scope/member/producer/contract identity and fixture provenance. Do not fabricate predecessor execution history. Missing `ActivityResult` fixtures must fail before the normal resolver's optional-producer-absence null path; required missing producers already throw. Dynamic expressions declare supported context; opaque/unqualified dependencies are unavailable. Durable test context propagates to owned composite/reusable/child execution across fresh activations and async drains; ambient DI alone is insufficient.

Bounded witnesses prove root-boundary completion, lexical frames, projections, reusable closed scopes and cancellation on the full candidate. Reject test entry without trusted finite scope/manifest. Portable fields/authored metadata cannot enable it; published starts cannot use the test-only boundary.

The overlay is not limited to input snapshots. Introduce a runtime-owned test frame/capture owner, separate from activity execution rows, with qualified lexical read/write/close semantics. Normal intrinsic variable operations, invoke/resume scoped readers, rematerializing parent callbacks and configured output capture all resolve through that owner when the admitted selected entry lacks its ordinary ancestors. In particular a selected leaf inside a reusable body needs its legitimate closed capture boundary even with no enclosing invocation. Do not fabricate ancestor activity executions. Fixture producers must lie outside selected owned work; reject fixtures that shadow actual target/child results. Actual normally executed owned results remain authoritative. Variable writes and output captures affect only run-owned frames and remain observable/assertable with provenance. Parent-dependent control instructions without a meaningful isolated boundary are unavailable.

A shared typed result-reference resolver supplies both direct bindings and portable expression parameters before value/protection-policy evaluation; a binding-only override misses the evaluator's direct causal lookups. Preserve envelope protection and explicit-null/missing distinctions. Supplied iteration frames are read-only unless the normal owned activity creates a writable frame. Persist the admitted target/context identity with the normal start seed/execution header. Treat its parentless completion/fault/cancellation as the test boundary, including a legitimate zero-outcome completion; do not reinterpret an empty outcome list as an indefinitely running test.

### Effects, time and model ownership

Preflight the reachable owned work through module-owned profiles. Controlled mode admits a closed declared effect/time set with configured substitutes. Initial qualified support must include the native witnesses, supported Event/User Task actions, `Delay` through scoped timer ownership and HTTP through its ordinary module transport. Unknown CLR/script code is unavailable until a package author contributes/qualifies its profile; a pure declaration cannot erase known nested effects.

A business-clock port keys virtual time to trusted test scope; ordinary execution reads wall time. `Delay` uses test-aware timer ownership. Controlled timers are excluded from the wall-clock pump and delivered through normal bookmark resume when scoped time advances. Host leases, expiry, audit timestamps, retention, real timeout and cancellation retain `TimeProvider`. Scope/clock/effects survive fresh activations, async drains and admitted child work. Unsupported delayed mechanisms fail preflight rather than waiting in real time.

Timer due selection and claim visibility must use separate clocks: existing `RuntimeDurableTimerClaimRequest.Now` serves both concerns and cannot simply receive virtual now. Prefer a scope-owned controlled-timer store/owner whose virtual due query remains separate from wall-clock fenced claims; use normal resume dispatch/receipt semantics. Prove leap-ahead business time cannot expire/steal another worker's infrastructure claim and that the ordinary pump cannot pick up controlled timers.

Enforce ownership before `ListDueAsync`, `ClaimDueAsync` and `GetNextDueTimeAsync`, never after the wall pump claims a controlled timer. Qualify advancement between Delay's timer persistence and bookmark commit, with idempotent pending delivery rather than a lost wake-up. Clock/effect propagation uses context-aware module services: Delay resolves business time from its execution context; HTTP carries validated attempt identity in request options to the normal transport boundary and revalidates durable scope on resumed/background work. Fresh CLR activation scopes do not inherit parent-request substitutions; arbitrary constructor service replacement remains unavailable without a separately qualified activation capability.

HTTP policy is enforced before request construction, connection resolution and credential access, with complete safe interaction identity. A transport handler alone is too late because connection authentication happens first. Integration explicitly maps both the safe destination and chosen test-system connection before the ordinary resolver runs, and uses real time by default. A mixed configuration names each real/substituted boundary and is admitted only when each policy is qualified. Secret-bound input policy and unknown-constructor refusal precede activation. Delayed event publication and User Task staging also require owner-level test isolation before ordinary routing or inbox records are created.

BPMN discovery distinguishes native structural BPMN, bound task, direct model and `RunBpmn`. Advertise no model profile before model-owned test scope/run-kind propagation, native-work origin, timers and cleanup qualify it. Unsupported independent targets get precise unavailable reasons. This does not replace required native witnesses or resume paused BPMN/ETL work.

### Steps, observations and results

Server-owned serializable steps reuse exact Test Drive wait/delivery receipts through module ports. Studio editors author those values; their browser transport callback is not the headless implementation. Each step has stable operation identity, finite real deadline and state-based settlement. Fresh fixture/substitute state belongs to each attempt, including partial preparation.

Manual delivery/time operations append immutable observations. Save as case offers their supported ordered steps for author confirmation, translating runtime wait IDs into stable model-owned selectors and rejecting ambiguity. Chosen assertions keep their observation provenance without silently adopting actual values as expectations. A waiting selected composite resumed manually must save/reload/replay headlessly without another manual action.

Cancellation closes admission, reconciles root/child/detached work and tears down only attempt-owned resources. Assertion evaluation reads exact safe runtime observations and complete interaction intervals. Pending/unknown async work cannot establish non-invocation. Keep actual execution beside verdict; exploration has no verdict and asserted runs require checks. Results are append-only; live transitions fenced/monotonic. Interruption stays pending/incomplete until recovery/expiry reconciliation, never inferred pass. This is not transparent restart-resume of every case.

### API, Studio and headless

The common authorized `/workflow-tests` API owns library, capture/compatibility, run/stop, bounded history and reports. Testing-owned read/manage/run rights coexist with candidate/evidence rights. Trusted context selects tenant/backend; portable payload never does. Import validates without executing/publishing. Secrets are references with destination mapping.

Studio offers one Tests library and common Target/Setup/Steps/Assertions/Results workbench. Designer shortcuts require explicit selection and retain open setup identity. Place test/history controls outside draft-edit-only disabled fields. Lifecycle/verdict/availability/cleanup are distinct. Ordinary users see meaningful labels first; exact IDs/manifests are available as details. Follow the UX record's controls/focus/narrow/theme requirements.

Packaged `dotnet-elsa test run` uses the same admission/steps/report APIs, accepting explicit saved revisions or bundle plus candidate/dependency mapping and authentication references. No browser automation or parallel assertion engine. JSON is authoritative; JUnit is a documented projection. Exit 0 requires every required member pass; assertion failure only gives 1; any invalid/unavailable/incomplete/infrastructure/required skipped/empty/all-skipped/unasserted selection gives 2; user cancellation gives 130. Explicit optional skips remain visible. Mixed outcomes retain full details.

## Critical path and review

M0 reviewed spec/plan/contracts/UX/tasks → M1 shared revisions/results → M2 native entry/context → M3 effects/time/steps/lifetime → M4 Studio/CLI → M5 complete acceptance controls → M6 exact-head review/merge/resulting-main closeout. Develop normal-runtime vertical witnesses early rather than finishing a disconnected library first. Advertise each native profile only after executed proof. Studio and CLI are both mandatory.

Review each artifact before the next. Bounded probes may refine contracts but do not silently become production support. Keep failures/refusals/not-run results visible. Genuine product tradeoffs return to Sipke one at a time; routine choices stay delegated. One active implementation branch owns this stack.

Final review has independent correctness/standards/acceptance axes; UX review opens current-commit composed screenshots/traces and tests risky interactions. Revert/mutation controls establish claimed behavior changes. Fix findings in commits without force-push, observe current org review cap/human holds and retain every gate. No milestone closes either feature before complete combined acceptance, exact-head green checks, permitted merge and resulting-main qualification.

## Validation and resource ownership

Run touched complete projects and compare actual/listed counts; respect dotnet build slots/load. Own disposable hosts/stores/ports and preserve other sessions. No open `main is red: CI` issue existed at establishment; `29601557f` CI was queued, so base health remains unproven until completion.

Qualification covers native target kinds, workflow action/event/time, expected-fault pass/completed-but-failed controls, substitute/real integration, source CAS/reload, portable/headless parity, stale/missing context/extensions, concurrency/idempotency, fixture contamination/partial cleanup, ingress/cancel/expiry/interruption and complete Studio states. Evidence belongs on existing issues/PRs and downloadable artifacts; local paths alone are not shared acceptance.

## Complexity Tracking

Three actual consumers (Studio, CLI and module authors) justify separate production Testing ownership independent from publishing/runtime. No new global helper, universal sandbox, hidden feature dependency or support inferred from source similarity.
