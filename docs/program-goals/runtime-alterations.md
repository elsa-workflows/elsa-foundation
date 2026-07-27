# Runtime Alterations

Status: active.

Area: Workflows Runtime operator mutations and bulk alteration orchestration.

Steward(s): Sipke plus the runtime-alterations control room.

## Purpose

Deliver a durable, auditable, extensible alteration surface for applying authorized operator changes
to existing workflow executions without bypassing the runtime's single-writer, checkpoint, identity,
or artifact-pinning invariants.

This bucket coordinates the alteration substrate and its initial built-ins. Detailed requirements
belong in the linked Speckit work unit, architectural decisions belong in ADRs, and runtime meanings
belong in the glossary.

## In Scope

- Durable alteration plans, immutable target capture, per-execution jobs, results, cancellation, and
  retry reconciliation.
- Single-execution and query-selected bulk targeting.
- The initial cancel-workflow, schedule-activity, reschedule-activity, modify-variable, and
  workflow-migration alterations.
- Stable schema-versioned alteration envelopes and trusted host-contributed handlers.
- Runtime API, authorization, audit, persistence providers, conformance tests, and backend end-to-end
  evidence required to ship the surface.

## Out of Scope

- A synchronous server-side `run` endpoint.
- Client-supplied executable code, persisted CLR handler identities, or arbitrary activity inputs.
- Implicit recovery of terminal workflows.
- Active-execution migration or caller-authored migration mappings in the initial contract.
- Studio UX unless it is selected as a later objective.

## Active Objectives

1. Deliver [issue #1016](https://github.com/elsa-workflows/elsa-foundation/issues/1016) through the
   Runtime Alterations Speckit work unit and a reviewed pull request.
2. Establish the durable plan/job orchestration and persistence contracts with provider conformance
   evidence.
3. Ship the initial built-in alteration handlers through the workflow actor and checkpoint boundary.
4. Expose authenticated submit, inspect, page-results, and cancel APIs with backend end-to-end
   coverage.
5. Route any post-delivery expansion, such as active migration, explicit mapping, or Studio UX, into
   a bounded follow-up objective or another owning bucket.
6. Complete four-provider runtime-alteration store evidence for the additive 35th Groundwork
   coverage-ledger row without mutating the immutable all-34 composition artifact.

## Linked Surfaces

- [Runtime alterations ADR](../adr/0049-runtime-alterations-use-snapshotted-atomic-jobs.md)
- [Runtime alterations Speckit work unit](../../specs/141-runtime-alterations/)
- [Runtime Execution Seam](runtime-execution-seam.md)
- [Elsa glossary](../glossary/elsa.md)
- [Elsa constitution](../../.specify/memory/constitution.md)
- [Framework constitution](../../.specify/memory/constitution-framework.md)

## Current Roadmap Notes

- Keep the initial delivery on one feature branch and expose one durable submit path.
- Target capture completes and seals before execution begins; execution then proceeds with bounded
  concurrency and no fixed API target-count limit.
- Treat the constitutions as draft quality gates where their provisional status affects planning.
- The implementation is under review in
  [draft PR #1080](https://github.com/elsa-workflows/elsa-foundation/pull/1080) from
  `codex/1016-runtime-alterations`.
- The remaining draft gates are the four-provider admission smoke/evidence for the additive 35th
  Groundwork row and a complete solution test run without the unrelated external SQL Server
  diagnostics conformance timeout.

## Delivery Evidence

- Full solution build: succeeded with zero errors.
- Focused alteration tests: Runtime 83/83, Runtime API 21/21, and Groundwork 15/15.
- Full architecture suite: 316/316.
- Backend evidence: alteration plans, replay/restart, runtime writes 10/10, instance paging,
  suspend/resume, typed variables, fault activity, and restart recovery all passed.
- Static review: diff whitespace, OpenAPI YAML, PowerShell AST, JSON documents, public-payload
  leakage, persisted handler identity, and project-reference checks passed.
- Independent bounded re-review confirmed that the seal/cancel race, Groundwork retry signal,
  read-time payload decryption, page-size bound, and operational migration suspension findings are
  resolved.

## Drift / Review Notes

- Do not turn this into a general runtime-operations bucket. Incident resolution, workflow recovery,
  ordinary retry/resume, and runtime execution architecture retain their existing owners.
- Move reusable meanings, gates, or workflows to their canonical glossary, constitution, or skill
  homes instead of growing this planner.

## Removal or Completion Conditions

Complete this bucket when issue #1016's durable alteration substrate and initial built-ins are merged
with provider and backend end-to-end evidence, and every remaining alteration follow-up is completed,
moved to another bucket, or explicitly dropped.
