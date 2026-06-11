# Quickstart: Groundwork Persistence Foundation

G0 is a planning slice. Use these checks to validate the artifacts before starting G1 implementation.

## 1. Validate Generic Boundary

Read:

- [spec.md](./spec.md)
- [contracts/groundwork-boundary.md](./contracts/groundwork-boundary.md)
- [data-model.md](./data-model.md)

Expected result:

- Generic concepts are named under Groundwork.
- Elsa-specific concepts are confined to the Elsa validation bridge.
- No `Groundwork.*` package is expected to reference an `Elsa.*` package.

## 2. Classify Roadmap Candidates

Use the workload model in [data-model.md](./data-model.md) to classify these candidates:

- Secrets.
- Activity catalog.
- Workflow definition metadata.
- Runtime entity definitions.
- Runtime entity instances.
- Workflow executable artifacts.
- Bookmark state.
- Durable value state.
- Scheduler state.
- Runtime checkpoint commits.
- Execution log.
- Queue backlog.
- Outbox records.
- Distributed locks.

Expected result:

- Metadata/configuration, catalog/authored data, and runtime-defined business data classify as Groundwork default candidates.
- Runtime continuation state classifies as benchmark-gated.
- Execution logs, queues, outbox records, and distributed locks classify as specialized-provider candidates by default.

## 3. Check Roadmap Mapping

Read [contracts/roadmap-slices.md](./contracts/roadmap-slices.md).

Expected result:

- Every original roadmap slice S1-S8 has a Groundwork-first equivalent.
- Original S5 is moved earlier as G3 and narrowed to an Elsa bridge plus one low-risk module.
- Runtime evaluation remains the final hardening decision instead of an assumed migration.

## 4. Confirm G1 Readiness

G1 can start when these are true:

- The product boundary is accepted.
- The package map is accepted.
- The workload taxonomy is accepted.
- The minimum manifest vocabulary is accepted.
- The roadmap slice mapping is accepted.

If any item is rejected, update G0 artifacts before implementation work begins.
