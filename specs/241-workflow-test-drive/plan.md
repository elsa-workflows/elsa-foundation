# Implementation Plan: Workflow Test Drive

**Branch**: `agent/codex/2862-workflow-test-drive` | **Date**: 2026-10-16 | **Spec**: [spec.md](spec.md) | **Issue**: #2862

## Summary
Extend existing draft Test Run with exact trigger discovery, public module drivers, prepared HTTP targets and modular Studio controls. Reuse the existing content-addressed executable, TestRun source reference, test scope and start dispatcher. Direct runs stay compatible. A real HTTP request is the start stimulus; preparation alone never dispatches.

## Technical Context
C#/.NET 10 backend; TypeScript/React Studio. Contracts in Runtime.Core, orchestration in Publishing.Api, protocol drivers in owning Activities modules. Runtime.Core never references Design or Publishing.Core. Existing process-local TestRun storage remains the baseline; prepared targets are bounded, expiring projections and fail closed after restart. No production bindings, global routes or schedules are written. No external dependency or performance target is added. Tests use focused xUnit projects, rebuilt-host REST e2e and CUA browser verification.

## Constitution Check
Loaded framework 6.1.0, Elsa 6.0.2, ADR 0080 and contributor review checklist. Public driver/editor/target contracts are legitimate third-party extension points; implementations internal. Configuration/mode discovery belongs to test control, while exact runtime dispatch is a separate contract. No activity-specific metadata is added to WorkflowDefinitionState or ActivityNode. No runtime→design dependency, provider requirement or new module identity is introduced. Services with logic are scoped; the process-local target registry is singleton for its shared bounded projection. Use normal authorization and test-scope provider fences. New APIs are additive optional request/view fields; old direct Test Run requests remain supported. Update extension catalogs and maps. Framework §2.24 is draft; Elsa §E2.8 versioning/Model X and §E2.9 are provisional, and this work does not amend them.

## Project Structure
- `Essentials/src/Workflows/Runtime/Core`: public driver/target models and dispatch contracts.
- `Essentials/src/Workflows/Runtime/Elsa.Workflows.Runtime`: bounded targets, exact dispatcher, isolated global wait filtering.
- `Essentials/src/Workflows/Publishing/Api`: discovery, input validation, prepared/start/get/stop orchestration.
- `Essentials/src/Activities/Http`: isolated ingress, real request projection and response delivery.
- `Essentials/src/Activities/Primitives` and `Scheduling`: built-in drivers through public contracts.
- `Studio/src/apps/Elsa.Studio.Web/Client/src/sdk`: public editors/actions registry and authenticated raw request seam.
- `Studio/src/essentials/Elsa.Studio.Workflows/Client/src`: reusable setup, module editors, actual results and stale snapshot handling.
- Existing domain test projects and `e2e-tests/`: behavior evidence.

## Delivery and verification
1. Core exact options/input validation and custom-driver proof.
2. HTTP prepared target and real request/response; Event and scheduling drivers.
3. Exact wait delivery/isolation/Stop fences; no generic unknown-wait bypass.
4. Studio modular controls and walkthrough.
5. Root review, focused checks, architecture/map gates, PR review request. Record actual verified feature using Screen Studio; script narration and a quiet original music bed; retain original recording and edited sources.

## Complexity Tracking
No constitution exception proposed. Optional real-time next-occurrence scheduling and virtual time remain out of initial scope. A separately managed persistent test session/executor is rejected; existing scope/reference authority governs resources.
