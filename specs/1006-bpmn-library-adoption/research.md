# Adoption decisions from the existing parity checkpoint

This is bounded implementation design for #2735 part 2, not a restarted parity investigation.

| Decision | Rationale | Alternative rejected |
|---|---|---|
| Use qualified released 0.5.0 | Part1 comparison and owner continuation select it. | Moving main, old 0.2/0.3 assumptions or a new interpreter copy. |
| Keep Elsa authoring envelope | Studio, runtime inspection and published artifacts depend on its existing child references and schema. | Retyping directly to library authoring roots under the same Elsa schema. |
| Capture actual checkpoints first | The old writer is the only trustworthy source of its real execution state and race order. | Synthetic typed-state fixtures or equal test totals. |
| Keep version-aware state I/O | Direct read loses activeChildren and producingActivityExecutionId; canceled tokens also need exact stopped handles before pruning. | Renaming fields and treating readable JSON as safe continuation. |
| Continue v1 in its declared representation | Spec242 forbids implicit conversion of active executions. A field-alias projection can preserve the representation while using library state internally. This remains subject to capture/recovery proof. | Restamping every read as v2, refusing all existing state, or keeping a second semantic engine. |
| Fold combined outcomes locally | Existing runtime already owns atomic parent-outcome identity. Released notifications need a live reporting handle; terminal faults require it removed. | Separate durable signal/fault items or two host callbacks against stale parent state. |
| Reuse library pruning and diagnostics | Token retention is semantic knowledge; the selected release already supplies it. | Another host retention policy or pruning before correlation recovery. |
| Preserve explicit host capability limits | A library binding discriminator does not make a Delay/Event mapping or Studio field exist. | Dropping new descriptors while advertising availability. |

Open engineering proof gates, not owner-policy questions: actual queued-loser order, deferred-fault capture, complete historical correlation, v1 writer/read-back behavior, combined-notification command reconciliation and real Studio behavior. Failure at a gate requires design correction before writer deletion.

Plan review corrections: capture same-node listener re-arm history; retire exact reporting handles between notifications as the released reference host does; use verdict-aware command reconciliation because a later interpreter call cannot withdraw starts held in an earlier call. These are required real-host witnesses before accepting the batch adapter.

## Called-workflow source findings

Actual existing CallActivity HTTP baseline passes waited completion and catch-all error handling. DispatchWorkflow.ResumeAsync completes its leaf normally with a Faulted outcome; its result diagnostics have a fixed safe allowlist. The local ParentNotificationEvaluation refuses activity roots, and the existing combined child-fault protocol requires a real local fault/incident. Therefore neither putting a BPMN code in DiagnosticMetadata nor faking a leaf fault is valid. FR-014 and the proposed control contract define a separate capability-bound transport; implementation remains gated on plan/task review and real-host proof.
