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

## Member bindings and runtime selection (#4078 design note)

**Status:** design note for T011–T017, written against `origin/main` at `a9771131c3` (11 Nov 2026). It decides how
the change is cut, not new behaviour. Sections 1–10 were first written from source read at that commit; the open
claims were then characterised by tests run on that commit (nine items, §11), and slice A has since been delivered
on top of it (§10). Each claim says whether it was read or run. The rules themselves are in the [binding contract](contracts/binding-contract.md),
the [data model](data-model.md#compiled-read-selection) and the [authoring contract](contracts/authoring-contract.md);
this section does not restate them.

Path keys used below: `WRC` = `Essentials/src/Workflows/Runtime/Core`, `WR` =
`Essentials/src/Workflows/Runtime/Elsa.Workflows.Runtime`, `WP` =
`Essentials/src/Workflows/Publishing/Elsa.Workflows.Publishing/Services`, `WDV` =
`Essentials/src/Workflows/Design/Validations/Elsa.Workflows.Design.Validations`, `ADC` =
`Essentials/src/Activities/Design/Core`, `CLR` = `Essentials/src/Activities/Design/Reconciliation/Clr/Services`,
`EF` = `Essentials/src/Workflows/Runtime/Persistence/EntityFrameworkCore/Stores`.

### 1. What already exists

The foundation leaf (#3922, PR #3948, merged 10 Nov) and #4053/#4059 put these member pieces on main:

- **Reserved authored kinds, refused.** `ActivityDataReadBinding` recognises `InputMember`, `WorkflowRequestMember`,
  `VariableMember` and `ActivityResultMember` and returns `UnsupportedMemberBinding` for them
  (`WDV/Internal/ActivityDataReadBinding.cs:36-40,106,155-156`). A `memberPath`, or a `path` on anything but
  `WorkflowRequest`, on an old kind returns `UnsupportedMemberSelection` (`:108-111`, portable parameters `:81-84`).
  The analyzer turns either into `ActivityData/<code>` (`WDV/Services/ActivityDataSelectionAnalyzer.cs:210-213`), and
  the workflow compiler throws on those findings before any write (`WP/WorkflowExecutableCompiler.cs:245-261`); the
  template compiler does the same (`WP/ActivityTemplateCompiler.cs:546-551`).
- **Member path in the context query.** `ActivityDataSelectionQuery.MemberPath` and `ShapeLimits`
  (`Essentials/src/Workflows/Design/Validations/Core/Models/ActivityDataSelectionModels.cs:49-50`), bounded to 64
  non-null segments, depth ≤ 16 and ≤ 512 members (`ActivityDataSelectionAnalyzer.cs:395-397`; endpoint
  `Essentials/src/Workflows/Design/Api/Services/ActivityDataContextService.cs:162-163`). A path needs a focused source
  (`ActivityDataSelectionAnalyzer.cs:87-91`). For a path, the analyzer returns the **whole-value** binding with the
  member's shape (`:147-153`): there is no member binding to store yet.
- **Shape facts through every prefix.** `IActivitySerializedShapeSource` requires the selected shape to carry
  effective nullability, presence and protection through each prefix (`ADC/Contracts/IActivitySerializedShapeSource.cs:13-14`).
  The CLR reader composes them per segment (`CLR/ClrSerializedShapeReader.cs:34-75`: dictionary step makes presence
  `Optional` at `:45-51`; scalar or collection parent stops with `activity.shape.member-parent-not-object` at `:52-53`;
  unknown name stops with `activity.shape.member-not-declared` at `:59-60`; nullable/presence combine at `:61-65`).
  `JsonElement`, `JsonDocument`, `JsonNode` and `object` are `activity.shape.dynamic-json`; converters, number handling
  and polymorphism are opaque (`:224-238`). Member selection on a named output starts after the existing projection
  (`CLR/ClrActivitySerializedShapeSource.cs:108-143`).
- **The activity-value profile** is extracted (`Essentials/src/Activities/Runtime/Core/Serialization/ActivityValueJsonProfile.cs`,
  revision `elsa.activity-value-json/1`).
- **Scope-free result admission** (`ActivityDataSelectionAnalyzer.cs:272-312`) and the `EarlierProducerRunCanReachConsumer`
  fact, used for both offering and admitting (#4053, #4059).

Missing: every runtime piece, the compiler's knowledge of member kinds, pinning, hashing, capability and guards.

| Task | State | Evidence |
|---|---|---|
| T011 runtime model | Not started | `RuntimeInputBindingSource` has six members, `SecretRead = 5` last (`WRC/Models/RuntimeInputBinding.cs:128-141`; `WRC/PublicAPI.Unshipped.txt:4049-4055`). No selection type. |
| T012 authored kinds and validation | Partly | Names reserved and refused (above). The compiler does not know them: an unknown kind falls through to `CompileExpressionInput` (`WP/RuntimeInputBindingCompiler.cs:422-457,844-869`), and it silently drops a `memberPath` on `ActivityResult` (`:639-666` reads named properties only); the analyzer, not the compiler, refuses that today. `Input`→`WorkflowRequest` lowering keeps only `referenceKey` (`WP/ActivityTemplatePlacer.cs:665-684`). |
| T013 pin and hash | Partly | Prefix composition exists in the shape source (above). Nothing pins it into a binding; the linker only plans `ActivityResult` (`WP/ActivityResultConversionPlanLinker.cs:57`) and is called only by the workflow compiler (`WP/WorkflowExecutableCompiler.cs:290-291`). Hashers unchanged. |
| T014 runtime selection | Not started | Resolver has no member arms; its default throws (`WR/Resolvers/RuntimeInputBindingResolver.cs:18-34`). Its projection walk splits dots and falls back to case-insensitive names (`:146-160,172-187`): not reusable for exact segments. |
| T015 inline/external fit before snapshot | Not started | Materializer external projection reuses the same dotted, case-insensitive walk (`WR/Services/Values/RuntimeActivityInputMaterializer.cs:416-453,521-536`). Precedent for a payload check: intrinsic `ValidateAssignmentPayload` (`WR/Services/Values/WorkflowIntrinsicExecutor.cs:721-729`). |
| T016 capability and guards | Not started | Checker, consumer registry, derived requirements, placement gate, preflight and reconciler exist (§8). No check in `WorkflowStartDispatcher` or `BookmarkResumeDispatcher` (no reference to the checker in either file). |
| T017 controls | Not started | Pinned controls to extend: `ScopedActivityResultHashTests.cs:40-41`, `WorkflowExecutableCompilerGoldenTests.cs:55`, `WorkflowExecutableHashContractTests.cs:15-19` (all under `Essentials/tests/Workflows/`). |

### 2. Runtime model (T011)

**Decision: three new enum members plus a selection that only they may carry.** The binding contract fixes new
authored discriminants; the runtime model makes the same choice, for two reasons proven from the code:

1. *Stored bytes.* Executables and templates are read with `JsonSerializerDefaults.Web` plus
   `JsonStringEnumConverter` (`EF/RuntimeArtifactJson.cs:23-24`), and Web defaults skip unknown properties. A
   `selection` property on an old `ActivityResult` binding would be dropped by every older reader, which would then
   read the whole value: the central silent misread. An unknown enum **name** fails that reader instead.
2. *Code that switches on `Source`.* Every existing arm for an old kind would treat a selection-bearing old kind as a
   whole-value read (hasher `WR/Services/Executables/WorkflowExecutableHasher.cs:129-140`, linker
   `ActivityResultConversionPlanLinker.cs:57`, remapper `WP/PlacedReferenceRemapper.cs:77-108`, materializer
   `RuntimeActivityInputMaterializer.cs:512-516`, intrinsic `WorkflowIntrinsicExecutor.cs:741-743`, inspector
   `Essentials/src/Workflows/Runtime/Api/Services/WorkflowExecutableInspector.cs:494-508`). A new kind instead falls to
   each default, which this design makes refuse (§3).

**Shape.** Append, after `SecretRead`: `WorkflowRequestMember = 6`, `VariableMember = 7`, `ActivityResultMember = 8`.
Ordinals matter: the template behaviour hash and the snapshot binding fingerprint serialize `Source` with default
options, i.e. as a number (`Essentials/src/Workflows/Publishing/Core/Services/ExecutableActivityTemplateBehaviorHasher.cs:21-32`,
`RuntimeActivityInputMaterializer.cs:620-636`); stored executables and the V2 hash use the name
(`RuntimeArtifactJson.cs:24`; `WR/Services/Executables/WorkflowExecutableHashJsonContext.cs`, `UseStringEnumConverter`;
`WorkflowExecutableHasher.cs:381`). Neither names nor ordinals may ever change.

```text
public sealed class RuntimeReadSelection                       // WRC/Models, beside RuntimeInputBinding
  ctor(int schemaVersion, IReadOnlyList<string> segments, RuntimeReadSelectionMode mode,
       ValueTypeDescriptor? selectedType = null, bool? mayBeAbsent = null, bool? mayBeNull = null,
       ValueRepresentation? representation = null, string? profileRevision = null)   [JsonConstructor]
  const int CurrentSchemaVersion = 1; const string RequirementKey = "elsa.runtime.input-member-selection"
public enum RuntimeReadSelectionMode { Known = 0, DeferredFit = 1 }
RuntimeInputBinding: [JsonIgnore(WhenWritingNull)] RuntimeReadSelection? Selection { get; }
```

Invariants in the `RuntimeReadSelection` constructor: schema version exactly 1 (a later schema read by this binary
throws, the in-version fence); 1–64 segments, each non-null, empty strings allowed, copied to an immutable array; a
total size bound (proposed 4 096 UTF-16 code units, §11); `Known` requires `selectedType` and `profileRevision`;
`DeferredFit` requires `selectedType`, `mayBeAbsent` and `mayBeNull` to be null, so unknown stays unknown. No
mutable, reference or write API. Presence and nullability are two nullable booleans rather than a third enum; the
Design `ActivitySerializedPresence` cannot be used from Runtime Core.

`ValidateCanonical` (`RuntimeInputBinding.cs:85-118`) gains: each member kind maps to its old payload
(`WorkflowRequestMember`→`workflowRequest` with `Path == null`; `VariableMember`→`variable`;
`ActivityResultMember`→`activityResult` with `ProducerScopeId == null`, §5); `selection` is non-null exactly when
the source is a member kind; still exactly one role payload. A member kind without a selection, or an old kind with
one, throws. That turns every code path that rebuilds a binding through the old constructor (for example
`PlacedReferenceRemapper.cs:157-174`, `ActivityResultConversionPlanLinker.cs:99-110`, `ExecutableNodeCompiler.cs:465-467`)
into a loud failure until it is updated, instead of a dropped selection.

**Constructor and E5.** The existing 13-parameter constructor (`RuntimeInputBinding.cs:14-28`,
`PublicAPI.Unshipped.txt:4032`) keeps its exact signature and line and forwards `selection: null`. A new overload
takes `RuntimeReadSelection? selection` as a **required positional** parameter right after `source`, then the same
optional parameters; it carries `[JsonConstructor]`. Placing it there, not as a trailing optional, avoids CS0121
ambiguity for every existing call that omits trailing arguments, and keeps old calls bound to the old overload.
`PublicAPI.Unshipped.txt` (Runtime Core) gains: the new constructor line; `RuntimeInputBinding.Selection.get`; the
three enum members with `= 6/7/8`; `RuntimeReadSelection` with its constructor, getters and two constants;
`RuntimeReadSelectionMode` with `Known = 0`, `DeferredFit = 1`. Under §E5 these are additive (Minor): an overload on
a class, new members and types. No interface changes (`IRuntimeInputBindingResolver` stays as is, §7). RS0026 (more
than one public overload with optional parameters) is a warning, not an RS0016/RS0017 error; the precedent is
`ExecutableNode` (`PublicAPI.Unshipped.txt:3032-3037`). `PublicAPI.Shipped.txt` is empty (one line), so the
"last released baseline" for the manual §E5 comparison is the current unshipped file.

Not in schema 1: a protection minimum. No shape source sets member-level protection (`ClrSerializedShapeReader.cs`
`Shape()` sets nullability, presence and representation only), so the field would always be empty; source and
projection protection keep flowing as today (`RuntimeInputBindingResolver.cs:108-111`,
`RuntimeActivityInputMaterializer.cs:357-360`). A later schema can add it.

### 3. Compatibility, proven from the code

| Artifact | Whole-value bytes before → after | What a member binding adds | Older reader |
|---|---|---|---|
| Authored document (`ArgumentValue`) | Identical: no field on old kinds. | New `expressionType` and `memberPath`. | Stored losslessly as an opaque value. Publication on any server since #3948 refuses `ActivityData/UnsupportedMemberBinding` before a write (§1). Before #3948 it compiles to an expression of language `ActivityResultMember`, which no evaluator runs (binding contract §6, not re-verified). |
| Executable JSON (`EF/EfWorkflowExecutableStore.cs:82,1032`) | Identical: `Selection` omitted when null, like `Secret` (`RuntimeInputBinding.cs:82`) and a null `ProducerScopeId` (`:232`). | `"source":"ActivityResultMember"`, `"selection":{…}`, derived requirement in `runtimeRequirements`. | Unknown enum name: the store throws `InvalidDataException` "payload is corrupt" wrapping the converter's `JsonException` (`EF/EfWorkflowExecutableStore.cs:1038-1041`); nothing is returned. A numeric source (no store writes one) reaches `ValidateCanonical`, which throws, and the store wraps it the same way. Run: `EfRuntimeArtifactScopeTests.MemberSourceReads.cs`, which also shows a `selection` added to an old kind is dropped and the whole value read. |
| Template envelope (`EF/EfExecutableActivityTemplateStore.cs:339,404-406`) | Identical, same options. | As above. | As above. |
| Executable hash V2 (`WorkflowExecutableHasher.cs:375-380`) | Identical: the binding is serialized whole through the source-generated context, which honours `WhenWritingNull`. | `selection` inside `contract`. | Not a reader. |
| Node-payload hash (`WorkflowExecutableHasher.cs:123-146`; used by `ComputeHash(node)` and draft test runs, `Essentials/src/Workflows/Publishing/Api/Services/ActivityDraftTestRunService.cs:286`) | Identical if old arms are untouched. | **Silent hazard:** a member kind falls to `_ => CanonicalJson(LiteralValue)` (`:140`), so two member reads differing only in path hash alike. The store keeps the first body for an equal hash and checks nothing but the hash (`EF/EfWorkflowExecutableStore.cs:140-144`), so a second test run would execute the first path. Closed by explicit arms and a throwing default (§6). | — |
| Template behaviour hash | Identical (default options, `WhenWritingNull`). | `Selection` and the derived requirement. | — |
| Input snapshot (`WRC/Models/ActivityExecutionValueFlowRecords.cs:65-72`) | Values unchanged; binding fingerprint unchanged only if selection is appended when present, as secrets are (`RuntimeActivityInputMaterializer.cs:637-644`). | The **selected, converted** value; never the source. | An older runtime cannot load the executable, so it never reads such a snapshot. |
| Inspection view (`Essentials/src/Workflows/Runtime/Api/Models/WorkflowExecutableInspectionViews.cs:124-137`) | Identical. | Without change, shows `Source` `ActivityResultMember`, the producer reference and the projection key as summary, and no path: a misleading inspection, not a misread. Add a `Selection` view member and a summary naming the path. | Older Studio shows the unknown source string. |

**Mixed versions.** In distributed placement an older member that cannot deserialize the pinned executable resolves
the requirement as `Unresolved` and does not claim the execution; commands wait for a member that can run it
(`Essentials/src/Workflows/Runtime/Distributed/Elsa.Workflows.Runtime.Distributed/Placement/ExecutionPlacementRequirementResolver.cs:57-59,78-85`;
`ExecutionPlacementGate.cs:143-160`). Without the distributed module the outcome is destructive, not a wait. A
drain whose pinned executable read throws parks that work item as poisoned with no retry and raises one blocking
`SchedulerWorkPoisoned` incident; a failed start or checkpoint item faults the run, a failed child schedule leaves it
running and blocked (run: `InputReadFailureCharacterizationTests.A_drain_whose_pinned_executable_read_throws_poisons_that_work_item_without_a_retry`,
with a throwing reader standing in for an older build). So where several non-distributed runtimes share one store,
member bindings must not be published until every runtime is upgraded; slice C states this beside the preflight.

**As delivered in slice C (the shared-store upgrade rule).** Publication of member reads sits behind the
`WorkflowDesignValidations` setting `MemberReads` (`WorkflowDesignValidatorOptions.MemberReads`), off by default.
It is enforced in the shared analyzer, the one admission step every executable-producing path runs before any write
(workflow publication, version and draft test runs, template compilation and activity-draft test runs, BPMN
publication, Elsa 3 import): while it is off a member read is neither offered nor admitted and is refused as
`ActivityData/UnsupportedMemberBinding`, exactly as before slice C. A host that shares a store with older runtimes
leaves it off until every runtime is upgraded; the rule is stated for hosts in the feature's README, beside the
setting. The publication preflight is unchanged: it already reports the derived requirement unmet where a runtime
cannot run member reads.

**Every silent path found, and how it closes.** (a) Selection on an old kind ignored by an older reader: closed by
new enum members (§2). (b) Node-payload hash collision: closed by explicit arms plus a throwing default. (c)
`PlacedReferenceRemapper` passed unknown kinds through unchanged, so a placed member read would have kept its
template-local producer id; an `isOptional` read would then resolve to null. (Corrected: since slice A the remapper
refuses every member read it has no arm for, `WP/PlacedReferenceRemapper.cs:78`; slice C added the result arm and
slice D1 the variable arm, so only request member reads are still refused.) It was also caught at workflow
publication, because the data-selection guard reads payloads, not `Source` (`WP/ActivityTemplateDataSelectionGuard.cs:48-53`),
and refuses an unknown producer. (d) A replacement
`IRuntimeActivityInputMaterializer` or `IRuntimeInputBindingResolver` built before this change (both registered with
`TryAdd`, `WR/Extensions/RuntimeCoreServiceCollectionExtensions.cs:524-525`) could read `binding.ActivityResult` and
ignore the selection: closed by keeping member kinds away from the replaceable resolver (§7) and by tying the
capability to the built-in materializer (§8). (e) Before #3948 an old compiler drops `memberPath` on `ActivityResult`;
new clients never write that shape, and current main refuses it in the analyzer; the new compiler refuses it too.

### 4. Validation and typing (T012, T013)

**Parsing.** Authoring and publication parse member kinds in one place each: `ActivityDataReadBinding.Direct`
(today's refusal at `:106` becomes a parse of the old identity payload plus `memberPath`), and a new
`RuntimeInputBindingCompiler` branch before the expression fall-through (`:422-457`). Both refuse a missing, empty,
non-array or non-string path, a null segment, more than 64 segments or the size bound, and any field of the wrong
role. The compiler also refuses `memberPath` (and `path` outside `WorkflowRequest`) on old kinds itself, so admission
does not depend on the analyzer alone.

**Typing.** At publish the analyzer asks the shape source for the stored path, with limits sized to it (path
traversal is not depth-limited, only the subtree built below the leaf is: `ClrSerializedShapeReader.cs:37-66` versus
`:83-86`), and classifies:

| Selected shape | Outcome |
|---|---|
| `Known` (or `Truncated` below the leaf) | `Known`: pin leaf type, representation, `mayBeAbsent` (any optional prefix or dictionary step), `mayBeNull` (any nullable prefix). |
| `member-not-declared` on a known object | Refuse statically (new `ActivityData/UnknownMember`). |
| `member-parent-not-object` (scalar or standard collection parent) | Refuse statically (new `ActivityData/MemberParentNotObject`). |
| `dynamic-json`, converter, number-handling or polymorphic opaque, other opaque reasons | `DeferredFit`. |
| No owner, `metadata-unavailable`, `type-not-admitted`, `serialization-profile-unbound` | `DeferredFit` (uncertain, not absent; as the whole-value rule at `ActivityDataSelectionAnalyzer.cs:360-361`). |
| `projection-mismatch`, `no-typed-result`, undeclared projection | Existing refusals (`UnknownProjection`). |

Today the reader reports a known-absent member as `Unknown` with a reason code (`ClrSerializedShapeReader.cs:59-60,279-284`);
keying refusal on reason text is brittle (decision 5). Root nullability for `$result` is passed as `false`
(`ClrActivitySerializedShapeSource.cs:137-141`). That holds for CLR activities: completing a typed result with null
faults the producer with VF-ACT-006 and its reader never runs (run:
`InputReadFailureCharacterizationTests.A_typed_result_cannot_be_committed_as_null_so_its_reader_never_runs`). Runtime
still never trusts `Known` presence or nullability for safety: facts narrow publication and plan conversion; runtime
re-checks (§7).

**Offer equals admission.** The same `Resolve` call decides a browsed member and a stored one, as for scope-free
results (`ActivityDataSelectionAnalyzer.cs:276-312`). For a query with a path it must return the **member** binding,
echoing the exact segments, when admitted, and a null binding with a reason otherwise; today it returns the
whole-value binding (`:147-153`).

**How pinned facts reach conversion.** The linker cannot own pinning: it runs only in the workflow compiler
(`WorkflowExecutableCompiler.cs:290-291`), not in template compilation or activity-draft test runs (no other call
site). And the workflow compiler compiles bindings (`:239`) before the analysis (`:245`). Recommendation: the
compiler emits `DeferredFit` selections (honest, and safe if a path forgets the next step); a small internal pin
pass, run right after the analysis in both `WorkflowExecutableCompiler` and `ActivityTemplateCompiler`, upgrades
admitted paths to `Known` from facts the analysis returns per input; the linker then plans conversion from
`Selection.SelectedType` instead of the projection type. `DeferredFit` gets no plan; an explicit, non-Auto
conversion request on a `DeferredFit` read is refused at publish (binding contract §3). Intrinsic member reads stay
without a plan (`ExecutableNodeCompiler.cs:446-447`) and are checked by the intrinsic payload validation.
(Corrected in slice D1: the linker plans activity results only, so it can refuse this only for result member reads.
A variable member read is always deferred and the linker never sees a conversion request on it: whole-value
`Variable` compilation drops the authored conversion entirely, `WP/RuntimeInputBindingCompiler.cs:619`. The
refusal for variable member reads is therefore in the binding compiler, as
`ValueConversionRejectionReason.SelectedMemberContractUnknown`. Whole-value `Variable` behaviour is unchanged.)

**As delivered in slice C (result member reads).**
- *Known-absent classification (decision 5):* the CLR reader is unchanged; the analyzer refuses on the shape's stable
  reason codes `activity.shape.member-not-declared` (`ActivityData/UnknownMember`) and
  `activity.shape.member-parent-not-object` (`ActivityData/MemberParentNotObject`), so the data-context endpoint
  reports an absent path exactly as before. A selected shape that is `Known` or `Truncated` with a type and a profile
  revision is pinned; anything else is admitted as `DeferredFit`.
- *Malformed paths* are `ActivityData/InvalidMemberPath` (missing, not an array, empty, a non-string or null segment,
  more than 64 segments, more than 4 096 characters); a scope or a `path` on the read is `ActivityData/InvalidSource`.
  Both are analyzer findings. The binding compiler applies the same parse and, because it runs before the analysis,
  raises the same finding itself as an `ActivityDataSelectionRejectedException`; it also refuses a `memberPath` (and a
  `path` outside `WorkflowRequest`) on a whole-value kind as `ActivityData/UnsupportedMemberSelection`.
- *Facts reach the pin pass* through `ActivityDataSelectionAnalysis.MemberReads`, one entry per admitted read, keyed by
  the input path and matched on producer, projection and exact segments; anything but exactly one matching fact leaves
  the read deferred.
- *Where the pin pass runs (corrects the recommendation above):* only in `WorkflowExecutableCompiler`, for reads in the
  workflow's own body. A read inside a reusable activity's body is published `DeferredFit` and checked at run time. The
  template providers (Graph and Agent) return no compiled member reads at template compilation: a Graph body is
  compiled from its authored occurrences only when the template is placed, after the template's analysis, and the
  workflow's analysis does not look inside a placed body; placed body nodes also carry their dependency's authored id,
  not the body's. So the template compiler has no pin call. Placement still remaps the producer and keeps the segments,
  and an explicit conversion on such a read is refused (`SelectedMemberContractUnknown`). Pinning at placement, with
  facts keyed by occurrence id, is a follow-up.
- *Intrinsics:* a result member read on an intrinsic input compiles with no source plan, like a whole result read, and
  is planned by the linker when its member is known.
- *Linker:* an explicit conversion on a `DeferredFit` read is refused with the new
  `ValueConversionRejectionReason.SelectedMemberContractUnknown`.

### 5. `ActivityResultMember` and the scope-free rule

A member result read is admitted exactly like a scope-free `ActivityResult` read (binding contract §1 conditions
1–4, the same `Resolve` code), and then by its path (§4). `isOptional` keeps its producer-occurrence meaning; it
never means "member optional".

**Recommendation: `ActivityResultMember` never declares a scope.** The contract makes `producerScopeId` "optional
and legacy" on both kinds (binding contract §1; data-model row). There is no legacy for a new kind: no stored
document can carry one. Accepting a scope would import into the new kind the stale and Unavailable reads that #4053
documented for declared scopes (this file, "Activity-result bindings without a scope"), with nothing gained, since
the resolver already ignores a null scope (`WR/Services/Values/CausalActivityResultResolver.cs:78`). The code-first
builder writes no member kind (`Essentials/src/Workflows/Design/Core/Authoring/WorkflowValue.cs:28-85`). So:
authoring and the compiler refuse a `producerScopeId` on `ActivityResultMember` as `ActivityData/InvalidSource`, and
`ValidateCanonical` refuses a non-null `ProducerScopeId` with that source. Contract text to change: binding contract
§1 "`producerScopeId` is optional and legacy on both `ActivityResult` and `ActivityResultMember`" → "on
`ActivityResult` only; `ActivityResultMember` declares none, and one that does is refused"; the data-model table row
for `ActivityResultMember` drops "optional legacy `producerScopeId`".

### 6. Hashing (T013)

Old arms of every hash stay byte-identical; only new kinds add content.

- **Node payload** (`WorkflowExecutableHasher.cs:123-146`). New arms, with prefixes no old arm uses:
  `request-member:{F(memberKey)}:{S}`, `variable-member:{F(scope)}:{F(key)}:{S}`,
  `result-member:{F(producer)}:{F(projection)}:{isOptional}:{S}`, where `F(v)` is `{length}#{v}`. **Every
  variable-length field is framed**, not only the segments (design review, #4083): these bytes are new and can never
  change later, so no member key, scope, producer, projection or segment may contain a delimiter that makes two reads
  render alike. `S` is `sel:1:{mode}:{n}:` then `F(segment)` for each segment, then
  `:{F(FormatType(selectedType)) or -}:{mayBeAbsent}:{mayBeNull}:{representation}:{F(profileRevision) or -}`; the
  booleans and enum names are fixed vocabulary. The default arm is a throw; it is reachable only by an unknown kind
  (each kind has an arm, and `Literal` always has its payload). Delivered in slice A.
- **V2 behavioural payload** (`:375-380`): no code change; `selection` enters `contract` through the
  source-generated context. A per-field mutation test proves every selection field moves the hash, so a future
  `[JsonIgnore]` cannot drop one silently.
- **V1 historical** (`:41-60`): no V1 artifact can hold a member kind; its rendering refuses one rather than
  attempt a match.
- **Template behaviour hash:** no code change (default serialization of the whole root plus `runtimeRequirements`,
  `ExecutableActivityTemplateBehaviorHasher.cs:21-32`). This corrects T013's file list: that hasher needs tests, not
  edits.
- **Snapshot binding fingerprint** (`RuntimeActivityInputMaterializer.cs:620-646`): append `{key, Selection}` only when
  present, as secrets are.
- **Excluded:** labels, display order, the shape's metadata-asset revision (decision 7). Included: segments, mode,
  selected contract, profile revision, and the conversion plan fingerprint already in the line.

Proof that whole-value hashes cannot move: old arms and their inputs are unchanged; the new property is omitted
when null from both serializer-based hashes; the fingerprint enumerates explicit properties (`:624-636`). Pinned
controls that must stay green unchanged: `ScopedActivityResultHashTests.cs:40-41` (node payload and template),
`WorkflowExecutableCompilerGoldenTests.cs:55` (compiled corpus bytes and hashes), `WorkflowExecutableHashContractTests.cs:15-19` (V1).

### 7. Runtime resolution and materialization (T014, T015)

**Where selection runs.** Outside the replaceable resolver. For a member binding the materializer and the intrinsic
executor derive, once per node (in the existing `PreparedInputs` cache, `RuntimeActivityInputMaterializer.cs:205-221`),
the whole-value binding of the same role (`WorkflowRequest` with no legacy path, `VariableRead`, `ActivityResult`),
resolve it through `IRuntimeInputBindingResolver` exactly as today, then apply one internal selector to the
resolved envelope. Role, causal and projection resolution, `RequireReadable` and projection policy are therefore
unchanged (`RuntimeInputBindingResolver.cs:52-55,80-131`). A replacement resolver never sees a member kind; the
built-in resolver keeps throwing for one (`:33`). This mirrors how secret reads bypass the resolver
(`RuntimeActivityInputMaterializer.cs:288-292`).

**Order** (binding contract §4): resolve root → withheld root refuses (existing) → transient-resource root refuses
selection → inline value, or for an external root read the payload through `IExternalPayloadStore`
(`:483-505`), apply the declared projection path with the existing rule, then the exact segments → presence and
nullability → fit and conversion → existing destination storage (`:394-414`) → snapshot. Selection on an external
value loads the whole payload, as external projection already does (`:429`); there is no partial read, so cost is
one full read per consumer materialization, bounded by the store's own limits. Encryption-required values are
already withheld and refuse at `RequireReadable`.

**Traversal.** Ordinal `JsonElement.TryGetProperty` per segment, never the case-insensitive fallback
(`RuntimeInputBindingResolver.cs:172-187`); a dot is part of a name; `"0"` is a key. Dictionaries are JSON objects,
so they need nothing special. Run (`Essentials/tests/Activities/Runtime/Tests/ActivityValueJsonTests.MemberNameMatching.cs`):
on a duplicate key both this lookup and input hydration take the last value; literal names with dots, digits, padding
or no characters match as written. They differ on case: the activity-value profile hydrates case-insensitively, so a
payload carrying `CustomerName` hydrates a whole DTO, while the exact segment `customerName` reads `Absent`.
That is the contract's choice (exact names) and is what makes CLR-seeded request members fragile (§9).

| Observation | Result | Surfaces as |
|---|---|---|
| Required producer missing or ambiguous | Existing `CausalActivityResultResolutionException` (`CausalActivityResultResolver.cs:85-91`). | The StartActivity work item is poisoned (`DoNotRetry`); consumer stays `Scheduled` with no snapshot; run stays `Running`; one blocking incident of type `SchedulerWorkPoisoned` names the consumer, with the cause in its message and in `runtime.faultType`/`runtime.faultMessage` metadata (run: `InputReadFailureCharacterizationTests.A_failed_causal_input_read_blocks_the_reader_with_one_poison_incident_before_it_starts`). |
| Optional producer missing | Root `ExplicitNull`, as whole-value reads (`RuntimeInputBindingResolver.cs:86-93`); destination rules decide (decision 8). | As today. |
| Member absent | `Absent`, with failed segment index. | Accepted only for an optional nullable input (`RuntimeActivityInputMaterializer.cs:605-612`); otherwise new `VF-SEL-001`. |
| Leaf null | `ExplicitNull`. | Nullable input accepts; otherwise `VF-SEL-002` naming the leaf. |
| Null before the last segment | Stop; `ExplicitNull` with origin segment. | Same rule, `VF-SEL-002` naming the prefix. |
| Array or scalar before the last segment | Selection fails. | `VF-SEL-003`. |
| Value that does not fit the target | Fails before snapshot. | `VF-SEL-004`. |

All of these throw from input materialization before the snapshot commit and before construction, so they take the
same scheduler-poison path as the causal failures; the producer's committed completion is untouched. The incident
type is always `SchedulerWorkPoisoned`, so a new failure is told apart only by its exception type in
`runtime.faultType` and its code in the message text: the selector should throw one dedicated exception type whose
message starts with the `VF-SEL` code.

**Fit.** A bounded internal helper in Workflows Runtime, using the unchanged activity-value profile and the pinned
target and nullability, runs before the snapshot for **every** member read, `Known` included (decision 9): `Known`
facts are trusted for planning, not for safety. The existing hydrator check stays as later defence. Intrinsic reads
reuse `ValidateAssignmentPayload` (`WorkflowIntrinsicExecutor.cs:721-729`); its external path (`:673-712`) and
`HasExternalProjection` (`:741-743`) need the same selector.

**As delivered in slice B (corrects the text above where they differ).**
- One internal reader, `RuntimeMemberRead`, serves the materializer and the intrinsic executor; the selector is
  `RuntimeReadSelector`. The whole-value binding is derived per read, not cached in `PreparedInputs`.
- *Name matching* enumerates the object's members with `JsonProperty.NameEquals` (ordinal) and keeps the last match,
  so duplicate-key behaviour does not depend on `TryGetProperty`'s handling of repeated names.
- *Protection:* a source whose policy requires encryption is refused with `VF-SEL-003` rather than read, because the
  runtime only ever holds such a value withheld; a withheld source is refused exactly as its whole-value read is; a
  live resource is refused with `VF-SEL-003`. The selected member carries the binding policy combined with the
  source's, so it is never less protected than the whole value.
- *External sources* are read whole through `IExternalPayloadStore`. A named result projection on an external result
  uses the existing external rule (dotted path, case-insensitive fallback) before the exact segments. The selected
  member is stored through the input's normal destination storage with the source's storage profile as fallback; it
  is not forced external.
- *Codes:* `VF-SEL-001` absent (or an absent source) where the input is required or not nullable; `VF-SEL-002` null
  source, prefix or member where the input is not nullable (an optional producer that did not run reads as a null
  source, decision 8); `VF-SEL-003` a segment applied to an array, string, number or boolean, or an unselectable
  source; `VF-SEL-004` a conversion failure or a member that does not deserialize into the input type. An optional and
  nullable input reads an absent member as `Absent` and a null one as `ExplicitNull`. The one exception type is
  internal, `RuntimeReadSelectionException`; the scheduler records its code as `runtime.inputFailureCode` and the input
  as `runtime.inputKey` on the poisoned work item, beside the fault type and message. An intrinsic always refuses an
  absent member and writes a null member as null.
- *Fit* needs the type registry for a typed input; `Object`, `Elsa.Any` and `JsonElement` inputs accept any JSON.
- `RequiresActivityHistory` now treats request and variable member reads as their whole-value roles.

**Snapshots and retries.** The snapshot stores the selected, converted value. Retry successors copy the pinned
snapshot values unchanged (`WR/Services/Alterations/ActivitySuccessorStaging.cs:64-66`), so a retry never
re-selects. Composites that re-materialize inputs on child completion
(`WR/Services/Values/RuntimeActivityInputSnapshotMaterializer.cs:7-14`) and structure inputs
(`RuntimeActivityInputMaterializer.cs:223-269`) go through the same method, so they select too. Two existing
predicates need updating: `RequiresActivityHistory` lists non-history sources positively, so new request and
variable member reads would over-fetch history (`:38-42`, harmless); `UsesExecutionIndependentBindings` is a
positive list and correctly excludes member reads (`:34-36`).

### 8. Capability and requirements (T016)

Today a requirement is a `RuntimeRequirement(consumerKey, schemaVersion)` (`Essentials/src/Activities/Runtime/Core/Models/RuntimeActivityDescriptor.cs:30-44`).
Executables and templates derive one per node descriptor in their constructors
(`WRC/Models/WorkflowExecutable.cs:118-124`; `WRC/Models/ReusableActivityRuntimeModels.cs:45-50`).
`RuntimeRequirementChecker` answers them from `IRuntimeActivityConsumerCapability` registrations only
(`WR/Services/Executables/RuntimeRequirementChecker.cs:30-62`); the intrinsic engine capability is the precedent for
an engine marker (`WR/Services/Values/IntrinsicActivityConsumerCapability.cs:24-31`). Consumers: Publishing preflight
(`Essentials/src/Workflows/Publishing/Api/Services/RuntimeRequirementPreflight.cs:111`), artifact reconciliation
(`Essentials/src/Workflows/Runtime/Reconciliation/Elsa.Workflows.Runtime.Reconciliation/Services/WorkflowArtifactReconciler.cs:903`),
distributed placement (`ExecutionPlacementGate.cs:149`).

**Design.** Derive `elsa.runtime.input-member-selection@1` in the same two constructors whenever any node has a
binding with a selection. Derivation cannot be forgotten by a compiler, follows placement into workflow
executables automatically, and never touches an artifact without a selection. Advertise it with a
`InputMemberSelectionCapability` registered beside the materializer, plus a composition check (precedent:
`WDV/Internal/DataSelectionAnalyzerCompositionCheck.cs`) that fails startup when the capability is present but
`IRuntimeActivityInputMaterializer` is not the built-in one (decision 10). Guard with the one checker at the front
doors, before an execution is created or a bookmark consumed: `WorkflowStartDispatcher` after its executable read
(`WR/Services/Executions/WorkflowStartDispatcher.cs:186`), `BookmarkResumeDispatcher` after its read and before
consumption (`WR/Services/Bookmarks/BookmarkResumeDispatcher.cs:123`); and once per artifact in the drain read
(`WR/Services/Scheduler/PinnedExecutableRead.cs:14-36`, used by every scheduler handler, including
`Essentials/src/Activities/Runtime/Elsa.Activities.Runtime/Services/WorkflowResumeBookmarkSchedulerWorkHandler.cs`),
caching the verdict with the burst-cached executable. In one new binary the guard can only fail when composition
removed the capability; across versions the enum fence (§3) and placement (`Unresolved`) do the work. An old
runtime cannot be retrofitted; it refuses by failing to read.

**As delivered in slice B (corrects the paragraph above).**
- *Advertisement:* `InputMemberSelectionCapability` reports schema `1` only while the composed
  `IRuntimeActivityInputMaterializer` is the built-in `RuntimeActivityInputMaterializer`; a host that replaces it
  advertises nothing (`WR/Services/Values/InputMemberSelectionCapability.cs`). This replaces the composition check of
  decision 10: a replaced materializer stays a valid host for every executable without member reads, instead of
  failing startup.
- *Guards:* `SchedulerWorkHandlerHelpers.EnsureMemberSelectionSupported` asks the one `IRuntimeRequirementChecker`
  about the member-selection requirement only, and refuses when it is not `Available` or when no checker is composed.
  It runs at every activity start (`WorkflowStartActivitySchedulerWorkHandler`, so the root never starts) and at
  bookmark resume before the bookmark is consumed (`WorkflowResumeBookmarkSchedulerWorkHandler`). Every start,
  dispatcher-made or queued, reaches the root's activity start, so the dispatchers and the drain read need no guard of
  their own; guarding the shared drain read would also have turned distributed placement's precise "requirement
  unmet" into "cannot be read". A refusal is a poisoned work item and a blocking incident naming the requirement; the
  Start work item may already have written the execution's started checkpoint, but no activity runs. Other
  requirement kinds keep their existing enforcement: checking them all here would refuse executables whose descriptor
  consumers advertise no capability today.

### 9. Other source families

| Kind | Differs from the result case | Shape facts |
|---|---|---|
| `VariableMember` | Requires an explicit `declaringScopeId`; legacy `Variable` defaults to the workflow scope (`RuntimeInputBindingCompiler.cs:619-632`, `ParseVariableReference`). Current items are the owning structure's scoped variables. Placement remaps the scope as for `VariableRead` (`PlacedReferenceRemapper.cs:83-99`, `RemapScope`). Delivered in slice D1. | Always `DeferredFit`: a declared type yields `serialization-profile-unbound` (`ClrActivitySerializedShapeSource.cs:151-165`); variables can hold JSON from other writers. |
| `WorkflowRequestMember` | Payload `memberKey` only; legacy dotted `Path` must be null. Owner14Nov correction: the request root must name a declared workflow input; an empty declaration list is a closed v1 contract, so undeclared roots are refused before publication. | Always `DeferredFit`. A CLR value supplied at start is serialized with default JSON options (PascalCase) by `WorkflowExecutionStartCommandPayload.ToJsonValues` (`Essentials/src/Workflows/Runtime/Core/Models/WorkflowExecutionStartDispatch.cs:710-713`). That one function seeds both request inputs (`WR/Services/Executions/WorkflowStartDispatcher.cs:467`) and start-time variables (`:503`), so a CLR-seeded request **or variable** may carry `CustomerId` where an author types `customerId`: exact segments then read `Absent`. Rule chosen (slice D): exact stored names; normalising CLR seeding is a separate follow-up. |
| `InputMember` | Authored only. Placement must lower it to `WorkflowRequestMember` with all segments; `Input` reads are lowered by `InputReadLowering` (`WP/InputReadLowering.cs`), keyed by reference key in a placed body (`ActivityTemplatePlacer.ToGraphInputRead`) and by the declared input's name at workflow level (`ExecutableNodeCompiler`, #4087, §11 item 4); slice D2 extends it so `InputMember` lowers to `WorkflowRequestMember` with `memberPath` unchanged, in both places. Delivered in slice D2. | Always `DeferredFit`. |

None of the three can be `Known` today, so all depend on the runtime fit helper, and the issue's acceptance names
only activity results. They belong to this leaf (T012 lists all four) but in a later slice than
`ActivityResultMember`.

### 10. Slicing

Order: model, then runtime, then publication. Publication before runtime would let a server publish artifacts no
runtime can run: half-enabled and reachable.

| Slice | Adds | Inert until the next slice because | Proving tests | Risk |
|---|---|---|---|---|
| A. Member-read model (T011; hashing part of T013; requirement derivation of T016; model and hash part of T017) | Enum members, `RuntimeReadSelection`, constructor overload and invariants, `Selection` serialization, hash arms with throwing default, fingerprint append, inspection view, derived requirement, PublicAPI. | Nothing compiles a member binding; the resolver throws for one; no capability is advertised, so preflight, import and placement report the requirement unmet. Locally there is no front-door guard until slice B: a hand-built member executable runs up to its reader, whose input read is refused before construction (run: `MemberReadRefusalTests`). | Invariant rows; round trip; corpus goldens unchanged; distinct hashes for distinct paths, modes, types and delimiter-bearing names; old-reader controls (§11); requirement derived for member, absent for whole. | Low–medium: public shape and hash rendering. Delivered (receipt in [tasks](tasks.md)). |
| B. Runtime selection (T014, T015, capability and guards of T016, runtime part of T017) | Selector, materializer and intrinsic integration inline and external, fit before snapshot, reason codes (one dedicated exception type, `VF-SEL` code first), capability with composition check, front-door and drain guards, and the `RequiresActivityHistory` update so request and variable member reads stop fetching activity history (§7). | Still no publication path emits member bindings; only hand-built executables reach it, and those now run correctly. | Unit rows for every outcome in §7, exact and literal names; inline/external parity; intrinsic; constructor counter and no snapshot commit on failure; retry reuse; real engine harness in a Sequence and a top-level Flowchart; guards refuse start, resume and queued work without the capability. | High: materialization order, external values, protection. Delivered: capability by composed materializer rather than a composition check, guards at activity start and bookmark resume (§8); receipt in [tasks](tasks.md). |
| C. Publish result member reads (T012, T013, T017 for `ActivityResultMember`) | Authored parse, analyzer offer and admission, compiler branch and old-kind refusal, pin pass, linker on the selected type, remapper arms, contract text (§5). | Enables the feature for API clients; the Studio member picker (T022) stays gated in its own leaf. | Offer-equals-admission rows; refusal before any write through the real publish handler; template placement; real host publish → run in a Sequence and a top-level Flowchart, inline and external (acceptance 1). | Medium–high. Delivered behind the `MemberReads` setting, off by default (§3 "As delivered in slice C"); receipt in [tasks](tasks.md). |
| D. Other families (T012, T013 for the rest) | `VariableMember`, `WorkflowRequestMember`, `InputMember` and its placement lowering. | Each kind refused until its row lands. | Deferred-fit rows; placement lowering keeps segments; reopen and publish. | Medium. Split: D1 `VariableMember` (delivered, receipt in [tasks](tasks.md)); the #4087 fix (delivered); D2 `WorkflowRequestMember`, `InputMember`, the code-first rename (decision 12), delivered. |

### 11. Open decisions and what reading could not establish

| # | Decision | Recommendation | Strongest argument against |
|---|---|---|---|
| 1 | Runtime model | New enum members (§2). | Many `Source ==` sites to update; a selection on old sources touches fewer lines. |
| 2 | Scope on `ActivityResultMember` | Refuse it (§5). | Departs from approved contract text and from symmetry with `ActivityResult` in copy and remap code. |
| 3 | Where selection runs | Outside the replaceable resolver, on a derived whole-value binding (§7). | A replacement resolver that wanted to serve member reads cannot; one more binding per node. |
| 4 | Pinning point | Compiler emits `DeferredFit`, a pin pass after analysis upgrades (§4). | A third pass and a new per-input fact on the analysis result; reordering analysis before compile would avoid the pass but change which error wins. |
| 5 | Known-absent classification | Have the CLR reader return `Unavailable` for `member-not-declared` and `member-parent-not-object`, and refuse on state. | Changes what the data-context endpoint reports for an absent path today (`Unknown`). |
| 6 | Possibly null or absent into a non-nullable or required input | No static refusal; runtime decides, as whole-value reads do. | The contract says known-incompatible contracts fail publication; an author learns only at run time. |
| 7 | Contract identity in the hash | Profile revision only, not the metadata-asset revision. | A metadata re-scan that changed facts in a way the hashed fields miss would not change identity. |
| 8 | Optional producer missing | Keep `ExplicitNull`, as whole-value reads. | The issue wants missing producer and null "reported distinctly"; here they differ only in diagnostics. |
| 9 | Fit for `Known` reads | Always run the fit check before the snapshot. | A second deserialization per member read; whole-value reads do not pay it. |
| 10 | Capability and replaced materializer | Decided in slice B: advertise only while the built-in materializer is composed, so a replaced one advertises nothing and member executables are refused before they run (§8). Was: fail composition. | A custom materializer that delegates correctly to the built-in one cannot advertise; an explicit opt-in can come later. |
| 11 | Path size bound | 64 segments (existing query bound) and 4 096 UTF-16 code units in total. | Arbitrary; a long dictionary key could exceed it where JSON itself would not. |
| 12 | Name clash | Was: keep the code-first builder's `WorkflowRequestMember<T>` (`WorkflowValue.cs:28`), which writes the legacy dotted `WorkflowRequest` path (`WorkflowBuilder.cs:117-123`), and document it. Reversed and done in slice D2: the code-first type is now `WorkflowRequestRead<T>` (`WorkflowValue.cs:28`; Design Core has no PublicAPI baseline). | Same name, different wire meaning, in the same product as the new discriminant (ADR 0080 D5 plain names). |

**Where the spec disagrees with the code or with #4053/#4059.** Binding contract §1 and the data model keep an
optional legacy scope on `ActivityResultMember` (decision 2). Binding contract §6 says an old compiler can publish an
unusable expression artifact from a member kind; on current main, since #3948, publication refuses it before any
write (§1). Data model "Protection minimum" has no facts to pin (§2). Tasks T013 point pinning at the linker, which
runs only in the workflow compiler (§4), and list `ExecutableActivityTemplateBehaviorHasher` for edits it does not
need (§6).

**Characterised on main (`a9771131c3`), after this note was first written.** Each item below was open; the tests
named pass on main and pin current behaviour. Rows that slices A–C deliberately change carry a comment in the test.

1. *Older binary reading a member executable.* Stand-in only: the current EF store, with its own options, refuses an
   unknown source name or number as a corrupt payload, and silently drops a `selection` on an old kind
   (`EfRuntimeArtifactScopeTests.MemberSourceReads.cs`). A real older build was not run; that stays open.
2. *Older non-distributed runtime, unreadable executable.* Stand-in: a throwing executable reader. The work item is
   poisoned without retry and the run blocked or faulted (§3). Destructive, so the upgrade rule in §3 applies.
3. *How a failed input read surfaces.* Scheduler poison, `SchedulerWorkPoisoned` incident, cause only in text and
   metadata (§7 table).
4. *Workflow-level `Input`.* Published as an expression of language `Input`, not lowered to a request read. A
   composed host does not refuse it either: the expression validator skips reference kinds such as `Input`
   (`WDV/Validators/ExpressionDraftSemanticValidator.cs:30-38`). At run time the materializer hands the expression to
   the portable evaluator, which refuses it for having no capability profile, so the reader's input read is poisoned
   before it starts. The analyzer offered `Input` for workflow documents, and Studio's `Input` reference editor writes
   it. Filed as #4087 and **fixed**: publication lowers a workflow-level `Input { referenceKey }` read to
   `WorkflowRequest { memberKey = name of the declared input with that reference key }` in `ExecutableNodeCompiler`,
   for ordinary, structure-projected and intrinsic inputs, through `InputReadLowering`, the helper the placer's body
   lowering also uses. A reference key that names no declared input is left as authored and refused as
   `ActivityData/UnknownInput`. A workflow now offers the request read; a reusable body still offers `Input`. Run end
   to end with the input supplied and with it omitted and defaulted (`WorkflowInputReadPublicationRunTests`).
   Consequence for slice D2: `InputMember` lowers to `WorkflowRequestMember` through the same helper, keyed by name
   at workflow level and by reference key in a placed body; at workflow level the analyzer offers
   `WorkflowRequestMember`.
5. *Typed whole result as null.* Refused at completion (VF-ACT-006); the note's assumption holds (§4).
6. *Conversion.* `Elsa.Any`, `Object` and `JsonElement` sources cannot be planned onto `String` (VF-COER); `Elsa.Any`
   onto `Elsa.Any` is identity. An identity plan checks only the envelope's type label and passes a number, object or
   array labelled `String` unchanged (`DynamicSourceConversionCharacterizationTests.cs`). This confirms that
   `DeferredFit` gets no plan, and strengthens decision 9: without the fit check a wrong payload under a `Known` plan
   reaches the snapshot.
7. *Name matching.* Last wins on duplicates in both lookup and hydration; case differs (§7).
8. *Every executable-producing path refuses before any write.* Workflow publish, version and draft-snapshot test
   runs, reusable-activity template compilation (which activity-draft test runs call before saving,
   `ActivityDraftTestRunService.cs:191,318`) and BPMN compilation all refuse every member kind and a member path on
   an old kind in the shared analyzer (`PublishWorkflowRequestHandlerTests.MemberReadAdmission.cs`,
   `WorkflowTestRunRequestHandlerTests.MemberReadAdmission.cs`, `BpmnDataSelectionPublicationTests.cs`). Elsa 3
   import publishes through the same `PublishWorkflow` request (`Extensions/Elsa3/src/Activities/Design/Import/Reconciliation/Services/ImportedWorkflowPublisher.cs:95-96`;
   read, not run). The binding compiler on its own still turns a member kind into an expression and drops a path on
   an old kind; only the analyzer refuses. A test run's rejection reason carries only the generic "One or more
   activity data references are invalid." and loses the finding code and path, unlike publication.
9. *Node-payload hash default arm (§3).* Not expressible on main: no constructor or reader admits an unknown source,
   so it is left to slice A's own hash tests.

None of this changes a decision or a slice boundary. It adds two obligations: slice B throws one dedicated exception
type with the `VF-SEL` code first (§7), and slice C states the shared-store upgrade rule (§3).

## Studio member picker (#4309) design note

Scope: T018–T025 for member reads, on top of the #3094 whole-output chooser. Design only. Every claim below was read
from main at `e5399c29f5`. Paths are shortened: `SC` = `Studio/src/essentials/Elsa.Studio.Workflows/Client/src`,
`WDV` = `Essentials/src/Workflows/Design/Validations/Elsa.Workflows.Design.Validations`.

### 1. How Studio learns that member reads are on

**Today nothing tells it.** The one data-selection capability, `elsa.api.activity-data-selection` v1, carries only
the two context links (`Essentials/src/Workflows/Design/Api/Capabilities/WorkflowActivityDataCapabilitySource.cs:11`,
`Essentials/src/Activities/Design/Api/Capabilities/ActivityDataSelectionApiCapabilitySource.cs:12-13`). The
`MemberReads` setting is read only by the analyzer (`WDV/Services/ActivityDataSelectionAnalyzer.cs:116,234`) and
bound by the feature (`WDV/WorkflowDesignValidationsFeature.cs:53,60`). The runtime requirement
`elsa.runtime.input-member-selection@1` is checked only by publish preflight, import and placement (§8) and is never
shown to Studio.

The capability cannot be reused with a dormant flag. A single dormant contribution makes the whole capability dormant
(`Essentials/src/Api/Capabilities/Elsa.Api.Capabilities/Services/ApiCapabilityCatalog.cs:91-110`), which would also
switch off whole-output Choose data.

**Studio cannot safely work it out by probing.** When the setting is off, a focused query that has a member path does
not become a member read (`ActivityDataSelectionAnalyzer.cs:116`). The source then resolves as the *whole* read, while
its shape is described *at the member path* (`:174`, `Shape` at `:441-462`). The answer looks like a member offer but
carries a whole-value binding. A client that stores the server's binding unchanged would silently store the wrong
read.

**The smallest server addition** (owner decision A):

- *A1 (recommended).* Add a fact `MemberReads` (bool, init) to `ActivityDataSelectionAnalysis`
  (`Essentials/src/Workflows/Design/Validations/Core/Models/ActivityDataSelectionModels.cs:80-92`). The analyzer sets
  it from its own options. It reaches both context endpoints with no new wiring, because both return the whole
  analysis (`Essentials/src/Workflows/Design/Api/Models/ActivityDataContextViews.cs:20-30`;
  `Essentials/src/Activities/Design/Api/Services/ActivityDataSelectionContextService.cs:137-139`). The answer comes
  from the component that admits the read, per document, and an older server that omits the field reads as false.
  Studio already queries before it shows anything, so it needs no extra round trip.
- *A2.* Add a link-free capability `elsa.api.activity-data-member-reads` v1, declared only while the setting is on, and
  read with the existing `hasApiCapability` (`SC/api/capabilities.ts:93-96`). This matches the "existing capability
  discovery" wording of T018. Its cost is placement: the setting lives in `WDV`, which references neither
  `Elsa.Api.Capabilities` nor the Design API (`WDV/Elsa.Workflows.Design.Validations.csproj:10-13`), and the Design API
  references only `Validations.Core` (`Essentials/src/Workflows/Design/Api/*.csproj:24`). One project would need a new
  reference, or a fact interface would have to move into `Validations.Core`.

Under either option the slice also fixes the off-state trap. With the setting off, a non-empty member path on a focused
query adds `ActivityData/UnsupportedMemberBinding` and returns no source, the same refusal a stored member binding gets
(`:234-237`). Studio also checks the kind defensively: it stores a member offer only when the returned binding's
`expressionType` is the member kind it asked for.

Whether a runtime can run the read stays out of Studio. The setting's own text says to turn it on only when every
runtime can run member reads (`WDV/WorkflowDesignValidationsFeature.cs:52`), and publish preflight refuses an artifact
whose requirement is unmet (§8). Studio shows that refusal as an ordinary publish blocker.

### 2. What the context endpoint returns for member facts

- **Discovery** (no source, empty path) lists declared inputs, visible variables and result projections
  (`ActivityDataSelectionAnalyzer.cs:127-159`). Each source carries `id`, `displayName`, `scopeId`, `binding` (null
  plus `reasonCode` when it cannot be chosen), `availability`, `shape` and `activityResultSource`
  (`ActivityDataSelectionModels.cs:66-77`).
- **The shape** is `ActivitySerializedShape`: `State` Known/Unknown/Truncated/Unavailable, `Kind`
  Scalar/Object/Map/Collection, `Type`, nullability, presence, representation, sensitivity, `Members` (literal
  case-sensitive `SerializedName`), `Element`, `MapValue` and `ReasonCode`
  (`Essentials/src/Activities/Design/Core/Models/ActivitySerializedShape.cs:7-74`). `ShapeLimits` bound the whole tree
  with one global member budget, and a literal path is never split or truncated (`:76-81`). The analyzer spends that
  budget across the page (`ActivityDataSelectionAnalyzer.cs:170-176`).
- **Known versus deferred:** only result members can be Known. A declared type, which covers variables, inputs and
  request members, gives `activity.shape.serialization-profile-unbound`
  (`Essentials/src/Activities/Design/Reconciliation/Clr/Services/ClrActivitySerializedShapeSource.cs:151-165`, §9). Owner14Nov correction: undeclared workflow request roots are refused (`ActivityData/UnknownInput`); declared request/input and variable members remain deferred and offer literal segments.
- **A focused member query** (`source` plus `memberPath`) returns one source whose `binding` is the member read and
  whose `shape` is the member's (`:103-126,174`).
- **Type fit is not reported at design time.** Neither discovery nor focused analysis compares a source or member type
  with the target type. Discovery reason codes are availability and contract codes only
  (`SC/activityDataSelection.ts:233-278`). A known mismatch shows up first when the conversion plan is resolved at
  publication (`Essentials/src/Workflows/Publishing/Elsa.Workflows.Publishing/Services/ActivityResultConversionPlanLinker.cs:115-126`).
  The only member-specific design finding is `SelectedMemberContractUnknown`, for an explicit conversion on an untyped
  member (`ActivityDataSelectionAnalyzer.cs:66-71`). See owner decision C.
- **Refusals lose their reason on the workflow endpoint.** It returns `invalid` / `invalid-source-or-target` with no
  analysis whenever there are findings (`Essentials/src/Workflows/Design/Api/Services/ActivityDataContextService.cs:74-75`).
  The activity-definition endpoint returns `invalid` with the findings (`ActivityDataSelectionContextService.cs:137-139`).
  A refused focused query (bad path, unsupported kind, restricted role) therefore has no explanation in a workflow
  editor. Proposal: the workflow endpoint also returns the analysis on `invalid`. This is additive in v1, and the
  client still discloses nothing for any state it does not read.
- **#4072 / PR #4213** makes an omitted `memberPath` mean the whole value, through a JSON constructor and an
  `init` normalizer on `ActivityDataSelectionQuery`, and changes only that model and two endpoint test files. Studio
  is not blocked: it always sends `memberPath` explicitly (`SC/api/activityDataSelection.ts:80-91`), and the picker
  will keep doing so. To avoid duplicating that work, no slice here touches `ActivityDataSelectionQuery` or the
  endpoints' `Valid` checks (`ActivityDataContextService.cs:156-163`; `ActivityDataSelectionContextService.cs:175`).
  Slice S0 edits `ActivityDataContextEndpointTests.cs` too, so it starts after #4213 merges.

**The Studio client keeps very little of this today.** `readSource` keeps only `shape.type` alias and collection kind,
the binding and the producer identity. It drops `scopeId`, shape state, kind, members, element, map value, presence,
nullability, representation and reason code (`SC/api/activityDataSelection.ts:206-215`, type at `:33-46`).
`readTarget` drops the target type (`:191-204`). Findings are never read (`:179`). Every request uses the
whole-value query with zero depth and zero members (`:85-91,138,157`).

### 3. The wire

`activityInputWire.ts` already keeps any structured value under a syntax other than Literal/Object/Secret as an
object (`SC/activityInputWire.ts:332-343`). Reopen keeps `expressionType` and value (`expandActivityNode`,
`:276-305`). So all four member kinds (`ActivityResultMember`, `VariableMember`, `WorkflowRequestMember`,
`InputMember`) and unknown future kinds survive save, Code Apply and reopen as authored. No wire code change is
needed.

What T019 still needs is tests:

- The existing unknown-kind row authors an `ActivityResultMember` with `producerScopeId: "main"`
  (`SC/__tests__/activityInputWire.test.ts:169`), which the server now refuses (scope-free rule, §5). Replace it with
  canonical rows for each member kind in the analyzer's exact payloads (`ActivityDataSelectionAnalyzer.cs:266,287,354,428`).
  Include dot-bearing and empty segments.
- Run each row through the full save → Code Apply → reopen chain, as the whole-output row does (`:152-164`), not only
  resave (`:181`).
- Keep a `FutureSource` row so an unknown kind stays untouched.

### 4. Picker UX on existing controls

**Where it lives.** The picker stays in the existing chooser: `ActivityDataBrowser` in an `AnchoredPopover` with
`SkeletonRows` (`SC/ActivityDataChooser.tsx:15,124`), opened from Choose data (`:406`) or the syntax-picker entry
(`SC/ActivityPropertiesPanel.tsx:608-619,948-951`). It is offered only where `offersActivityData` allows
(`SC/activityDataSelection.ts:93-100`) and the server returns `memberReadsEnabled: true`. Without that it behaves exactly as
today.

**Source families.** Today `groupActivityOutputSources` keeps only result sources and says inputs and variables keep
their own editors (`SC/activityDataSelection.ts:184-209`). The picker adds three groups, with results first in the
server's order:

- **Workflow inputs.** At workflow level the server offers them as `WorkflowRequest` reads keyed by name; in a body
  they are `Input` reads (`ActivityDataSelectionAnalyzer.cs:261-268`).
- **Request members** of declared workflow inputs, keyed by their names; no undeclared-name proposal.
- **Variables**, nearest scope first. All visible scopes are listed, so a shadowed outer variable is present with its
  own `scopeId` and id `variable:{scope}:{key}` (`Essentials/src/Workflows/Design/Core/Services/ScopedVariableResolver.cs:86-108,157-161`;
  `ActivityDataSelectionAnalyzer.cs:289`). A name that appears in more than one scope shows the scope as
  disambiguation and marks the outer one as shadowed. The read stays explicit (`declaringScopeId`), so choosing the
  outer one is legitimate.

Owner-exposed current items (ForEach) arrive as scoped variables and need no group of their own.

**Nested browsing.** Expanding a source sends a focused query with the source's whole binding as `source`, the path
so far, and small limits (for example depth 1, about 32 members). The member list comes from `shape.members` and
always uses `serializedName`, the exact stored name. `Collection` and `Map` show `element` / `mapValue` as one
literal step only where the server reports a shape; element indexing is not part of the path contract. Choosing a
node stores the focused answer's `binding` unchanged, in one edit, through the existing `chooseData`
(`SC/ActivityPropertiesPanel.tsx:990-1000`). That function needs one fix: it now returns without storing when the
catalog has no descriptor for the binding's kind (`:992-996`). Member kinds get their own reference descriptors, as
`activityResultExpressionDescriptor` does (`SC/activityDataSelection.ts:22-36`).

**Literal segments.** When the shape is Unknown, Truncated or Unavailable, or the family is deferred (variables,
inputs, requests), the picker offers a "Member name" text field. It adds one exact segment at a time, case-sensitive,
with dots kept literally. The client checks the server's bounds (1–64 segments, 4096 characters) before querying.
The help text says names are exact. For request and variable values seeded from .NET code the stored names are
PascalCase (`CustomerId`), and a different case reads as absent (§9, #4178).

**Provenance and reopen.** A member reference shows the source label, then the segments, then the type when it is
Known, for example "Get customer › billing › city · String". A member reference editor contribution modelled on
`activityResultReferenceContribution.tsx` renders it. Replace and Clear keep the existing focus rules
(`SC/ActivityDataChooser.tsx:451-467`).

**Reasons.** The picker shows:

- The source `reasonCode` through the existing `describeUnavailableSource`.
- Shape `State` and `ReasonCode` for "shape not known, type the member name".
- Deferred fit ("checked when the workflow runs") for every non-result family.
- The focused query's findings (UnsupportedMemberBinding, InvalidMemberPath, SourceRestricted, scope or availability
  codes) once S0 returns them.

A known type incompatibility is shown only if owner decision C adds a server fact. Until then the picker shows the
member type and the target type next to each other, and publish reports the mismatch.

### 5. Slice plan

Each slice is about 500 lines or less, has its own tests and is merged on its own. Member authoring stays hidden until
`memberReadsEnabled` is true, so a partial merge is inert.

| Slice | Content | Proof |
|---|---|---|
| **S0 server (first)** | The `MemberReads` fact (A1 or A2). The off-state refusal of a focused member path. The workflow endpoint returns the analysis on `invalid`. Starts after #4213 merges. | Analyzer rows (off/on, each family, refused focused path); endpoint tests for both endpoints; contract docs. |
| **S1 client and wire (T018, T019)** | `readSource` / `readTarget` keep the full shape tree, `scopeId`, target type and findings. Add `requestFocusedSource(source, memberPath, limits)` for both endpoints and read `memberReadsEnabled`. Canonical member wire rows. No UI. | `api/__tests__` reader rows (truncated and unknown trees, defensive drops); wire save → Code Apply → reopen rows for four kinds plus a future kind. |
| **S2 picker model (T022 model)** | Pure functions: family grouping with shadowing, path model (exact segments, bounds, literal entry), descriptors and provenance text for member kinds, reason text. | Unit rows only. |
| **S3 picker UI (T020–T022, T023)** | Nested browsing in `ActivityDataBrowser`; focused requests with cancel, stale and revoked handling; the defensive kind check; the `chooseData` descriptor fix; the member reference editor; workflow editor only. | Behaviour tests beside `activityInputOptionsUi.test.tsx` and `workflowInputPersistence.test.ts`: one-edit history, literal escaping, loading/empty/error/Retry, keyboard, no hidden save, protected values kept. |
| **S4 backend e2e (T024)** | A restart-style script in `e2e-tests/` on the owned-server lifecycle (`e2e-tests/README.md:72-112`), with `MemberReads` on. For each family: catalog → save → publish → execute; Known versus deferred; a type-fit refusal with no write; consumer input and producer completion evidence. Needs only S0 and can run beside S2–S3. | The script and its run receipt. |
| **S5 acceptance (T025)** | Extend `Studio/tests/acceptance/chooseDataJourney.ts` / `journeys.spec.ts` and the `tools/ux-acceptance/run.py` inventory (`:31`) with nested choice, a literal segment, a shadowed variable, reopen, publish and run. Add a minimal-host run and 320px captures. Evidence goes in `quickstart.md`. | The owned-host journeys in the four profiles plus minimal; independent capture review. |

Order: S0 → S1 → S2 → S3 → S5, with S4 after S0 and alongside S2–S3. S0 goes first because the advertisement form
decides S1's reader and the Studio gate, and it is the only server change.

### 6. Risks and owner decisions

Decided (13 Nov):

- **A. Advertisement form: A1.** An init bool on `ActivityDataSelectionAnalysis`, set by the analyzer from its
  options. It is named `MemberReadsEnabled` (wire `memberReadsEnabled`), because the analysis already has a
  `MemberReads` list of admitted reads.
- **B. The workflow endpoint returns findings on `invalid`**, after the same rechecks as a supported answer, as the
  activity-definition endpoint already does.
- **C. Design-time type fit is deferred** to #4311. The picker shows both types and publication refuses a mismatch.
- **D. Ship with the exact-name hint**; #4178 stays open.
- **E. A per-run override turns `MemberReads` on** for the e2e and acceptance hosts; the Workbench default in
  `Essentials/apps/Elsa.Workbench/shells.json:200` stays off.

As delivered in S0, the off-state refusal is a finding with no source, the form every refused focused source
already takes (the stored member read is refused the same way). A member path on a focused source that has no
member read (a scoped result read, a legacy dotted request read) is `ActivityData/InvalidSource` in either setting,
because the same trap applied there with member reads on.

Risks:

- Studio storing a whole binding from an off-state focused answer. S0 closes it on the server and S3's kind check
  closes it on the client.
- Several focused requests per expansion on large shapes. Mitigation: small limits, and abort a request when the next
  expansion supersedes it.
- `ActivityPropertiesPanel.tsx` is 2287 lines. S3 keeps its change to `chooseData` and a descriptor list, with the
  rest in the chooser module.

Out of scope: activity-definition draft editors (T041), Switch and intrinsic inputs (T042), BPMN (T043), optional
reads of conditional producers (`isOptional`), and pinning member reads inside placed bodies.

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


### Owner correction14Nov: explicit workflow request inputs

The owner selected explicit inputs and removal of the undeclared-request option on
[#4309](https://github.com/elsa-workflows/elsa-foundation/issues/4309#issuecomment-6871062664).
S4 exploratory proof found the previous assumption wrong: newly compiled workflows always carry an
explicit v1 input contract, including an empty list, and runtime rejects undeclared start-input names.
Discovery, stored whole/member/legacy-path request admission, and publication must match those declarations.
A workflow request uses the declared input's name; reusable bodies keep their existing declared boundary
reference keys. Declared Object/JSON input members still accept exact dotted/empty literal segments and
remain DeferredFit. No public input is inferred from bindings and runtime validation is not relaxed.
Studio removes the undeclared-name form and host flag. Existing unsupported references retain their exact
payload and report an actionable declare-input problem with explicit Replace/Clear. This changes new
source admission, not stored legacy compiled artifacts. Earlier open-request proof is historical evidence,
not acceptance of runtime support; fresh committed-head proof and independent UX review remain required.
