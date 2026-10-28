# Durable material, execution and delivery model

Design for review; identifiers below describe roles, not a stable public API promise. Shared contracts live in Workflows.Runtime.Core and contain no BPMN library types. BPMN payloads remain model-owned.

## Immutable material

**Runtime material** has a kind/version and complete existing artifact identity/incarnation. Native material wraps the unchanged `WorkflowExecutable`; a model variant carries protocol/version, opaque canonical definition payload, declared capabilities, immutable dependency pins and input/output contracts. Model material has no fabricated RootActivity.

The existing artifact provider owns physical storage, content equivalence, scope/source references, closure leases and retention. Native historical JSON/hash remains byte-compatible. The new variant has its own declared write/read family and includes kind/protocol/profile/dependency pins in its hash. Same identity plus different content is conflict, never update. Native-only APIs refuse the model variant explicitly rather than interpreting it as a native root. Provider capability defaults to unsupported unless its complete selected store/lease/retention path is qualified.

**BPMN artifact payload** contains the released process definition, the initial direct profile, binding inventory and versioned binding data. Service bindings pin published native material and typed results; human/timer bindings identify supported model operations. The runtime reads no mutable draft, Studio layout or importer catalog to execute. Native dependency closure and model binding pins share complete physical-incarnation/deletion protection; string-only lease acquisition is insufficient.

## Model execution checkpoint participant

One scope/execution key binds permanently to model protocol/version, material identity/incarnation and authority. Header fields are persistence scope, tenant, execution ID, protocol, artifact pin, semantic revision, lifecycle status, authority/root initiator and optional exact native result address. Opaque payload contains BPMN process state, live/stopped work handles, attempts, human waits, timer bindings, incidents and terminal business result. Do not serialize this payload into metadata strings.

Validation occurs at both application and provider boundaries: physical key equals full logical identity; scope/tenant/authority match; immutable protocol/material/result-address fields never change; expected semantic revision matches; current owner/fence is valid; new receipts and owed work belong to this exact decision. Provider writes the model row, receipt, consumed scheduler claim, outbox and create-only marker in one RuntimeDbContext transaction. Failure leaves none of that new decision visible.

Semantic revision advances only on accepted model decisions. Execution fence identifies coordinator ownership; worker attempt/fence identifies a work attempt. They are independent. A model state update cannot authorize a foreign/obsolete work result. Existing native participants and absent-model fingerprints remain unchanged; do not append an empty serialized model field to old identities.

## Accepted input and conflict identity

An input records scope, execution, command/delivery ID, canonical content fingerprint, exact invocation/attempt where applicable, accepted revision and retained result. Its logical key includes scope/execution/input ID. Equivalent retries return the recorded outcome after authorization; conflicting content under the same key is refused without mutation. Unknown model protocol/command is refused before durable acceptance and cannot reach Noop fallback.

The actor's in-memory duplicate cache and a dispatched/queued response are not the accepted-input receipt. The durable marker/receipt decides replay after restart. Authorization and material/execution identity checks precede returning a retained result, including when a different owner now holds the execution.

## Work, cancellation and native service bridge

A logical work handle binds model execution, element/binding, semantic token, iteration and invocation. Attempts add an immutable attempt ID and generation. A native service binding adds deterministic child workflow identity, pinned worker artifact, canonical inputs and typed result address. Start/cancel/result identities derive from those retained values, not wall-clock retry time or caller-provided native-parent IDs.

Start admission validates the real committed source/outbox claim, exact current work binding and dependency pin. Its final acceptance condition must be staged atomically with native child creation, not just checked before it. If cancellation wins first, later start cannot admit a live child. If start wins first, cancellation remains owed until actual native/ETL cancellation or a truthful existing terminal result is proven. A missing child alone is not sufficient acknowledgement while its start remains deliverable.

The native child's terminal checkpoint records typed model-result responsibility through a registered enricher. The model consumes a matching result and its input receipt atomically with progression. The sender retires responsibility only after durable model consumption/equivalence or proven terminal invocation. Technical fault, business error and cancellation remain distinct. For ETL, inspect both native service outcome and actual pipeline run/partition disposition; Completed workflow status cannot erase BusinessError.

Native `RunBpmn` has its own real committed activity/wait. Its start/cancel intents address the same model execution that direct start would use. Cancellation-before-admission writes a durable model invocation cancellation disposition so delayed start cannot run it. Terminal model delivery retains the exact native wait/registration/material pin; no synthetic bookmark or native parent is created for a directly started BPMN execution.

## Timers, human waits and broadcasts

Timers retain an invocation/attempt, due time and deterministic input identity. They are not eligible before due time. An explicit wait expiry, where present, is checked at acceptance; callers cannot backdate eligibility. Due time alone does not prove the timer decision committed. Competing eligible inputs follow the first durably accepted model decision; retries retain that outcome.

Human completion addresses the current retained human invocation/attempt and authorized scope. It is distinct from task claim/assignment/delegation, which this task does not implement. Incident retry records a new attempt and one retry-command receipt; older results cannot overwrite it.

## Retryable versus final fault

The immutable service binding selects FinalFault (default) or HoldForRetry with a positive bounded attempt budget. A technical child failure under HoldForRetry records the original child/pipeline result and failed attempt, retains the interpreter's exact pre-failure state and active work token, and enters nonterminal WaitingIncident. The failure has not been submitted as a terminal semantic fault. Human/operator retry is allowed only for that current incident/work/failed attempt and remaining budget.

Retry atomically records one request receipt and a new physical attempt/child identity for the same logical semantic work, then owes its dispatch. Same-attempt delivery recovery still uses one child/pipeline identity; an explicit new retry attempt may admit a distinct run while preserving the previous run and failure evidence. The interpreter is not restarted or rewound. Old attempt results are rejected even if the interpreter still has the same logical token. A competing timer/parent cancellation may retire the held work and incident; a later retry cannot reactivate it.

FinalFault and exhausted budgets feed the failure to the interpreter and retain the resulting terminal disposition. Final semantic fault/cancellation is never reopened by incident retry; return a contextual conflict. Modeled business errors go through their declared catch/propagation path and are not reclassified as retryable technical incidents. Qualify this with actual direct and RunBpmn technical failures, retained state across restart, one new retry attempt, unchanged old fault evidence and late-old-result refusal.

Broadcast delivery retains its first complete recipient snapshot before any dispatch, including an empty snapshot. A model recipient records exact execution/model protocol/element/wait/invocation/attempt identity and scope. Adding a recipient family must preserve historical native snapshot JSON, hashes, retention and default-refusing capabilities. Recipients created after publication but before first selection may match; later re-armed listeners cannot join an existing snapshot. Physical source/claim validation remains required before replay or acknowledgement.

## Terminal state and retention

Completed, Faulted and Cancelled are distinct lifecycle outcomes. BusinessError is a typed semantic result, not automatically technical Faulted. An already-terminal pipeline retains its actual result when its BPMN caller later cancels. Model-terminal state can still have owed cancellation or parent-result delivery; do not delete or declare delivery settled from the terminal flag alone.

One shared lifecycle reader resolves authoritative native or model state in the physical scope before dispatch/drain and resumption cleanup. Native status keeps its original stop/purge policy. The model reader distinguishes nonterminal Running/WaitingIncident, TerminalPendingSettlement and TerminalSettled, without writing a native execution header. While terminal, ordinary token progression/new work is refused; exact pending cancellation acknowledgements, retained child-result/parent-delivery proofs and identical input replay may settle obligations without evaluating or reviving token semantics. Such settlement updates are fenced checkpoint decisions with receipts and outbox changes, not independent cleanup writes.

RuntimeResumptionService must not apply its native delete-all-queued-work path to a terminal model. It routes retained settlement inputs through the same verified protocol/lifecycle policy; only classified obsolete progression work is purgeable. Unknown/unavailable material or input type remains owed/unavailable, never purgeable by default. A terminal model with pending settlement is an explicit fresh-process acceptance case. Native terminal-purge tests remain valid and unchanged in meaning.

Material, dependency pins, source cohorts, accepted-input replay evidence and exact recipient/invocation state remain retained while needed by owed work. Deletion/cleanup must consult model references and source proofs as well as native ones. New capability admission-off rollback retains installed readers/workers for existing accepted instances. Old binaries must not write unreadable new families. Raw infrastructure/serialization/provider failures become contextual domain failures with preserved causes; transient unavailable dependencies retain work rather than becoming success or permanent unsupported-state poison.

## Schema and compatibility

Add explicit typed model execution/input tables and any required artifact-family discriminator/version through the Runtime EF migration family for SQLite/PostgreSQL/SQL Server/MySQL. Preserve native rows, indexes, content-addressed identity and consumer-owned contexts. Use existing payload-column configuration and physical key/hash validation conventions. Extend provider capability and read/write-family checks before enabling new admissions.

The existing generator's default Initial-regeneration mode deletes prior migration sets. It is unsuitable for these retained real databases. Add an explicit narrowly selected incremental mode through the same tooling/factories, preserve the existing Runtime migration IDs/files, and test fresh plus baseline-database upgrade. Document the saved-execution exception without freezing unrelated module baselines or claiming the general #1976 freeze completed.

Native constructors, enum numeric values, DTO properties and old replay payload shapes remain unchanged. Add optional overloads/default-interface capabilities only where actual consumers need them. Validate both direct serializer and actual IPayloadSerializer behavior; absent optional values must remain absent. Native coalescing retains its original semantics; model-bearing commits force a durable boundary until folding is explicitly implemented and qualified.
