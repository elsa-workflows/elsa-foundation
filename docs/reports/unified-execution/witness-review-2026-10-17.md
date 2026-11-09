# Witness review checkpoint — 17 October 2026

This is the consolidated research review for Program #2892, not production architecture ratification. Experiments are isolated from production code. See the [evidence ledger](README.md) for the current disposition of each workstream. The owner-authorized protocol exception is limited to research despite unrelated red main CI; it does not turn that CI result green or establish production migration ownership.

## Shared work (#2896)

The bounded WorkProof run passed **111 assertions, 43 OS process identities and four negative controls**. It covers explicit composition-owned completion, durable owed dispatch and receipts, bounded partition batches under one pipeline execution, separate sink deduplication, stale result rejection, commit replay, aggregate record admission and process-death recovery of reservations. Sequence and Flowchart retain distinct composition policies; no actor is created per activity or partition.

Review findings resolved in the witness include correcting the optional concurrency test to race distinct result commits at one state revision, making admission and bounded reads transactional, retaining reservations until confirmed worker exit, and rejecting configurations that could deadlock when batch capacity exceeded the record budget. The current evidence records those fixes and the four negative controls.

Limits remain: the budget is record-based rather than byte/memory-based; worker-death detection and reservation release are local-host evidence; SQLite WAL does not prove multi-host storage; and external effects still require destination-specific idempotency. The experimental HMAC scope is not production authentication. These are product design obligations, not failed local assertions.

## Operational BPMN (#2898)

Root's final BPMN verification passed against the clean upstream interpreter pin `be9c6eaa963b846104d5aebf38d337bafab08710`; the refreshed evidence directory includes the host outputs and source hashes. The host checkpoints actual interpreter state, work handles and owed commands together. Fresh processes resume a fork/join; timer/human races and incidents follow interpreter dispositions; changed definitions and foreign/stale handles are rejected. A persisted Sequence-style parent runs the same BPMN child as direct hosting and records matching child traces.

The service-task extension now invokes the shared WorkProof `PartitionPipelinePolicy`. Its local success, business-error, worker-fault and interrupting-cancellation paths cover fresh-process partition provisioning/recovery, stable service-handle identity, separate ETL batch workers and replay of the BPMN-side result receipt after its commit. The interpreter remains the owner of BPMN token transitions. The ETL and BPMN stores do not share an atomic transaction; the worker fleet is local, there is no broker, and fault/cancel can leave the experimental pipeline execution `Running` with its work cancelled. The bridge is an operational witness, not an Elsa integration or general BPMN conformance claim.

The earlier nested-subprocess comparison was replaced by the persisted Sequence-style parent adapter because that is the relevant native embedding boundary. Modeled-message delivery is validated against a live listener; it is not arbitrary token alteration. Human task, incident and operator support still need product-level scope, authorization and audit decisions. Existing production migration ownership (#2735) is unchanged.

## Provider substitution (#2900)

The first full provider comparison passed for the same WorkProof Sequence and Flowchart definitions through direct coordinator hosting and an Orleans execution grain. It exercised overlapping Flowchart workers, Orleans membership-based capability placement, two-silo failover, pending receipt recovery, stale-owner rejection and normalized semantic outcome comparison. The setup comparison is qualitative; it is not a performance benchmark or a production provider guarantee.

Follow-up review found a scope/key delimiter collision, startup cleanup, shared-source manifest coverage and effect comparison gaps. The corrected run passed: length-prefixed identity and CR/LF rejection protect the provider boundary; matching effect payloads, 21 source hashes, 48 archived-log hashes and confirmed process/temp cleanup are recorded. Root independently reran the earlier full comparison, reviewed the fixes, and verified final artifacts rather than repeating an unchanged long failover run. The current setup remains local loopback clustering and SQLite WAL on one machine, with experiment-only identity credentials.

## Independent model (#2901)

Root's updated RuleModelProof run passed **29 assertions** after the engine was packed and loaded from the actual `.nupkg` artifact, the package descriptor was aligned with its NuGet package ID, and state validation replayed each trace round against the pinned rules. Review findings resolved in that update include removing the host's runtime project reference to the engine and rejecting fabricated inference traces and unknown model status values. Coordinator resume and worker claim retain the immutable execution pin and reject a package swap.

The finite positive-rule model reaches its least fixed point in four persisted rounds on the unchanged shared WorkProof substrate. The witness proves local package integrity and deterministic state replay, not package publisher authentication, arbitrary state migration, multi-host execution, or a production extension contract. Work cancellation rejects late results but leaves the model execution `Running`; terminal model cancellation is not implemented.

## Agent collaboration (#2913)

The real Microsoft Agent Framework package probe and the candidate native composition run are separate evidence. The package probe supports specific findings about receiver-local serial handling, concurrent receivers, round-wide barriers and fan-in buffers; package/source provenance remains distinct. It does not establish durable native composition.

The candidate native AgentCompositionProof final root run passed **142 assertions** using deterministic local executors. Independent review found that budget 4 could dispatch a follow-up cohort with no remaining capacity, then repeatedly fail to consume its result. Admission now checks participant cohorts, reviewers and manager decisions before dispatch; budgets 0–7 are covered and the durable budget-4 scenario ends with zero pending receipts. Tool results explicitly bind the approved invocation, with a forged-ID negative fixture. Earlier route, fan-in membership and conflicting-message findings were also corrected.

The full fixture encloses the collaboration in Sequence; Researcher/Specialist overlap, Reviewer waits for both, one feedback round is bounded, and exact approved tool work survives effect-before-ack loss. Accepted decisions/configuration/usage and versioned resource references survive fresh processes. Usage is fixed test units, not real model token or monetary admission. No live LLM, external approval identity, production connector, or multi-node transport is proved.

## Independent model-framework semantics (#2913)

The pinned Microsoft Agent Framework source probe distinguishes round-wide `Task.WhenAll` from fan-in edge buffering. Fan-in waits until every source has contributed, retains multiple messages from one source, then clears the arrivals; same-receiver delivery remains serial. These are reference semantics, not a durability claim. Native composition must preserve message identity, deduplication and fan-in membership as separate concerns.

## Recommendation and HTML review

Independent recommendation review found the boundaries coherent and required clarification that BPMN still has a separate experimental checkpoint store. The recommendation now identifies one neutral kernel as contract convergence to validate in production integration, not an already demonstrated shared BPMN store. It includes accept/revise/reject dispositions, lifecycle/cancellation gaps, governance reconciliation, operational product follow-ups and a bounded next specification slice.

The presentation distinguishes simulations from executed evidence and links the durable reports. Browser tests cover model lenses, nested composition, zoom, keyboard activation, all recovery scenarios and responsive widths. Direct-file testing is unavailable under browser policy; the no-external-dependency audit and that limitation are recorded rather than claiming a disconnected browser test passed.
