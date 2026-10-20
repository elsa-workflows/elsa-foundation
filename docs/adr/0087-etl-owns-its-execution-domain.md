---
status: proposed
date: 2026-10-20
---

# ETL owns its execution domain

Status: Proposed; domain-list amendment awaits ratification. Implementation unit: [#3109](https://github.com/elsa-workflows/elsa-foundation/issues/3109), [spec 255](../../specs/255-finite-file-etl/spec.md), under [Program #2892](https://github.com/elsa-workflows/elsa-foundation/issues/2892).

## Decision proposed

Add `Elsa.Etl` to the pinned Elsa domain tree, with the purpose **“Executes data transformations over independently recoverable partitions.”** Ship it as an optional capability under `Extensions/Etl`, with its Studio companion under that same capability. A host can install ETL without choosing an optional actor provider.

ETL owns its authored definition, immutable runnable artifact, run, partition cursor, bounded batch, output acceptance and terminal result. Its native workflow adapter uses the existing checkpoint outbox and bookmark-resume contracts. Native Sequence and Flowchart retain composition semantics; ETL does not introduce records or partition rules into those composites. No workflow or actor is created per record or batch.

The first implementation is finite CSV-to-JSONL with a pinned local lookup. It does not stabilize a universal execution API, qualify another actor provider, or complete the wider ETL feature.

## Why a distinct domain

`Elsa.Pipelines.Core` composes middleware. Its pinned purpose and current implementation do not describe datasets, cursors or connector effects. Nesting ETL under `Elsa.Pipelines.Dataflow` would give one root two unrelated meanings. Treating ETL as a native composite would instead entangle record processing with workflow activity semantics.

The proposed domain separates those responsibilities while sharing durable infrastructure through existing contracts. The runtime consumes an immutable ETL artifact and runtime configuration, never a mutable draft or Studio layout. Feature-specific contracts remain small; provider algorithms and orchestration policy remain internal.

## Package boundaries

- `Elsa.Etl.Core`: immutable artifact/schema shapes and independently named design, runtime and store contracts needed by demonstrated consumers. No parser, policy, EF dependency or mutable-state execution path.
- `Elsa.Etl.Design`: definition validation and publication.
- `Elsa.Etl.Runtime`: finite-file execution, bounded partition work and result delivery.
- `Elsa.Etl.Persistence.EntityFrameworkCore`: durable stores and schema, using the existing EF persistence family.
- `Elsa.Etl.Api`: authorized definition/run commands and queries, with authoring and runtime routes enabled independently.
- `Elsa.Etl.Workflows.Design` and `.Runtime`: publication pinning and native activity invocation respectively; neither makes ETL runtime depend on workflow design.
- `Elsa.Studio.Etl`: authoring and run/partition inspection.

Each package has an actual consumer or deployment boundary. Add a separate connector package only when a real connector dependency or independently selectable implementation requires it. Add no generic foundation package, provider umbrella or placeholder implementation.

## Consequences and limits

ETL state does not enter the workflow checkpoint's typed state-change participants. An ETL-owned enricher adds admission responsibility to the real bookmark-created checkpoint; an idempotent ETL admission transaction and ETL terminal outbox bridge the two stores. Cancellation records an idempotent tombstone even when it arrives before admission.

Physical file effects and a relational cursor cannot share one atomic transaction. Deterministic chunk identities and content checks make matching replay converge; a stale worker cannot make output authoritative or advance state. A physical write already in progress may finish. Lease expiry never implies that a live task released its buffers.

Ratification adds the domain entry and purpose, with the normal Elsa constitution version/report update. It changes no existing domain's purpose, workflow semantics, provider default or frozen wire contract. The proposal is not recorded as accepted merely because the broader ETL program is authorized.
