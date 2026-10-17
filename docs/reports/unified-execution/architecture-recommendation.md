# Elsa 4 unified execution architecture — working recommendation

**Status: provisional research recommendation; combined witness review is not complete.**
Program [#2892](https://github.com/elsa-workflows/elsa-foundation/issues/2892), decision task #2904. This report is not a ratified constitution, production API proposal, or migration authorization. Read the [source baseline](current-source-baseline.md), [evidence ledger](README.md) and [witness review](witness-review-2026-10-17.md) for exact pins, observed results and limitations. Terms here describe candidate boundaries; canonical repository terminology remains in `docs/glossary/`.

## Recommendation

Build one execution platform with independently extensible semantic models, composition components and hosting providers. Share identity, durable commit/recovery and work delivery. Preserve model-specific definitions, state and behavior. Native workflows and BPMN should be first-class peers; ETL can use the same infrastructure without moving record-by-record processing through the workflow coordinator. Agent collaboration should initially be an explicit native composition with its own messaging semantics, subject to its durable witness.

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

An execution is addressed by a complete scope and stable execution ID. Persist a pinned artifact reference and state-schema identity before dispatch. Resolve the model through a registry, validate host capabilities, then allow that model to interpret the next accepted stimulus. A model may call its composition component; the kernel need not know whether a component is a Sequence, a Flowchart or something else.

The resulting transition commits state, durable owed work/continuations and a stable receipt atomically. A worker processes a pinned invocation under its own attempt fence. An accepted result is another durable input, not permission to complete the root. The model/component consumes it, decides the next state and dispatches additional work. Empty work queues can mean suspended, awaiting a message, awaiting a semantic continuation, or complete; only semantics distinguish them.

Retries reuse identity. Authorization is checked before receipt lookup. Identical committed requests can return their receipt after ownership changes; conflicting content fails. New writes require the current owner fence and state revision. Notification loss must not lose authoritative owed work. External effects remain at least once unless the destination supplies a stronger contract; completion persistence does not retroactively roll back a delivered effect.

The current Foundation checkpoint envelope contains workflow/activity/bookmark/incident state explicitly. Extracting a neutral kernel therefore requires deliberate refactoring, not renaming that envelope. The experimental JSON state/plan is evidence of a boundary, not the recommended production serialization or public API.

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

A provider must declare capabilities and pass the same behavioral suite. For Orleans, use Orleans membership/placement as the fleet authority; do not keep a competing Elsa node-liveness table. Framework activation and message delivery do not replace durable owed-work recovery or stable result identity. Default versus optional provider selection remains pending the full comparison. AI-assisted implementation reduces coding cost but does not remove distributed-systems maintenance, upgrades, diagnostics and failure-testing costs.

## BPMN as a first-class model and as a child

Host the real BPMN interpreter as the owner of token, gateway, event, scope and error semantics. Give it durable state, work handles, timer/event inputs and ordered command dispatch. Do not reinterpret BPMN by translating every element into native Flowchart joins. Preserve its model-specific validator and inspection projections.

An embedded BPMN activity still makes sense as a native adapter to a BPMN child execution. It should use the same BPMN host/model as direct BPMN execution. The parent awaits a child outcome through the native composition protocol. The direct-versus-composed witness compares identical child traces through a persisted Sequence-style adapter; current Elsa integration remains separate work.

A proper BPMN product also needs task assignment/claim/completion, external workers, durable timers and correlations, incident/retry policy, definition deployment/versioning, audit and operations. These can be built above the semantic/host boundary. Each action must address a stable task or listener incarnation and be authorized, correlated and idempotent. Model a human task projection as an operational view of durable owed work, not a second independent authority for process progression.

Operator alteration must be a validated model command with audit and preconditions. The witness proves only delivery to a matching live modeled listener and rejection of arbitrary token movement. It does not prove arbitrary process modification, migration of running instances, or full BPMN conformance. These require their own support matrix and tests. Existing production migration ownership (#2735) remains intact.

## Agent collaboration

Separate three dimensions: control composition, message/data delivery and resource bindings. A resource connection to memory or knowledge is not a Flowchart transition. Give bindings a kind, scope, stable reference/version and authority; decide explicitly whether the binding captures a snapshot or resolves mutable content at execution time. Persist enough information to explain a resumed turn.

The real Microsoft Agent Framework probe distinguishes serial per-receiver handling, concurrent receivers, global round barriers and fan-in buffers retaining multiple arrivals. These mechanics can be implemented by a native agent composition without putting them into every native workflow. Multiple messages from one sender are not duplicate delivery of the same message. Stable message identity, delivery deduplication and fan-in membership are separate concerns.

Persist conversation/turn progress, pending approvals, budget consumption and tool-call identity at the appropriate boundaries. Store large conversation/knowledge content by reference where appropriate; a checkpoint must not silently become an unbounded transcript. Existing Elsa agent turn/tool/approval code is reuse material. Live model/tool integrations, durable approval-to-effect correlation, nondeterministic replay and retention remain separate product verification requirements beyond deterministic collaboration witnesses.

A distinct agent execution model becomes justified only if native composition cannot express its lifecycle or scheduling without distorting the native contract. Different diagram edges alone are not sufficient justification. Keep this decision revisable after the native durable witness.

## Extension discipline

A new model package should register its artifact/state contracts, capability requirements, transition implementation, work handlers and inspection projection. Registration must not require a model-name switch in the shared kernel. Shared inspection must allow model-specific details rather than forcing fields such as token count, round or current activity onto every model.

The independent extension witness uses finite monotone rule inference to a fixed point. It is deliberately a semantic system with facts/rules and convergence rather than a renamed Sequence. Acceptance requires pinned package/artifact/state resolution, incompatible-host rejection, fresh-process recovery, result replay and cancellation without editing existing engines. Until that witness and provider comparison pass review, the extension seam is provisional.

## Adoption plan and decision gates

1. Finish M1 evidence in #2896, #2898, #2900, #2913 and #2901. Record failed assumptions and review each claimed boundary. Do not graduate a generic API solely because the toy experiments compile.
2. In #2904, disposition each boundary as accept, revise or reject and independently review the combined recommendation. Resolve whether host substitution exposes a useful narrow contract and whether any independent model required substrate changes.
3. Under #2905, design production migration slices against current source, preserving existing definitions, activity semantics and persisted executions. Add contract/compatibility tests before replacing providers or commit storage. Version persisted schemas and define rollback; do not change live instances implicitly.
4. Build native durable offload and first-class BPMN hosting as bounded integration slices, with current owners. Expand BPMN operations through #2907 and ETL through #2908 based on explicit supported behavior. Agent composition remains linked to existing agent infrastructure rather than a parallel product stack.
5. Produce #2911's interactive HTML after combined review, using real evidence traces alongside clearly labeled architecture simulations. Provide overview, responsibility views, failure/recovery walkthroughs and links back to durable issues/source. Keep it usable offline and by keyboard.

The issue hierarchy is the progress authority. This report is the reasoning artifact; source/evidence packages are reproducible witnesses. The HTML is a communication view, not another independent backlog or a substitute for the contracts and acceptance criteria.
