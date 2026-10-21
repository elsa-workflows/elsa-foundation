---
status: accepted
date: 2026-10-21
---

# Rules owns its execution domain

Status: Accepted after the bounded planning review on 21 October 2026 under Sipke Schoorstra's standing delegated architecture/refactoring/self-review authority. No multi-architect vote is asserted. Implementation unit: [#3181](https://github.com/elsa-workflows/elsa-foundation/issues/3181), [spec 999](../../specs/999-durable-rules/spec.md), under [Program #2892](https://github.com/elsa-workflows/elsa-foundation/issues/2892). The program lead adopts this narrow domain decision under that delegated authority; runtime/package/provider qualification remains pending.

## Decision

Admit optional `Elsa.Rules`, with the purpose **“Executes finite rule inference with durable round progression.”** Its initial language is finite positive propositional inference, not a general-purpose rule authoring product. Rules owns its immutable artifact, invocation identity, model revision, round receipts, committed facts, cancellation and terminal delivery. Native Sequence and Flowchart retain their own composition semantics.

The Rules model has its own durable boundary. It runs directly or through an ordinary native CLR adapter that uses the existing immutable publication, checkpoint outbox and bookmark-resume contracts. Rules state does not enter native checkpoint participants; no cross-store atomic transaction is claimed. The fourth model will demonstrate extensibility through explicit modules and existing infrastructure rather than adding a universal engine registry or model-name switch to the workflow runtime.

## Why a distinct domain

`Elsa.Pipelines` composes middleware; finite inference is a different responsibility. Native composites own their child orchestration. Attaching rule rounds to a native composite or to a per-rule actor would entangle those semantics. As with [ETL](0087-etl-owns-its-execution-domain.md), shared durable mechanics do not require shared model state or one scheduling algorithm.

Hosting selects where a run's work executes. Rules persistence owns authoritative transitions. This leaf does not select an actor framework, create a second membership service or implement the later general task-runner/pause product. A future worker host can gate new claims without changing accepted model state; physical work already running may complete. No workflow-to-dedicated-queue mapping is assumed.

## Concrete packages and consumers

- `Elsa.Rules.Core`: immutable language/artifact/invocation/pin/result shapes and direct runtime contracts used by hosts and native adapters; no inference policy, EF or design storage.
- `Elsa.Rules.Runtime`: finite evaluator, complete round validation, direct facade and worker progression through explicit persistence contracts.
- `Elsa.Rules.Persistence.Core`: separate CQS store/reader contracts, ownership/receipt/transition confirmations and validation seam consumed by Runtime and EF.
- `Elsa.Rules.Persistence.EntityFrameworkCore`: Rules-owned contexts, migrations and transitions for SQLite, PostgreSQL, SQL Server and MySQL.
- `Elsa.Rules.Workflows.Design`: immutable Rules artifact/semantic pin contribution during normal native publication.
- `Elsa.Rules.Workflows.Runtime`: ordinary native activity, checkpoint responsibility, result delivery and durable parent-proof verification; no workflow Design dependency.

Each planned module has a concrete intended consumer exercised by the acceptance cases. No empty provider umbrella, general operator UI or new API product is admitted. Runtime consumes frozen artifact and installed deployment identity; mutable design storage is unavailable during recovery acceptance.

## Guarantees and qualification

Accepting a complete round receipt and consuming it are distinct atomic model transitions. The validator recomputes all applicable firings; consumption alone advances facts and produces one next round or terminal result. The closure round adds no facts. Positive lower budgets can fault after their last adding round; they cannot claim convergence.

Cancellation before admission shares the same unique scoped identity; cancellation after admission preserves committed facts and rejects late advancement. Worker lease changes and semantic model revision remain separate. Terminal native delivery is retired only with durable consumption/equivalence/parent-terminal proof, never enqueue acknowledgement. Physical attempts can repeat; this is no exactly-once external-effect claim.

Acceptance requires actual computed-version packages in a clean PackageReference consumer, original semantic asset pins, separate PostgreSQL processes, real persisted Sequence/Flowchart continuation, all four real database engines and additive upgrade/rollback proof. SQLite or source-host tests alone do not qualify this leaf. Package identity is not publisher authenticity. Unsupported deployment pins preserve recoverable responsibility rather than silently refusing or repinning old work.

## Amendment scope

Adoption adds only the Rules domain name and purpose, with the Elsa constitution minor update 6.1.0 → 6.2.0 and its sync-impact report. Framework stays at 6.1.0. Existing domain purposes, defaults and native contracts stay governed by their existing rules. Draft/provisional sections are not ratified by this change. The owner's delegated decision authority is recorded explicitly instead of claiming the normal multi-architect consensus occurred.
