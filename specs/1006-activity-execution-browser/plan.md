# Implementation Plan: Activity execution browsing

**Branch**: `agent/codex/3375-activity-executions` | **Date**: 2026-10-26 | **Issue**: #3375

## Summary
Implement the approved generic execution navigator, child browser and exact execution scope. Keep the existing pinned canvas and evidence inspector. Generic means executable-node identity and recorded ancestry, never an activity-type allowlist.

## Technical Context
TypeScript/React Workflows module; .NET 10 Runtime API and existing in-memory/EF inspection stores. Reuse Studio controls/tokens and existing exact inspection authorization. No new project, dependency, feature identity, writer/scheduling behavior or persistence schema. EF provider-specific read-only JSON/recursive SQL supports SQLite, PostgreSQL, SQL Server and MySQL without losing historical runs.

## Constitution Check
Framework v6.1.0 and Elsa v6.3.0 gate indexes and relevant review rules read. CQS: collection reads never mutate. Runtime remains independent of Design. New query models belong to Runtime Core; concrete query execution stays in implementation/provider projects. Track changed public APIs and test affected implementations. No draft/provisional rule is relied upon or ratified. Apply risk-based behavior tests, including causal red/green; this extends existing code rather than selecting a greenfield cadence.

## Design
- Extend the inspection store with an additive bounded collection query/result. Filter by executable node, exact parent and arbitrary ancestor; order by UTC scheduled ticks, sequence, ordinal execution identity. Return matching count, position, previous/next exact IDs and bound page continuations.
- Implement the in-memory provider over its locked projection set. EF uses provider-aware parameterized JSON extraction and recursive ancestry on existing inspection rows, then decodes only the selected bounded rows with existing validation. Existing summary/descendant semantics remain unchanged. No migration/backfill is necessary.
- Runtime API authorizes the run before querying and binds continuations to access profile, tenant, run and predicates. Advertise `activity-executions`; reuse exact inspection for values. Unsupported hosts retain exact inspection with an explicit browsing-unavailable state.
- Workbench owns exact selected execution and ancestor scope independently of authored canvas frames. Generic parent breadcrumbs and child lists choose scope. Canvas selection, overlays and evidence use only scoped executions; absent scope evidence never falls back to another occurrence. Timeline/deep-link navigation resolves exact parent ancestry.
- Add a compact navigator under the selected activity header and expandable list; child groups show repeated pinned nodes, with iteration labels only from provenance. Latest-in-scope default; explicit selection persists on refresh. Abort and identity-guard requests.

## Project Structure
Runtime Core/API/provider paths under `Essentials/src/Workflows/Runtime/`; run workbench and new browser components under `Studio/src/essentials/Elsa.Studio.Workflows/Client/src/`; affected existing Runtime and Workflows test projects.

## Validation and Delivery
Tasks follow story order with behavioral regressions, affected whole test projects, frontend checks, architecture/maps checks, and rebuilt-host browser proof. Required acceptance includes existing Looping body4/10, generic back-edge/nested/parallel/reusable contexts, pagination/ties, sensitive/tenant isolation, stale requests, narrow/keyboard/light/dark. Recheck competing claims before committing. Open draft PR with evidence and leave merge to the review flow; no merge requested.
