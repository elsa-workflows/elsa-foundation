# Host compatibility contract

This preserves existing contracts; it introduces no general public runtime API.

1. Public authoring: BpmnProcess/BpmnDecision fingerprint, Bpmn.Activities slot, Elsa authored schema and childNodeId/listenerNodeId retain their existing meanings. Studio and published descriptors are independent witnesses.
2. State: each accepted schema has a capable version-aware reader and writer. Existing v1 is not silently restamped; unreadable or ambiguous state is retained and refuses new dispatch. Any additive correlation representation and old/new reader matrix require actual checkpoint proof.
3. Callback: exact committed child execution and semantic work-token identity govern completion/fault/signal. Node ID alone, inherited metadata and first-match lookup are insufficient.
4. Combined outcomes: consume notifications in order, then fault, against successive local interpreter states and one runtime continuation. Preserve original incident/fault and the general parent-outcome receipt. An exact handle retired by an earlier notification receives no later signal callback, matching the released reference host; its terminal fault is still evaluated with exact identity. A final Fault suppresses all accumulated unissued starts and inadmissible teardowns while retaining emitted outward signals. Interrupting handling may absorb only that incident; non-interrupting handling does not erase it.
5. Binding: existing Delay/Event/PublishEvent/DispatchWorkflow/nested mappings retain their policy. Unsupported library descriptors are explicit degradation/unavailability; Unknown never qualifies publish readiness.
6. Persistence: semantic state plus owed commands and consumption identity commit through the existing checkpoint authority. Library pruning runs after required legacy correlation is restored; no host copy of semantic retention remains.
7. Retention: keep replay identities and readers needed by accepted executions. No cleanup or rollback may strand or resurrect work.

Acceptance is defined in [spec.md](../spec.md). Technical staging and proof gates are in [plan.md](../plan.md); these statements alone are not execution evidence.
