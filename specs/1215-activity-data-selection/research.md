# Activity Data Selection: design research

**Status:** Reviewed M2 design rationale; adoption takes effect on M2 merge. M1 research is closed;
[plan readiness](plan.md#readiness-and-qualification) records exact qualification and remaining delivery gates.
**Date:** 2026-11-05. Inspected baseline `e8476790`; requirements reviewed at `46b1a830d`.

The merged [M1 report](../../docs/reports/activity-data-selection-m1.md) owns actual experiment outcomes.
Supporting persisted expression runs succeeded; the requested no-expression selection journey failed.
This document records reviewed production choices, not a passing feature or adopted research code. Product
comparisons/references remain in the [program plan](../../docs/plans/activity-data-selection.md).

## Decisions

| Question | Proposed decision | Reason and alternatives considered |
|---|---|---|
| Author experience | Input-adjacent Choose data, shared source/member browser, readable reference; existing Studio controls. | Removes mandatory variable/capture/source editing. A standalone visual prototype does not resolve observed wire/runtime gaps; validate the composed implementation. Reconsider a large sidebar only from observed need. |
| Runtime meaning | Consumer-owned immutable read after existing role/causal resolution. | Derived required producer projections change producer success. Generated JavaScript makes basic selection depend on optional tooling. Neither meets ADR 0045/product boundaries. |
| Roles/wire | New authored InputMember/WorkflowRequestMember/VariableMember/ActivityResultMember, three appended compiled sources. | Extra paths on old kinds can be silently ignored. A generic mutable address/reference conflates roles. Preserve old whole-value wire/ordinals and expression parameters. |
| Path syntax | Nonempty exact string segments over serialized object/map names; no indexing/wildcard/fan-out. | Dotted syntax cannot distinguish literal dots/empty keys. Whole collections and explicit loops/transformations retain cardinality. |
| Uncertain shape | Known leaf contract before conversion; honest DeferredFit for unknown/truncated suffix. | Dynamic keys/opaque converters cannot be reflected reliably. Reject known invalid paths statically; check unknown presence/null/type before consumer activation. Uncertainty is neither guaranteed compatibility nor known absence. |
| Serialization truth | Thin existing-Core extraction of the unchanged activity-value profile; effective serialized metadata. | M1 observed CustomerId/customerId and JsonPropertyName mismatches. General payload/schema options differ. No S-9/#3805/global change or business-instance execution. |
| Target fit | Internal Workflows Runtime check before selected snapshot commit/construction using unchanged profile. | Hydrator currently checks after construction; executable validator uses other defaults. Reuse bounded alias/fit logic; no coercion/global codec registry. Unsupported opaque targets reject. |
| Availability | One validation-owned analysis service/Core port, owning structure control facts. | Discovery/validator diverge on Flowchart. Copying dominators into HTTP/UI creates another authority. Preserve all legal entries and Switch projected inputs. |
| Current state | Authorized exact-base persisted or nonpersisting snapshot analysis under existing save owners. | Existing context ignores frontend state/requires an authored input. Matching normal autosave or explicit snapshot/save-and-load handles current edits; hidden save/stale facts misrepresent intent. |
| Composition | Language-neutral Design capability; installed adapters only for explicit expressions. | Language-specific tooling cannot establish minimal-host mapping. New enum kinds plus engine requirements fail closed; local/queued/recovery admission must be added to existing distributed/import checks. |
| Expression insertion | Existing whole-source portable parameter plus escaped adapter access, one history operation. | No new parameter schema needed. Ambient activity names would adopt draft ADR 0089. A second editor owner loses continuity. |
| Graph edits | Identity-aware traversal/remap and repairable invalid references. | Labels cannot identify same-name producers. Pair copy remaps internal IDs; consumer-only copy preserves/diagnoses scope. Serialized member names stay unchanged. |
| Verification | Real host author/save/code/reopen/publish/run, causal, restart, minimal/restricted and inspected UX matrix. | Research semantic controls do not prove product acceptance. A restart reporting zero resumed work cannot prove durable continuation. |

## Activity-result bindings without a scope

**Decision.** A direct `ActivityResult` binding the server returns declares no `producerScopeId`, and the
owning structures supply one fact that decides where such a binding is sound. Existing documents keep their
scope, bytes and hashes. The rule is in the [binding contract](contracts/binding-contract.md#1-authored-wire);
the reason codes are in the [authoring contract](contracts/authoring-contract.md#source-identity-members-and-compatibility).

**Why the scope goes.** The resolver compares a declared scope only when the producer has one at run time,
and only Flowchart nodes (`scope:root`, or a generated id per loop pass or first-wins race) and placed
reusable boundaries (their own execution id) get one. Inside a closed reusable body the comparison is already
skipped. So the only value an author can know is Flowchart's private root constant, and no workflow was
found where declaring a scope selects the right occurrence and omitting it selects a wrong one. On a
Flowchart loop the declared `scope:root` silently reads the first pass; without a scope the second pass fails
as Ambiguous. Under a first-wins fork and beside a placed reusable activity a declared scope makes a valid
read Unavailable, which the scope-free form resolves. The resolver needed no change: a null declared scope
already skips the comparison, as portable parameters do.

**The fact.** `ActivityChildResultFacts.EarlierProducerRunCanReachConsumer`: within one run of the structure,
can an earlier run of the producer child be among the runs that led to the consumer child? It is defined by
lineage, not by "can run again", because a run's lineage follows the run that scheduled it:

- Sequence, If, Switch and Parallel schedule each child themselves, once per run: `false`.
- While, Do, For and ForEach schedule every pass of the body themselves, so an earlier pass is never in a
  later pass's lineage: `false`.
- Compensation runs each forward step once. Handler retries are each scheduled from their own step's run, and
  the producers a handler reads are forward steps: `false`.
- In a Flowchart each node run is scheduled by the run that completed before it, so on a cycle every pass
  leads to the next: `true` exactly when the producer can reach itself. A merge node that runs once per arrival
  is no exception, because each of its runs schedules its own successor. A Flowchart whose analysis cannot be
  built says nothing.
- BPMN proves order only for one unconditional linear chain of tasks, which runs each task once: `false`.
  Any other shape (gateways, loops, events, listeners, multi-instance) says nothing. A BPMN task run is
  scheduled by the token's previous run, like a Flowchart node.

Unset counts as "it can", so out-of-repo structures fail closed.

**Where the fact is asked.** At the lowest structure that holds producer and consumer, for that pair; and at
every structure above it, for its child that holds both, asked as producer and consumer at once. The second
part is needed because the lowest structure answers truthfully for its own run: a Sequence on a Flowchart or
BPMN loop runs its children once per run, but the loop runs the Sequence again and a completed composite leads
back to its children, so the first pass's producer is in the second pass's lineage. Before #4059 the analyzer
relied on the cyclic back-edge walk for that, which refuses only when an enclosing structure says
`IsCyclicBackEdge = true`; a BPMN process with a gateway leaves it unset, so a Sequence on its loop was offered
a binding that blocks as Ambiguous on the second pass. The walk stays for every read; the fact is now also
required of every enclosing structure for a scope-free read. A reusable activity's body is judged on its own
graph: the walk ends at the definition's root, and at run time the body's reads require the producer to share
the boundary's execution.

**Evidence.** Run through the real engine (`Essentials/tests/Activities/Behavioral/Tests/ActivityResultOccurrenceTests.cs`):
the Flowchart loop (scoped stale read, scope-free Ambiguous), first-wins fork, placed reusable sibling,
Sequence, top-level Flowchart, a producer before a looping Flowchart, a looping Flowchart nested in a loop
(silently stale in every form), every loop body, a Sequence on a Flowchart loop, a Sequence on a BPMN gateway
loop (Ambiguous with both producer runs) and a merge node. One workflow is taken from the analyzer's offer
through the publication compiler to a real run (`BpmnDataSelectionPublicationTests`). Publication refusal before
any write runs through the real publish handler for a Flowchart cycle and the BPMN gateway loop
(`PublishWorkflowRequestHandlerTests`). Established by reading only: that each handler's `false` holds for
retries and operator alterations (compensation) and for third-party scheduling, how an older runtime refuses a
scope-free executable (it reports a corrupt payload and runs nothing), and that Elsa 3 imports with a scoped
read in a Flowchart fail at run time.

**Not changed (#4056).** Bindings that declare a scope and portable expression parameters keep their existing
rules, including the loop weaknesses above. The code-first builder still writes a scope.

## Evidence and source seams

These are source observations, not new acceptance runs. Refresh exact source locations as main advances.

- [activityInputWire](../../Studio/src/essentials/Elsa.Studio.Workflows/Client/src/activityInputWire.ts)
  omits result/member structured kinds. M1 Code Apply escaped a seeded reference into JSON text.
- [PersistedExpressionAuthoringContextSource](../../Essentials/src/Workflows/Design/Api/Services/PersistedExpressionAuthoringContextSource.cs)
  requires authored/projected state and reflects CLR names/reference-nullability. Its frontend sends
  location/revision while ignoring the supplied editor state. Sequence/Flowchart availability differs.
- [ValueFlowValidator](../../Essentials/src/Workflows/Design/Validations/Elsa.Workflows.Design.Validations/Validators/ValueFlowValidator.cs)
  has independent structure logic; preserve Switch projection and Flowchart's start/no-incoming entries.
  M1 publication accepted a deleted portable producer, requiring shared admission on all executable paths.
  Design API composes the validation feature, but endpoint-free Publishing currently does not; its
  implementation assembly reference is not service registration. Add the explicit feature dependency
  and test endpoint-free publication rather than relying on Workbench composition.
- [RuntimeInputBindingCompiler](../../Essentials/src/Workflows/Publishing/Elsa.Workflows.Publishing/Services/RuntimeInputBindingCompiler.cs)
  ignores an extra member path and retains Customer→String mismatch. Pin the selected type and preserve
  InputMember across reusable boundary lowering.
- [RuntimeInputBinding](../../Essentials/src/Workflows/Runtime/Core/Models/RuntimeInputBinding.cs)
  validates one role payload and rejects unknown sources. Named enum JSON readers reject unknown strings;
  numeric future values reach canonical validation and reject. Test actual baseline readers and preserve
  constructor signatures; do not equate old authored-compiler behavior with compiled-reader behavior.
- The hasher explicitly formats bindings: serialization alone does not add new behavioral content to hashes.
- [ActivityValueJson](../../Essentials/src/Activities/Runtime/Elsa.Activities.Runtime/Services/ActivityValueJson.cs)
  uses Web/camelCase/strict numbers for completion/hydration. CLR MetadataLoadContext avoids executing
  author code; do not feed reflection-only types blindly to runtime JsonTypeInfo or load implementations.
- [RuntimeActivityInputMaterializer](../../Essentials/src/Workflows/Runtime/Elsa.Workflows.Runtime/Services/Values/RuntimeActivityInputMaterializer.cs)
  and [WorkflowIntrinsicExecutor](../../Essentials/src/Workflows/Runtime/Elsa.Workflows.Runtime/Services/Values/WorkflowIntrinsicExecutor.cs)
  have distinct inline/external/intrinsic paths. Target fit must precede selected snapshot/activation;
  [ActivityInputHydrator](../../Essentials/src/Activities/Runtime/Elsa.Activities.Runtime/Services/ActivityInputHydrator.cs)
  is later defense.
- Existing runtime requirement consumers cover placement/import/advisory preflight. Local start/bookmark
  resume and queued/recovered work need guards after pinned read, before scheduling/user code. An engine
  marker cannot retrofit old binaries; new discriminants provide concrete old-reader refusal.

## Scope and review

Preserve ADR 0045, explicit returns/collections/optional-source rules and existing result protection.
No latest-result lookup, retention change, nested writes, automatic fan-out, global serializer, new language/
editor, ambient activity API or runtime schema inference. Runtime previews remain a separate increment.
No ADR 0046/0089 amendment is needed for this proposal; route any later required semantic amendment through
its actual governance before adoption.

[Plan](plan.md) fixes package/port ownership, [binding contract](contracts/binding-contract.md) fixes
behavior, and [quickstart](quickstart.md) fixes evidence. Independent M2 artifact review is complete;
normal exact-head checks/review, merge and resulting-main qualification remain delivery gates.
