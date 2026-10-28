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

## FR-015 bounded source audit and design decisions

Owner policy is settled in the reviewed spec. This audit continues delivery from T044 at `c76b16f2c`;
it does not repeat the parity investigation.

| Decision | Source constraint and rationale | Alternative rejected |
|---|---|---|
| Exact source outbox row owns immutable cohort | Existing EF row has scoped identity, ContentJson, schema stamp and revision; existing handlers receive only Intent. | A second replay ledger, intent-ID-only identity or publisher-time matching. |
| Select both complete recipient classes before dispatch | Router snapshots bookmarks but currently resolves and starts bindings afterward. | Per-attempt sets or persisting a prefix after some effects. |
| Preserve every sorted binding and per-artifact key | A shed first binding leaves its artifact key available to a later binding. | Per-binding start keys or collapsing same-artifact entries. |
| Narrow accepted-cohort start authority and GC roots | Ordinary live publication lookup cannot start a selected old artifact after republish; current GC roots cover executions/dispatches. | Resolve a replacement publication or borrow unrelated RetainedDependency authority. |
| Explicit old-writer family boundary | Every status rewrite replaces serialized outbox content; old readers ignore an unknown JSON member. | Leave the current family stamp and assume added JSON survives. |
| Durable source before coalesced fan-out | Overlay flush may reissue intents under its own commit identity. | Persist a process-local cohort or accept a provisional outbox identity. |
| Hold ambiguous legacy owed sends | First recipients are unrecoverable if an old send dispatched before acknowledgement; attempt zero is not proof. | Silently resnapshot current listeners. |

Correctness and standards source audits found no additional policy question. The proposed contract
makes these engineering requirements explicit. Actual-store legacy preflight and execution/GC race
proof remain implementation gates; this research artifact is not their evidence.
