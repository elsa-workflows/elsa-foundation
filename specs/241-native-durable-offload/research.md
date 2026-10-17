# Source decisions

Baseline: `15feed9e0be709348602e054cca85a33f0f76873`, inspected 2026-10-17 by root and three read-only discovery agents.

| Decision | Evidence | Alternative rejected |
|---|---|---|
| Keep coordinator single-writer | `WorkflowSchedulerDrainer` awaits each dispatch; ADR 0031 and 0078. | Parallelizing the scheduler would race native structural state and a scoped EF context. |
| New worker ledger, existing EF transaction | `EfRuntimeCheckpointCommitStore` stages participants and immutable marker in one fenced transaction; queue claims are per-workflow FIFO. | Direct worker dequeue of coordinator work. |
| Do not execute inside generic outbox handler | `RuntimePostCommitOutboxProcessor` awaits handler and does not renew during delivery; claim follow-up is tied to DispatchWorkflow failure. | Long-running handler or repurposing dispatch-specific follow-up. |
| Retain ordinary activity identity | `DispatchWorkflow` can already parallelize separate executions but requires child artifacts and contracts. | Wrapping configured activities as hidden child workflows does not prove the requested same-workflow activity placement. |
| Pinned capability and placement | `ExecutableNodeCompiler` sees CLR activity type and contract; `WorkflowExecutableHasher` has two explicit node hash projections and ignores arbitrary Metadata. | Unhashed metadata flags or treating SideEffectProfile as background-safety. |
| Reconstruct pinned typed result | `ActivityCompletionProjector` uses Web JSON; `$` output captures use actual transition.Result; `TypeReferenceFactory` and `IWellKnownTypeRegistry` resolve contract types. | Worker-supplied CLR type names or a JsonElement pretending to be every TResult. |
| Separate result dedup from continuation dedup | `ActivityAttemptActivationClaimer` has a per-child parent completion marker; #2232 and `WorkflowCompleteActivitySchedulerWorkHandler` show a separate late cascade replay gap. | Assuming a consumed queue item is a durable tombstone. |
| Real process recovery | `_ServerLifecycle.ps1` owns/restarts Workbench against the same DB; `HttpEndpointHostFixture.StartDurableSqliteAsync` is the production EF fixture. | Experimental raw SQLite proof or graceful-only in-process fixture. |

## Independent spec review

Reviewer `proof_criteria` requested explicit atomic acceptance, expiry-only rejection, competing coordinator fences and downstream completion replay controls. All are incorporated in FR-004/007/013 and the lifecycle contract. No product clarification is pending. Provider/code details are subject to implementation review and evidence.

Independent design review accepted the boundary and requested two protocol clarifications: acknowledge an identical already-committed receipt before applying lease-expiry rejection to new writes, and retain the fixed coordinator acquisition epoch rather than adopting a later owner's token. Both are incorporated in the contract. The full native completion-chain replay mutation remains required.
