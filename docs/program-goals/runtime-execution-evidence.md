# Runtime Execution Evidence

Status: active.

Area: deterministic QA verification / committed workflow-runtime evidence.

Steward(s): Sipke plus active runtime and QA architects/agents.

## Purpose

Deliver a separately composed Execution Evidence domain that records committed semantic workflow-
runtime facts and exposes them through a neutral API for deterministic automated verification. The
goal coordinates the complete multi-session path from a process-local vertical slice through durable
distributed storage and J-Test consumer conformance.

## In Scope

- The Execution Evidence `.Core`, default implementation, API, and EF Core provider modules.
- Explicit evidence sessions and propagation through asynchronous runtime boundaries.
- A governed, versioned catalog of committed semantic transition kinds.
- Checkpoint-atomic evidence intents and idempotent at-least-once materialization.
- In-memory and EF Core stores, query/wait APIs, completeness, retention, and conformance.
- Opt-in value capture, sanitization, redaction, explicit dispositions, and bounded payloads.
- Timing-independent absence and activation correctness for absent, enabled-unscoped, metadata-only,
  and value-capture compositions. Performance measurement is retired by ADR 0073 and must not run.
- Neutral consumer fixtures and a separately owned J-Test integration.

## Out Of Scope

- A Studio or other human-facing evidence UI.
- Replacing logs, metrics, traces, or Runtime inspection projections.
- A test assertion DSL inside Elsa.
- Capturing attempted or rolled-back behavior as canonical evidence.
- Global ordering across concurrent workflows, exactly-once external delivery, or cross-store ACID.
- A generalized data-classification policy engine, storage-pressure abstraction, or shared EF
  provider abstraction outside the module-owned contexts governed by Program #1665.

## Active Objectives

1. [Issue #1133](https://github.com/elsa-workflows/elsa-foundation/issues/1133) — historical closed foundation-slice objective. Its module, contract, session, deterministic
   checkpoint-intent, in-memory materialization, catalog, and API semantics remain useful input; its
   benchmark-baseline criterion is superseded and must not be scheduled or run.
2. [Issue #1134](https://github.com/elsa-workflows/elsa-foundation/issues/1134) — complete workflow, activity, bookmark, incident, checkpoint, ordering, integrity, and completeness
   coverage.
3. [Issue #1135](https://github.com/elsa-workflows/elsa-foundation/issues/1135) — add stimulus, scheduling, child-workflow, resume, timer, trigger, signal, and deduplication
   causation evidence.
4. [Issue #1136](https://github.com/elsa-workflows/elsa-foundation/issues/1136) — add state/value capture profiles, mutation evidence, selected inputs/outputs, sanitizers,
   redaction, size bounds, and value dispositions.
5. [Issue #1137](https://github.com/elsa-workflows/elsa-foundation/issues/1137) — the former
   Groundwork-shaped objective is now an EF Core durability outcome, blocked on Program #1665's
   architecture and sequencing while preserving crash/failover recovery, distributed conformance,
   and whole-session retention cleanup.
6. [Issue #1138](https://github.com/elsa-workflows/elsa-foundation/issues/1138) — publish neutral protocol/conformance fixtures and integrate them from J-Test without moving test-
   framework concepts into Elsa.

## Linked Surfaces

- [GitHub epic #1132: Runtime Execution Evidence](https://github.com/elsa-workflows/elsa-foundation/issues/1132)
- [Runtime Execution Evidence PRD](../plans/runtime-execution-evidence-prd.md)
- [Execution Evidence seam prototype](../reports/runtime-execution-evidence-seam-prototype.md)
- [Elsa glossary](../glossary/elsa.md)
- [Runtime Execution Seam](runtime-execution-seam.md)
- [Diagnostics Observability Readiness](diagnostics-observability-readiness.md)
- [ADR 0020: runtime checkpoint post-commit work](../adr/0020-runtime-checkpoint-commit-post-commit-work.md)
- [Execution Evidence ADR series](../adr/0052-execution-evidence-is-checkpoint-atomic-and-at-least-once-delivered.md)

## Current Roadmap Notes

- Use one numbered Speckit work unit and one GitHub feature issue per active objective.
- Execute the work units in order unless a later spec proves a safe dependency-independent split.
- Keep the epic/PRD as the end-state product contract; do not turn it into one mega-spec.
- Keep UI work out of the initial program. A later UI must consume the neutral API.
- Accepted [ADR 0073](../adr/0073-ef-core-is-the-only-first-party-persistence-family.md) replaces
  Groundwork durability with EF Core and retires every benchmark, timing, budget, gate, and permanent
  measurement instrument. Preserve only timing-independent correctness; do not schedule or run the
  superseded measurement work in #1133 or the former Groundwork provider direction originally
  tracked by #1137.
- Refresh relevant runtime, extension-point, architecture-reference, and feature-dependency maps after
  each implemented module slice when their inputs change.

## Drift / Review Notes

- This is a distinct domain and coordination bucket, not an expansion of Diagnostics Observability:
  execution evidence represents committed semantic facts, while observability handles telemetry.
- It consumes the Runtime Execution Seam but does not belong inside Runtime. Existing Runtime modules
  must remain free of Execution Evidence concepts.
- If work becomes test-framework-specific, keep Elsa conformance here and move the consumer adapter
  to J-Test's own tracker and repository.

## Removal or Completion Conditions

Complete this goal when the six feature outcomes are implemented and verified under their current
governing decisions, the EF Core provider proves complete evidence across restart/failover, J-Test
consumes the neutral protocol, and remaining follow-ups have moved to their normal owning domains or
been explicitly dropped. Superseded benchmark and Groundwork-provider criteria are not completion
gates and must not be run.
