# Elsa 4 unified execution architecture — research recommendation

**Status: reviewed research recommendation; proposed for owner ratification.**
Program [#2892](https://github.com/elsa-workflows/elsa-foundation/issues/2892), decision task #2904. This report is not a ratified constitution, production API proposal, or migration authorization. Read the [source baseline](current-source-baseline.md), [evidence ledger](README.md) and [witness review](witness-review-2026-10-17.md) for exact pins, observed results and limitations. Terms here describe candidate boundaries; canonical repository terminology remains in `docs/glossary/`.

See also [original ETL intent and Orchard prior art](original-requirements-and-prior-art.md) for dataset/connected-service modeling and the integrated user experience.

## Recommendation

Build one execution platform with independently extensible semantic models, composition components and hosting providers. Share identity, durable commit/recovery and work delivery. Preserve model-specific definitions, state and behavior. Native workflows and BPMN should be first-class peers; ETL can use the same infrastructure without moving record-by-record processing through the workflow coordinator. Agent collaboration should initially be an explicit native composition with its own messaging semantics, supported by the bounded deterministic durable witness.

The unifying contract is a durable transition of a scoped execution and its owed work. It is not a universal graph representation, a common join algorithm, an actor per activity, or a requirement that every model advance in the same scheduling loop. Those distinctions determine whether the architecture remains coherent as more models arrive.

## Responsibility map

| Boundary | Owns | Must not decide |
|---|---|---|
| Modeling surface | Model-specific languages, authoring validation, resource bindings, designer extensions | Runtime routing by diagram appearance alone |
| Pinned artifact resolver | Model/version, artifact digest, state schema, required capabilities, registered implementation | Silent migration to a different definition or handler |
| Execution model | Meaning of state and stimuli; legal transitions; completion/error/cancellation semantics; inspection | Provider membership or generic storage transactions |
| Composition component within a model | Sequence ordering, Flowchart joins, state-machine transitions, agent rounds/mailboxes | Global ownership/fencing or another model's token rules |
| Durable execution kernel | Scoped identity, state revision, ownership fence, commit fingerprint/receipt, owed work and accepted results | Inferring completion from an empty queue or joining branches |
| Hosting provider | Execution addressing, activation, serialization, placement, membership integration | Semantic completion or exactly-once external effects |
| Work execution service | Pinned invocation/input, claim/attempt, supported-handler check, result delivery | Root completion or the meaning of an ETL checkpoint |
| Model-specific data services | ETL batch admission/cursors; agent memory/knowledge bindings; BPMN task projections | Hidden changes to shared execution state |
| Operational services | Tasks, subscriptions, timers, incidents, audit, authorized operator commands | Bypassing the owning model to mutate internal state |

These are logical boundaries, not a prescription to create a repository, process or assembly for every row. A small installation should compose them in one .NET host. Deployment isolation and distributed providers remain optional.

## Execution and recovery contract

An execution is addressed by a complete scope and stable execution ID. Identity and signed capability fields require an unambiguous, versioned encoding; delimiter concatenation is unsafe unless every boundary rejects delimiter-bearing fields. Provider review exposed that flaw in the proof HMAC format; the adapter now rejects CR/LF scope fields and length-prefixes actor identity, while the shared fixture signer is not a production security contract. Persist a pinned artifact reference and state-schema identity before dispatch. Resolve the model through a registry, validate host capabilities, then allow that model to interpret the next accepted stimulus. A model may call its composition component; the kernel need not know whether a component is a Sequence, a Flowchart or something else.

The resulting transition commits state, durable owed work/continuations and a stable receipt atomically. A worker processes a pinned invocation under its own attempt fence. An accepted result is another durable input, not permission to complete the root. The model/component consumes it, decides the next state and dispatches additional work. Empty work queues can mean suspended, awaiting a message, awaiting a semantic continuation, or complete; only semantics distinguish them.

Retries reuse identity. Authorization is checked before receipt lookup. Identical committed requests can return their receipt after ownership changes; conflicting content fails. New writes require the current owner fence and state revision. Notification loss must not lose authoritative owed work. External effects remain at least once unless the destination supplies a stronger contract; completion persistence does not retroactively roll back a delivered effect.

The current Foundation checkpoint envelope contains workflow/activity/bookmark/incident state explicitly. Extracting a neutral kernel therefore requires deliberate refactoring, not renaming that envelope. The experimental JSON state/plan is evidence of a boundary, not the recommended production serialization or public API.

### A contract limitation exposed by cancellation

The experimental `CompositionPlan` has only `IsComplete`; the store projects Running/Completed, while `CancelWork` terminates an invocation without notifying the semantic model. The rule witness therefore remains Running after its only work item is cancelled. This is explicit evidence that these experiment DTOs are not a complete execution-model contract.

Revise the production candidate to separate common execution lifecycle from model-owned outcome. A model must receive a durable cancellation request, decide which work/subscopes to stop and commit its terminal or compensating state. Work cancellation receipts must be consumable semantic inputs, with a defined race against accepted success. A terminal envelope should carry a stable model outcome and inspection projection; it must not force BPMN business errors, native faults and rule cancellation into the same success flag. Already delivered external effects remain separate. Keep this as a proposed contract requirement until an end-to-end cancellation transition is witnessed; do not infer it from the current work-level test.

## Native composition and background activities

Keep joins with Flowchart semantics. Sequence should not acquire join concepts because the kernel supports Flowchart. Similarly, a state machine owns its transition rules. The native execution model provides the invocation protocol and hosts components; components supply their control semantics through explicit, testable transitions.

Background execution is an invocation policy, reusable across activity types when their contracts permit it. Persist the immutable inputs and handler version, schedule work, and let the component await a correlated result. An activity may itself invoke an external job, but that is distinct from the host generically offloading activity execution. Nested work must preserve cancellation, scope, idempotency and result correlation rather than creating an untracked background task.

Not every activity can transparently run remotely: ambient mutable context, process-local objects and nonserializable services need validation or an explicit local-only capability. Failure, cancellation and business outcomes must remain distinct. Retain middleware as an integration point where useful, while putting durable invocation ownership in a contract that survives worker or coordinator restart.

## ETL and the actor boundary

Use an execution actor for a pipeline run if that simplifies placement and coordinator recovery. Keep records and bounded batches in worker/data-plane services. A partition is a work/checkpoint boundary, not automatically an actor, workflow instance, persistence partition or tenant.

The ETL model owns source cursor meaning, partition completeness, transform topology, checkpoint aggregation and end-of-stream. Shared work owns dispatch and result mechanics. The witnessed pipeline uses one execution with multiple partition cursors; a blocked partition does not prevent another from advancing. Batch results advance cursors only after accepted completion. Destination idempotency handles delivery replay.

Backpressure needs explicit admission. Per-batch size is not aggregate capacity. The local witness now tests a separate execution record budget under an additional worker and recovers a reservation after confirmed process death. Production requires byte-aware budgets, fairness, connector buffering and failure-aware capacity reclamation. A lease expiring does not prove that an old process released its memory or stopped writing. Define the tolerated availability/safety tradeoff instead of claiming an unqualified global bound.

## Actor providers

The intended actor boundary is the full execution instance. This is a good fit for serializing coordinator decisions and abstracting placement across a cluster. It does not require each activity, token, ETL partition or agent turn to become an actor. Independent workers can run in parallel while the coordinator commits small semantic transitions.

Make the execution hosting contract replaceable, but do not copy every feature of Orleans or Proto.Actor into a lowest-common-denominator interface. Specify the behavior Elsa needs: stable address, activation/deactivation, mailbox/concurrency policy, recovery entry, capability-aware routing and cancellation of hosting operations. Keep application durability and fencing independently authoritative.

A provider must declare capabilities and pass the same behavioral suite. For Orleans, use Orleans membership/placement as the fleet authority; do not keep a competing Elsa node-liveness table. Framework activation and message delivery do not replace durable owed-work recovery or stable result identity. The bounded comparison exercises identical Sequence/Flowchart definitions through a direct coordinator and Orleans, including held parallel workers and owner loss. It supports provider substitutability, not a default-provider winner. Keep the built-in provider as the compatibility baseline and offer Orleans as an optional adapter after production conformance; do not replace the default on the strength of this local witness. AI-assisted implementation reduces coding cost but does not remove distributed-systems maintenance, upgrades, diagnostics and failure-testing costs.

## BPMN as a first-class model and as a child

Host the real BPMN interpreter as the owner of token, gateway, event, scope and error semantics. Give it durable state, work handles, timer/event inputs and ordered command dispatch. Do not reinterpret BPMN by translating every element into native Flowchart joins. Preserve its model-specific validator and inspection projections.

An embedded BPMN activity still makes sense as a native adapter to a BPMN child execution. It should use the same BPMN host/model as direct BPMN execution. The parent awaits a child outcome through the native composition protocol. The direct-versus-composed witness compares identical child traces through a persisted Sequence-style adapter; current Elsa integration remains separate work.

The service-task witness also starts a shared partition pipeline using a stable persisted BPMN work handle. It recovers partial provisioning, completed ETL work awaiting delivery, and a BPMN callback committed before process exit. A BPMN-side receipt makes callback replay idempotent; the two stores do not share a transaction. The BPMN host proof serializes its test driver and does not establish concurrent-host ownership/fencing; those guarantees must come from the production kernel. Success and business-error runs complete; fault/cancel adapters handle their BPMN outcomes while the experimental ETL model remains Running with cancelled work. That limitation reinforces the lifecycle revision above.

A proper BPMN product also needs task assignment/claim/completion, external workers, durable timers and correlations, incident/retry policy, definition deployment/versioning, audit and operations. These can be built above the semantic/host boundary. Each action must address a stable task or listener incarnation and be authorized, correlated and idempotent. Model a human task projection as an operational view of durable owed work, not a second independent authority for process progression.

Operator alteration must be a validated model command with audit and preconditions. The witness proves only delivery to a matching live modeled listener and rejection of arbitrary token movement. It does not prove arbitrary process modification, migration of running instances, or full BPMN conformance. These require their own support matrix and tests. Existing production migration ownership (#2735) remains intact.

## Agent collaboration

Separate three dimensions: control composition, message/data delivery and resource bindings. A resource connection to memory or knowledge is not a Flowchart transition. Give bindings a kind, scope, stable reference/version and authority; decide explicitly whether the binding captures a snapshot or resolves mutable content at execution time. Persist enough information to explain a resumed turn.

The real Microsoft Agent Framework probe distinguishes serial per-receiver handling, concurrent receivers, global round barriers and fan-in buffers retaining multiple arrivals. These mechanics can be implemented by a native agent composition without putting them into every native workflow. Multiple messages from one sender are not duplicate delivery of the same message. Stable message identity, delivery deduplication and fan-in membership are separate concerns.

Persist conversation/turn progress, pending approvals, budget consumption and tool-call identity at the appropriate boundaries. Store large conversation/knowledge content by reference where appropriate; a checkpoint must not silently become an unbounded transcript. Existing Elsa agent turn/tool/approval code is reuse material. Live model/tool integrations, durable approval-to-effect correlation, nondeterministic replay and retention remain separate product verification requirements beyond deterministic collaboration witnesses.

The production lifecycle contract must explicitly define when a proposed model decision becomes durably accepted, how approval suspension/resumption consumes that decision without reevaluation, and how cancellation reaches a model-owned outcome after tool effects may have occurred. Deterministic recovery in the witness establishes useful boundaries but does not close that contract. Usage accounting also needs admission before dispatch: rejecting an over-budget result after expensive work ran is not a spending bound.

A distinct agent execution model becomes justified only if native composition cannot express its lifecycle or scheduling without distorting the native contract. Different diagram edges alone are not sufficient justification. Keep this decision revisable after the native durable witness.

## Extension discipline

A new model package should register its artifact/state contracts, capability requirements, transition implementation, work handlers and inspection projection. Registration must not require a model-name switch in the shared kernel. Shared inspection must allow model-specific details rather than forcing fields such as token count, round or current activity onto every model.

The independent extension witness uses finite monotone rule inference to a fixed point. It is deliberately a semantic system with facts/rules and convergence rather than a renamed Sequence. Acceptance requires pinned package/artifact/state resolution, incompatible-host rejection, fresh-process recovery, result replay and cancellation without editing existing engines. The package and provider witnesses passed bounded review. The extension seam remains a candidate production contract until compatibility and lifecycle integration are specified.

## Reconciliation with current governance

The current checkout's decisions are not all interchangeable with this proposal. The following dispositions are proposed for review; no ratified file is changed by this investigation.

| Existing source | Preserve | Proposed clarification or amendment |
|---|---|---|
| [ADR 0031](../../adr/0031-runtime-burst-execution-sticky-single-writer-drain-with-in-process-fast-path.md), accepted | One serialized coordinator drain; durable truth; memory only a reconstructible cache; durable at-least-once delivery with deduplication | Its explicit wording says parallelism scales only across executions and calls that the intra-workflow ceiling. Durable offloaded leaf work introduces parallel compute within one execution. Amend that ceiling to distinguish serialized orchestration/state mutation from independently claimed worker compute. This is a real policy change, not an assumption that existing wording already permits it. |
| [ADR 0063](../../adr/0063-bpmn-moves-to-a-host-agnostic-library.md), **Proposed** in this checkout | Neutral BPMN semantics returns commands; Elsa owns bindings, persistence and operational integration; preserve frozen BpmnProcess/BpmnDecision contracts | First-class BPMN hosting extends the host options without retiring embedded activity adapters. Do not claim this proposed ADR is ratified. Source-pinned experiments under the owner's exception are not proof of a released migration prerequisite or authority to take over #2735. |
| [ADR 0073](../../adr/0073-ef-core-is-the-only-first-party-persistence-family.md), accepted | EF Core remains the only first-party production persistence family; domain contracts do not depend on EF | Raw SQLite experiments do not authorize a new persistence family. Any production neutral commit/outbox implementation must be realized through the established EF/runtime semantics and qualified provider-specific SQL. Actor-provider substitution must not replace that authority with grain storage. |
| [ADR 0078](../../adr/0078-workflow-executions-are-virtual-actors-and-cluster-membership-is-a-foundation-contract.md), accepted | Execution actors, one membership authority, commit fencing independent of routing, durable queue as record of owed work, provider conformance | Generalizing from workflow executions to model executions needs a reviewed contract extension. Use the Foundation membership contract for consumers and let an Orleans adapter translate its single fleet view; the direct Orleans API used by the probe is not permission for production consumers to bypass that contract. Preserve schema finalization/completeness, draining and executable-version rules. |

The constitution's artifact-only runtime and Design/Runtime deployment separation (§E2.2 and §E2.6.2) remain constraints: no model may require designer graphs/layout or mutable draft state to resume. Replacement hosting/membership services remain single-implementation contracts with duplicate-registration rejection (framework §2.6.2); additive model registration must use the appropriate extensibility mechanism, not silent override. A refactor must preserve valid behavior (framework §2.21.1); a new semantic capability must be named as such.

Executable-always-runs (§E2.6.1) also matters: a host lacking a required package must not corrupt or silently substitute an artifact. Capability-aware routing is a prerequisite to placing work correctly, not a waiver to leave an already accepted published artifact permanently unusable. Production composition/publication must ensure executable dependencies are available; the probe's incompatible-host refusal tests only the local safety boundary.

Activity versioning/Model X (§E2.8) and runnable-artifact material (§E2.9) carry draft/provisional qualifications in the current constitution. The experiment's package/artifact/schema pins inform those discussions; they do not silently ratify their final shapes. Propose any resulting amendments through the existing constitution-readiness ownership. No claim about current production implementation follows solely from an ADR's historical “already built” section.

## Proposed boundary decisions

These are research recommendations awaiting owner ratification, not claims that production Elsa already implements them.

| Candidate | Disposition | Evidence and consequence |
|---|---|---|
| One durable execution foundation | Accept direction | Contract convergence is an inference: native/rule work and the ETL bridge share WorkProof; the BPMN interpreter host still has a separate checkpoint store. A production BPMN-to-kernel integration proof remains required. Their semantic state remains different. |
| A universal workflow/data/agent graph | Reject | Control edges, data ports, state transitions and resource references encode different relationships. Share designer infrastructure through model-specific extensions. |
| Sequence/Flowchart as composition semantics | Accept | Component-owned ordering and joins survive independent worker completion. Do not move those algorithms into the generic kernel. |
| Execution-instance actors | Accept direction | Direct and Orleans hosting preserve the tested outcomes with workers outside the actor. Retain commit fencing independently of placement. |
| Actor per partition/activity/agent | Reject as default | The witnesses do not require it; adding those actors would multiply coordination boundaries without demonstrated benefit. Specialized future models can justify their own boundaries. |
| Pluggable hosting | Accept narrow contract | Start with existing built-in behavior; qualify an optional Orleans adapter. Proto.Actor remains an untested future adapter, not a claimed interchangeable implementation. |
| ETL on shared work infrastructure | Accept with model-owned data plane | Partition cursors and bounded batches belong to ETL. Record admission is demonstrated; byte bounds and distributed capacity reclamation remain product requirements. |
| First-class BPMN plus embedded adapter | Accept direction | Keep one real interpreter and expose direct/child hosting. Operational services build on stable handles; arbitrary alteration and conformance are separate scope. |
| Agent collaboration as native composition | Accept provisionally | Deterministic mailbox, round, approval and feedback scenarios fit without a new kernel model. Live integrations and lifecycle requirements may change that conclusion. |
| Independent execution-model registration | Accept direction | A packed rule-inference implementation loads through a generic registry without editing existing engines. Registration, schema and dependency compatibility need production governance. |
| Completion boolean and work-only cancellation | Revise | Cancelling work can leave a model Running. Durable model stimuli, outcome projection and terminal race rules are required before production extraction. |
| Exactly-once effects from actor delivery | Reject | Durable result acceptance cannot undo an external effect. Connectors must supply idempotency or explicitly expose weaker guarantees. |

## Product foundations and deferred capabilities

| Product capability | Foundation required now | Follow-up ownership |
|---|---|---|
| User tasks and external jobs | Stable work incarnation, authorized claim/complete commands, result correlation, audit and query projections | BPMN product #2907; align existing task/worker services |
| Timers, messages and event subscriptions | Durable owed commands, listener identity and deduplication; model validates event scope | #2907; preserve the tested winner/late-event rules |
| Incident retry and operations | Model-owned retry command with revision/authority preconditions and an inspectable outcome | Existing #2747; avoid a competing retry protocol |
| Arbitrary BPMN alteration and live migration | Explicit supported command matrix, interpreter validation, audit and old/new schema compatibility | #2907; rejected by current witness, not a hidden promise |
| ETL connectors and datasets | Typed ports/schema, versioned connected-service references, cursor ownership, delivery guarantees, byte-aware admission | ETL product #2908; original issue elsa-core#4809 |
| Agent memory, tools and approvals | Resource scope/version, durable accepted decision and exact approval-to-effect correlation, bounded content references | Extend #2912/#2913 conclusions through #2905; reuse current Agent infrastructure |
| Third-party models and hosting | Capability declarations, pin resolution, isolation and the same behavior suite | #2899 findings → #2905 production contract work |

The first-party runtime should expose common inspection headers (identity, model/version, revision, lifecycle, current owner and pending work) plus model-specific detail. BPMN tokens, Flowchart joins, ETL partition progress and agent approvals need their own projections. User interfaces can share navigation, permissions and commands while presenting the right vocabulary for each model.

Security remains explicit at every asynchronous boundary: tenant/persistence/authority scope is not interchangeable with actor address; worker credentials are not serialized resource bindings; result authenticity does not by itself authorize a tool effect. The experiment's HMAC and local resource fixtures are test mechanisms, not a proposed production identity provider. Retention and deletion must preserve enough receipt/correlation identity to avoid resurrecting work on replay.

## First production work unit: lifecycle and offload contract specification

The next slice under #2905 should specify and test the lifecycle boundary before extracting a generic runtime. Its output is a reviewed spec, compatibility fixtures and a small opt-in native integration proposal. It must not replace hosting, migrate existing BPMN execution, or introduce a second persistence authority.

**Scenario.** A native Sequence contains a Flowchart with two offloadable activities and then a final activity. Workers finish in either order. The Flowchart reports completion exactly once; Sequence starts its final activity once. Repeat with a cancellation request before either result, between results, and after the child completed. Recover across each durable boundary and compare existing synchronous behavior.

**Contract to specify.** An immutable invocation carries execution scope, definition/package/schema pins, activity invocation identity, inputs, required capability and an effect idempotency reference. Attempts carry their own fence. A result carries invocation/attempt identity and a typed success, fault or cancellation payload. A durable model input records result acceptance, cancellation request or operator action. A transition commits model state, work/child commands and an outcome projection with revision/fence checks. The public API shape must follow current domain terminology rather than copying the experimental DTOs.

**Decisions for that spec.** Keep the coordinator serialized. Define cancellation acceptance as a durable request, not immediate proof that physical work stopped. The model decides whether to await, compensate or terminate and records its outcome; late results remain auditable without reopening a terminal execution. Define exactly when child outcome consumption advances the parent. Persist decision acceptance before approved work dispatch. Treat retryable work failure, incident suspension, business error and terminal fault as distinct model outcomes/projections. Specify retention for receipt identities before allowing their deletion.

**Implementation seams to examine.** Start with the existing invocation scheduler, `RuntimeCheckpointCommitter`, `EfRuntimeCheckpointCommitStore`, ownership service and durable child-dispatch completion pattern identified in the [source baseline](current-source-baseline.md). Preserve the existing workflow checkpoint shape behind a compatibility adapter while designing a versioned neutral envelope. Do not replace these with the experiment's raw SQLite store. Recheck paths, open ownership and current main before starting production work.

**Acceptance.** Existing definitions and saved executions retain behavior; default execution remains local unless offload is explicitly enabled. Scope mismatch, unavailable pinned handlers and stale writes fail before mutation. Duplicate receipts and parent continuations do not repeat semantic advancement. Cancellation fixtures reach a model-owned state rather than leaving unprocessable results. Crash tests cover dispatch commit, effect delivery, result acceptance and parent continuation. Run affected unit/integration and REST e2e suites, architecture and map checks, plus negative controls proving the new guards matter. Document any backend-specific transaction limitation.

**Ordered tasks for the next approved slice.**

1. Refresh source/ownership, replay the existing Sequence/Flowchart compatibility baseline, and map persisted-record readers/writers.
2. Write the lifecycle/outcome specification and ADR 0031 amendment proposal; settle cancellation and child-completion race tables before naming public interfaces.
3. Define the immutable invocation/result and versioned envelope schemas, security context, capability validation and retention requirements; review them against BPMN and rule state needs.
4. Add contract fixtures for all acceptance scenarios, including negative controls, current persisted-execution compatibility and restart at each commit boundary.
5. Implement one opt-in native durable offload path through existing EF commit/outbox infrastructure, retaining synchronous behavior and existing providers by default.
6. Run the affected unit/integration/e2e and architectural gates, independently review the result, and document rollout/rollback. Only then select the separate BPMN-to-neutral-kernel integration slice with its existing owner.

Tasks 1–3 are specification work under #2905; tasks 4–6 become implementation leaves only after that specification is approved. This keeps unresolved API and migration choices visible without pretending the entire future engine is ready to implement.

**Rollout and rollback.** Keep the capability opt-in; introduce versioned persisted records additively, with compatibility readers and no automatic conversion of active executions. Rollback must stop new opt-in starts while retaining a capable worker/reader for already accepted instances. An old binary that cannot read the new schema is not a valid rollback plan. Approval of this research does not authorize a production migration.

## Adoption plan and decision gates

1. Use the completed bounded M1 evidence in #2896, #2898, #2900, #2913 and #2901, including its failed assumptions and explicit limitations. Do not graduate a generic API solely because the toy experiments compile.
2. Review the #2904 accept/revise/reject proposals with the owner. The independent reviews support a narrow hosting seam; the rule package required no shared WorkProof edits. Production contract ratification remains distinct from research acceptance.
3. Under #2905, design production migration slices against current source, preserving existing definitions, activity semantics and persisted executions. Add contract/compatibility tests before replacing providers or commit storage. Version persisted schemas and define rollback; do not change live instances implicitly.
4. Build native durable offload and first-class BPMN hosting as bounded integration slices, with current owners. Expand BPMN operations through #2907 and ETL through #2908 based on explicit supported behavior. Agent composition remains linked to existing agent infrastructure rather than a parallel product stack.
5. Use #2911's interactive HTML to discuss the reviewed recommendation, with linked executed evidence alongside clearly labeled architecture simulations. Provide overview, responsibility views, failure/recovery walkthroughs and links back to durable issues/source. Keep it usable offline and by keyboard.

The issue hierarchy is the progress authority. This report is the reasoning artifact; source/evidence packages are reproducible witnesses. The HTML is a communication view, not another independent backlog or a substitute for the contracts and acceptance criteria.
