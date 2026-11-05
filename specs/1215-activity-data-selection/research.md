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
