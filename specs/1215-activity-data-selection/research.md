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
the change is cut, not new behaviour. Every claim about current behaviour was **read** from source at that commit;
nothing was run for this note. The rules themselves are in the [binding contract](contracts/binding-contract.md),
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
| Executable JSON (`EF/EfWorkflowExecutableStore.cs:82,1032`) | Identical: `Selection` omitted when null, like `Secret` (`RuntimeInputBinding.cs:82`) and a null `ProducerScopeId` (`:232`). | `"source":"ActivityResultMember"`, `"selection":{…}`, derived requirement in `runtimeRequirements`. | Unknown enum name: `JsonException`, nothing read. A numeric source (no store writes one) reaches `ValidateCanonical` and throws (`RuntimeInputBinding.cs:105-117`). |
| Template envelope (`EF/EfExecutableActivityTemplateStore.cs:339,404-406`) | Identical, same options. | As above. | As above. |
| Executable hash V2 (`WorkflowExecutableHasher.cs:375-380`) | Identical: the binding is serialized whole through the source-generated context, which honours `WhenWritingNull`. | `selection` inside `contract`. | Not a reader. |
| Node-payload hash (`WorkflowExecutableHasher.cs:123-146`; used by `ComputeHash(node)` and draft test runs, `Essentials/src/Workflows/Publishing/Api/Services/ActivityDraftTestRunService.cs:286`) | Identical if old arms are untouched. | **Silent hazard:** a member kind falls to `_ => CanonicalJson(LiteralValue)` (`:140`), so two member reads differing only in path hash alike. The store keeps the first body for an equal hash and checks nothing but the hash (`EF/EfWorkflowExecutableStore.cs:140-144`), so a second test run would execute the first path. Closed by explicit arms and a throwing default (§6). | — |
| Template behaviour hash | Identical (default options, `WhenWritingNull`). | `Selection` and the derived requirement. | — |
| Input snapshot (`WRC/Models/ActivityExecutionValueFlowRecords.cs:65-72`) | Values unchanged; binding fingerprint unchanged only if selection is appended when present, as secrets are (`RuntimeActivityInputMaterializer.cs:637-644`). | The **selected, converted** value; never the source. | An older runtime cannot load the executable, so it never reads such a snapshot. |
| Inspection view (`Essentials/src/Workflows/Runtime/Api/Models/WorkflowExecutableInspectionViews.cs:124-137`) | Identical. | Without change, shows `Source` `ActivityResultMember`, the producer reference and the projection key as summary, and no path: a misleading inspection, not a misread. Add a `Selection` view member and a summary naming the path. | Older Studio shows the unknown source string. |

**Mixed versions.** In distributed placement an older member that cannot deserialize the pinned executable resolves
the requirement as `Unresolved` and does not claim the execution; commands wait for a member that can run it
(`Essentials/src/Workflows/Runtime/Distributed/Elsa.Workflows.Runtime.Distributed/Placement/ExecutionPlacementRequirementResolver.cs:57-59,78-85`;
`ExecutionPlacementGate.cs:143-160`). Without the distributed module, an older runtime's start, resume or drain read
throws; what then happens to the work item is not established (§11).

**Every silent path found, and how it closes.** (a) Selection on an old kind ignored by an older reader: closed by
new enum members (§2). (b) Node-payload hash collision: closed by explicit arms plus a throwing default. (c)
`PlacedReferenceRemapper` passes unknown kinds through unchanged (`:106-107`), so a placed member read would keep its
template-local producer id; an `isOptional` read would then resolve to null. It is caught loudly at workflow
publication, because the data-selection guard reads payloads, not `Source` (`WP/ActivityTemplateDataSelectionGuard.cs:48-53`),
and refuses an unknown producer (`:84-90`); the remapper still needs member arms. (d) A replacement
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
(`ClrActivitySerializedShapeSource.cs:137-141`). Whether a typed whole result can commit as null was not established,
so runtime never trusts `Known` presence or nullability for safety: facts narrow publication and plan conversion;
runtime re-checks (§7).

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
without a plan (`ExecutableNodeCompiler.cs:452`) and are checked by the intrinsic payload validation.

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
  `request-member:{memberKey}:{S}`, `variable-member:{scope}:{key}:{S}`,
  `result-member:{producer}:{projection}:{isOptional}:{S}`. `S` is framed so delimiters inside names cannot
  collide: `sel:1:{mode}:{n}:` then each segment as `{length}#{segment}`, then
  `:{FormatType(selectedType) or empty}:{mayBeAbsent}:{mayBeNull}:{representation}:{profileRevision}`. The default
  arm becomes a throw; today it is reachable only by a new kind (each old kind has an arm, and `Literal` always has
  its payload).
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
so they need nothing special.

| Observation | Result | Surfaces as |
|---|---|---|
| Required producer missing or ambiguous | Existing `CausalActivityResultResolutionException` (`CausalActivityResultResolver.cs:85-91`). | Blocking incident, consumer stays `Scheduled` (existing test `Essentials/tests/Activities/Behavioral/Tests/ActivityResultOccurrenceTests.cs:450-461`). |
| Optional producer missing | Root `ExplicitNull`, as whole-value reads (`RuntimeInputBindingResolver.cs:86-93`); destination rules decide (decision 8). | As today. |
| Member absent | `Absent`, with failed segment index. | Accepted only for an optional nullable input (`RuntimeActivityInputMaterializer.cs:605-612`); otherwise new `VF-SEL-001`. |
| Leaf null | `ExplicitNull`. | Nullable input accepts; otherwise `VF-SEL-002` naming the leaf. |
| Null before the last segment | Stop; `ExplicitNull` with origin segment. | Same rule, `VF-SEL-002` naming the prefix. |
| Array or scalar before the last segment | Selection fails. | `VF-SEL-003`. |
| Value that does not fit the target | Fails before snapshot. | `VF-SEL-004`. |

All of these throw from input materialization before the snapshot commit and before construction, so they use the
existing blocking-incident path; the producer's committed completion is untouched. Codes are in the message text,
because incidents carry failures as text (comment at `ActivityResultOccurrenceTests.cs:463-464`).

**Fit.** A bounded internal helper in Workflows Runtime, using the unchanged activity-value profile and the pinned
target and nullability, runs before the snapshot for **every** member read, `Known` included (decision 9): `Known`
facts are trusted for planning, not for safety. The existing hydrator check stays as later defence. Intrinsic reads
reuse `ValidateAssignmentPayload` (`WorkflowIntrinsicExecutor.cs:721-729`); its external path (`:673-712`) and
`HasExternalProjection` (`:741-743`) need the same selector.

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

### 9. Other source families

| Kind | Differs from the result case | Shape facts |
|---|---|---|
| `VariableMember` | Requires an explicit `declaringScopeId`; legacy `Variable` defaults to the workflow scope (`RuntimeInputBindingCompiler.cs:605-622`). Current items are the owning structure's scoped variables. Placement must remap the scope (`PlacedReferenceRemapper.cs:147-154`). | Always `DeferredFit`: a declared type yields `serialization-profile-unbound` (`ClrActivitySerializedShapeSource.cs:150-166`); variables can hold JSON from other writers. |
| `WorkflowRequestMember` | Payload `memberKey` only; legacy dotted `Path` must be null. An open request (no declared inputs) is always deferred (`ActivityDataSelectionAnalyzer.cs:219-222`). | Always `DeferredFit`. Request seeding uses default JSON options for CLR values (comment at `ClrActivitySerializedShapeSource.cs:161-163`), so a CLR-seeded request may carry `CustomerId` where an author types `customerId`: exact segments then read `Absent`. |
| `InputMember` | Authored only. Placement must lower it to `WorkflowRequestMember` with all segments; today's `Input` lowering keeps only `referenceKey` (`ActivityTemplatePlacer.cs:665-684`). Whether a workflow-level `Input` binding publishes at all was not established (§11). | Always `DeferredFit`. |

None of the three can be `Known` today, so all depend on the runtime fit helper, and the issue's acceptance names
only activity results. They belong to this leaf (T012 lists all four) but in a later slice than
`ActivityResultMember`.

### 10. Slicing

Order: model, then runtime, then publication. Publication before runtime would let a server publish artifacts no
runtime can run: half-enabled and reachable.

| Slice | Adds | Inert until the next slice because | Proving tests | Risk |
|---|---|---|---|---|
| A. Member-read model (T011; hashing part of T013; requirement derivation of T016; model and hash part of T017) | Enum members, `RuntimeReadSelection`, constructor overload and invariants, `Selection` serialization, hash arms with throwing default, fingerprint append, inspection view, derived requirement, PublicAPI. | Nothing compiles a member binding; the resolver throws for one; no capability is advertised, so preflight, import and placement report the requirement unmet. | Invariant rows; round trip; corpus goldens unchanged; distinct hashes for distinct paths, modes, types and delimiter-bearing names; old-reader controls (§11); requirement derived for member, absent for whole. | Low–medium: public shape and hash rendering. |
| B. Runtime selection (T014, T015, capability and guards of T016, runtime part of T017) | Selector, materializer and intrinsic integration inline and external, fit before snapshot, reason codes, capability with composition check, front-door and drain guards. | Still no publication path emits member bindings; only hand-built executables reach it, and those now run correctly. | Unit rows for every outcome in §7, exact and literal names; inline/external parity; intrinsic; constructor counter and no snapshot commit on failure; retry reuse; real engine harness in a Sequence and a top-level Flowchart; guards refuse start, resume and queued work without the capability. | High: materialization order, external values, protection. |
| C. Publish result member reads (T012, T013, T017 for `ActivityResultMember`) | Authored parse, analyzer offer and admission, compiler branch and old-kind refusal, pin pass, linker on the selected type, remapper arms, contract text (§5). | Enables the feature for API clients; the Studio member picker (T022) stays gated in its own leaf. | Offer-equals-admission rows; refusal before any write through the real publish handler; template placement; real host publish → run in a Sequence and a top-level Flowchart, inline and external (acceptance 1). | Medium–high. |
| D. Other families (T012, T013 for the rest) | `VariableMember`, `WorkflowRequestMember`, `InputMember` and its placement lowering. | Each kind refused until its row lands. | Deferred-fit rows; placement lowering keeps segments; reopen and publish. | Medium. |

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
| 10 | Capability and replaced materializer | Fail composition when the capability is present but the materializer is replaced. | Blocks a custom materializer that delegates correctly to the built-in one. |
| 11 | Path size bound | 64 segments (existing query bound) and 4 096 UTF-16 code units in total. | Arbitrary; a long dictionary key could exceed it where JSON itself would not. |
| 12 | Name clash | Keep the code-first builder's `WorkflowRequestMember<T>` (`WorkflowValue.cs:28`), which writes the legacy dotted `WorkflowRequest` path (`WorkflowBuilder.cs:117-123`), and document it. | Same name, different wire meaning, in the same product as the new discriminant (ADR 0080 D5 plain names). |

**Where the spec disagrees with the code or with #4053/#4059.** Binding contract §1 and the data model keep an
optional legacy scope on `ActivityResultMember` (decision 2). Binding contract §6 says an old compiler can publish an
unusable expression artifact from a member kind; on current main, since #3948, publication refuses it before any
write (§1). Data model "Protection minimum" has no facts to pin (§2). Tasks T013 point pinning at the linker, which
runs only in the workflow compiler (§4), and list `ExecutableActivityTemplateBehaviorHasher` for edits it does not
need (§6).

**Not established by reading; characterise before implementation, as for #4053:**

1. An actual baseline binary reading a member executable, by name and by number. Proposed control: run a build of
   `a9771131c3` against a store written by slice A; a test-local enum mirror only proves the converter.
2. What an older non-distributed runtime does with a work item whose executable fails to deserialize (incident,
   poison under the ownership lease of #4063, or a retry loop).
3. That a new selection exception surfaces as the same blocking incident as the causal failures.
4. Whether a workflow-level `Input` binding publishes today, which decides where `InputMember` can appear.
5. Whether a typed whole result can commit as null (the reader assumes not).
6. `ValueConversionPlanResolver` with a dynamic or `Elsa.Any` source, and whether the conversion executor checks a
   payload against an `Identity` plan's source type.
7. `JsonElement.TryGetProperty` on duplicate keys (documented as last wins, matching deserialization; not run).
8. Whether every executable-producing path (BPMN compile, import, workflow test run) runs the analyzer and so
   refuses member kinds and old-kind paths before slice C.

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
