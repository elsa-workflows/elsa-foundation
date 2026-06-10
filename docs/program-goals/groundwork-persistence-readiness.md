# Groundwork Persistence Readiness

Status: active.

Area: provider-neutral persistence framework / Elsa validation bridge.

Steward(s): Joey plus active architects/agents.

## Purpose

Create a focused coordination bucket for extracting the Persistence vNext idea into Groundwork, a generic provider-neutral persistence framework that is validated inside Elsa Foundation before it moves to a standalone repository.

This bucket keeps generic Groundwork framework work separate from Elsa-specific persistence migrations and from workflow runtime hot-path decisions.

## In Scope

- Groundwork product boundary, package map, and extraction readiness.
- Provider-neutral storage manifests, storage units, workload classification, provider capability reports, materialization plans, and schema history.
- Portable document storage and declared-index query semantics.
- Provider packages for SQLite, SQL Server, PostgreSQL, and MongoDB.
- Elsa bridge/integration work that validates Groundwork through real Elsa stores.
- Runtime-defined entity storage once the generic document/index contract is proven.
- Physicalization and performance evaluation.
- Runtime-store go/no-go evaluation where Groundwork may or may not be appropriate.

## Out Of Scope

- Moving Groundwork to a standalone repository before Elsa validation proves the boundary.
- Treating workflow runtime hot paths as automatic Groundwork migrations.
- Folding queues, execution logs, outbox records, timers, or distributed locks into ordinary document storage without benchmark evidence.
- Adding Elsa domain concepts to generic Groundwork packages.
- Replacing existing Elsa persistence paths without an opt-in migration plan.

## Active Objectives

1. Complete [Groundwork Persistence Foundation](../../specs/012-groundwork-persistence-foundation/plan.md) as the G0 product-definition slice.
2. Complete [Groundwork Core Manifest And Planner Kernel](../../specs/013-groundwork-core-manifest-planner/plan.md) as the G1 implementation slice.
3. Implement [Groundwork SQLite Document Store](../../specs/014-groundwork-sqlite-document-store/plan.md) as the G2 provider validation slice.
4. Preserve the original Persistence vNext roadmap by mapping each slice to a Groundwork-first execution slice.
5. Keep Elsa validation in an explicit `Elsa.Persistence.Groundwork` bridge.
6. Defer runtime hot-path migration until benchmark and concurrency evidence exists.

## Linked Surfaces

- [Groundwork Persistence Foundation spec](../../specs/012-groundwork-persistence-foundation/spec.md)
- [Groundwork Persistence Foundation plan](../../specs/012-groundwork-persistence-foundation/plan.md)
- [Groundwork boundary contract](../../specs/012-groundwork-persistence-foundation/contracts/groundwork-boundary.md)
- [Groundwork roadmap slices](../../specs/012-groundwork-persistence-foundation/contracts/roadmap-slices.md)
- [Groundwork Core Manifest And Planner Kernel spec](../../specs/013-groundwork-core-manifest-planner/spec.md)
- [Groundwork Core Manifest And Planner Kernel plan](../../specs/013-groundwork-core-manifest-planner/plan.md)
- [Groundwork SQLite Document Store spec](../../specs/014-groundwork-sqlite-document-store/spec.md)
- [Groundwork SQLite Document Store plan](../../specs/014-groundwork-sqlite-document-store/plan.md)
- [Runtime Execution Seam](runtime-execution-seam.md)
- [Workspace Split Readiness](workspace-split-readiness.md)

## Current Roadmap Notes

- G0 defines the product and planning boundary only.
- G1 adds generic Groundwork core/planner packages and tests before any Elsa store migration.
- G2 adds the first provider-backed portable document store using SQLite.
- G3 should validate one low-risk Elsa module before broad provider expansion continues.
- Runtime continuation state remains benchmark-gated; operational streams remain specialized by default.

## Drift / Review Notes

- If work becomes mostly workflow runtime architecture, route it through [Runtime Execution Seam](runtime-execution-seam.md).
- If work becomes mostly repository extraction mechanics, route it through [Workspace Split Readiness](workspace-split-readiness.md).
- If a Groundwork rule becomes a general framework quality gate, move it to the constitution and leave a link here.

## Removal or Completion Conditions

Complete or pause this bucket when Groundwork has either moved to its own repository, been rejected as a generic extraction, or reached a stable Elsa-validated provider/document-store foundation with remaining work tracked in implementation-specific specs.
