# Implementation Plan: Direct BPMN journeys in Studio

**Branch**: `agent/codex/2907-bpmn-studio-recovery` | **Date**: 2026-11-01 | **Spec**: [spec.md](spec.md)

## Summary

Make the delivered direct BPMN host usable through Studio. First deliver ordinary authoring/publication and durable discovery of start → human wait → host restart → completion. Then connect the existing published native RunEtl worker, retained failure and explicit retry. Finally add exact human claiming and interruption controls. Existing native authoring remains a separate supported publication path.

Use the current BPMN canvas, workflow drafts/versions, model runtime, mailbox/checkpoint receipts and ETL UI. No second interpreter, scheduler, generic execution console or editor. Source-bound decisions and actual pre-change checkpoints are in [research.md](research.md) and [evidence/preclaim/README.md](evidence/preclaim/README.md).

## Technical Context

**Language/Version**: Repository .NET SDK 10.0.300, C#, existing TypeScript/React toolchain.

**Primary Dependencies**: Released BPMN 0.5.0 packages; existing NativeEndpoints, CShells, scoped Studio SDK HTTP/capability/navigation services, React Flow. No new third-party dependency or bpmn-js adoption.

**Storage**: Existing separate Design and Runtime stores. Immutable source references/material, model state and input receipts; no new claim table or global execution catalog. Whole SQLite stores provide owned-host upgrade evidence; existing provider contracts stay unchanged.

**Testing**: xUnit, existing domain fixtures, Vitest/component checks and actual owned Workbench/browser journeys. Per-implementation/registration checks apply. For this work unit, run a failing control before behavioral regression fixes and focused checks alongside implementation; complete real acceptance before review. Elsa has no global test-cadence gate; this is a local delivery choice, not an amendment.

**Qualification cadence (owner-authorized 1 November)**: Use focused behavioral and affected-project checks during fixes, actual browser/host proof at completed-journey milestones, and full compatibility/architecture/API/package/independent review/required CI on the stable delivery candidate. Batch pushes at those milestones. Preserve a source/hash record for each result; a changed state format or shared runtime contract immediately reopens its affected compatibility checks. Capture actual legacy/pre-claim writers before changing them. Reuse unchanged product builds during harness-only diagnosis, recording the executing product source separately from the harness digest. Final delivery still requires the exact reviewed head and resulting main; no check, protection or human hold is bypassed.

**Target Platform**: Existing ASP.NET hosts and desktop/mobile browsers, including runtime-only/admission-off compositions.

**Project Type**: Additive domain API, runtime commands and Studio UI within existing feature packages.

**Performance Goals**: One bounded underlying store page per catalog request, default50/max500 rows examined. No unbounded filtering/fill loop or whole-store scan. User-controlled Load more can receive an empty authorized page with continuation.

**Constraints**: Preserve frozen authoring and old request fingerprints. Authorize before disclosure/replay. Old readers refuse claim-bearing material/state. Small verified commits, serial affected builds, no repeated unchanged matrices.

**Scale/Scope**: Supported direct profiles only. Separate human and service fixtures; no arbitrary mixed human→ETL graph. Full operator console, assignment teams, timed leases, arbitrary token alteration, live migration, Orleans and task-runner follow-up stay deferred.

## Constitution Check

Both constitutions and the contributor checklist were applied before research and again after design. Framework §2.24 and Elsa activity-versioning/Model X/§E2.9 draft or provisional material is not ratified by this plan.

| Gate | Design result and implementation check |
|---|---|
| Framework §§2.1–2.6; internal by default | Existing BPMN Core replacement port and implementations. No heavy Core package, cross-domain internals grant or general framework. Services scoped, ConfigureServices virtual, registration/per-implementation tests required. |
| Elsa §E2.2 | Runtime/API discovery uses runtime readers and immutable material. Optional direct publisher lives in existing Design-bearing BPMN Interchange, reading Design.Persistence.Core; runtime packages gain no Design reference. Prove runtime-only recovery. |
| Persistence/CQS/exceptions | Commands use existing mailbox and atomic checkpoint+receipt transaction. Reads project authorized state. No direct insertion of versions or new provider. Retain contextual domain exceptions and infrastructure causes. |
| Authoring/semantics | Versioned bindings are authored element properties, never layout. Native childNodeId keeps its meaning. Mechanical projection delegates validation/semantics to existing host/released engine. |
| Public API/versioning | Preserve constructors, legacy methods/payloads; additive replacement members default-refuse. Track PublicAPI and actual magnitude/ApiCompat. PR3410's 4.0 exception is not reused; actual version gate must pass before merge. |
| Tests/tier/build | Existing unit/integration tiers; child-host/browser acceptance in existing integration tooling. Affected architecture/composition/API checks and exact reviewed-head required CI. |
| Governance | Spec → reviewed plan → reviewed tasks before implementation. Exact claim6556217536, existing PR3603. Required checks, human holds and branch protection remain binding. |

**Post-design result**: No known constitutional violation or unresolved product-policy question. These are design constraints, not claimed passing tests. Tasks must exercise them before merge.

## Project Structure

```text
specs/1184-direct-bpmn-studio/
  spec.md, plan.md, research.md, data-model.md, quickstart.md, tasks.md
  contracts/{authoring-publication,runtime-operations,studio-navigation}.md
  evidence/preclaim/{README.md,capture.py,receipt.json}
Essentials/src/Activities/Bpmn/
  Core/                         # compatible commands, publication context, inspection DTOs
  Elsa.Activities.Bpmn/Internal/ # existing driver, source-aware publication, claim state
  Api/                          # authorized runtime catalogs, inspection and claims
  Interchange/                  # XML retention; optional authored direct publisher
Essentials/tests/Activities/Bpmn/{Interchange/Tests,Tests,Hosting/Tests}/
Studio/src/essentials/Elsa.Studio.Workflows/Client/src/
  bpmn/, api/, workflow-editor/  # reuse editor; publication/catalog/run UI
Studio/src/essentials/Elsa.Studio.Hosting/Client/src/{sdk,app}/
Extensions/Etl/
  src/Workflows/Runtime/         # authorized RunEtl identity state-view
  studio/Elsa.Studio.Etl/Client/src/ # selector, durable run links/contribution
tools/bpmn/                     # owned-host collectors and receipts
```

**Structure decision**: Existing Design-bearing interchange package gets a separately composed optional publishing feature. A new publishing project is unnecessary. Keep Design reads out of runtime. Capability wiring exposed a real contract dependency: lift the existing API capability declarations and thin registration into `Essentials/src/Api/Capabilities/Core`, preserving old API callers through forwarding; do not add another capability mechanism or make BPMN depend on its implementation. ETL supplies a contextual run-link contribution; Workflows does not parse ETL private state.

## Delivery sequence

1. Preserve actual pre-claim wait/completion stores. Fix/test direct-binding JSON/XML retention; old documents without metadata export unchanged.
2. Project an immutable saved root BpmnProcess version through explicit direct publication. Validate before admission; preserve trusted source/version/layout and replay conflicts. Keep native publishing.
3. Add authorized paged process/execution discovery and truthful inspection, then Studio publication/start/work. Prove P1 with actual restart/fresh browser, no scripted run ID.
4. Requalify both frozen native Studio families and runtime-only controls as a P1 gate.
5. Add service-workflow selection and ETL navigation/held-failure retry; retain pins, original failures and physical effects.
6. Add explicit claim-required material/state, claim commands/capabilities/UI and old-reader refusal. Capture actual P1 checkpoints before this next writer change.
7. Complete interruption/settlement, affected qualification, documentation and independent full-PR review; exact-head gates before merge, resulting-main verification afterward.

## Complexity Tracking

No exception requested. The sole new shared UI seam is a contextual run-related-link contribution needed for demonstrated BPMN→native→ETL navigation. It carries identities and authorized links, not execution semantics.
