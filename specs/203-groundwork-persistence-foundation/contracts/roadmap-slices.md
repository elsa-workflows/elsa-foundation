# Contract: Groundwork Roadmap Slices

The original Persistence vNext roadmap is rewritten as Groundwork-first slices.

## G0 - Groundwork Product Definition

Maps to: new prerequisite slice.

Outcome: Groundwork has a generic product boundary, package map, workload taxonomy, manifest vocabulary, roadmap mapping, and Elsa validation boundary.

Non-scope: implementation code and provider packages.

## G1 - Core Manifest And Planner Kernel

Maps to: original S1.

Outcome: `Groundwork.Core`, `Groundwork.Relational`, and `Groundwork.Documents` can validate provider-neutral manifests and produce relational/document plans.

Validation: sample manifest produces relational and document plans; no Elsa references.

## G2 - SQLite Portable Document Store MVP

Maps to: original S2.

Outcome: SQLite supports save, load, delete, declared indexes, portable query, optimistic concurrency, and schema history for document workloads.

Validation: shared document contract tests pass on SQLite.

## G3 - Elsa Bridge And First Real Module

Maps to: original S5, moved earlier and narrowed.

Outcome: Elsa can register Groundwork, materialize startup plans, expose diagnostics, and migrate one low-risk module such as Secrets through an opt-in Groundwork-backed path.

Validation: existing behavior remains intact and provider-specific EF migrations are not required for the Groundwork-backed path.

## G4 - SQL Server And PostgreSQL Providers

Maps to: original S3.

Outcome: SQL Server and PostgreSQL pass the same document/index contract used by SQLite.

Validation: provider differences remain isolated to `Groundwork.SqlServer` and `Groundwork.PostgreSql`.

## G5 - MongoDB Provider

Maps to: original S4.

Outcome: MongoDB materializes native collections and indexes from the same manifest and portable query contract.

Validation: native indexes are created for declared indexes.

## G6 - Runtime-Defined Entities

Maps to: original S6.

Outcome: Elsa runtime-defined entity definitions and instances use Groundwork document/index storage without requiring a physical table per runtime entity by default.

Validation: published runtime entity definitions can create and query instances by declared indexes.

## G7 - Physicalization And Performance

Maps to: original S7.

Outcome: Hot storage units can opt into provider-optimized physical storage while preserving the portable default.

Validation: at least one relational provider and MongoDB support optimized physicalization.

## G8 - Runtime Evaluation And Hardening

Maps to: original S8.

Outcome: Elsa workflow runtime stores receive a go/no-go decision for Groundwork default, Groundwork with physicalization, or specialized provider.

Validation: runtime benchmarks, concurrency checks, retry diagnostics, and operational guidance exist before any runtime hot-path migration.
