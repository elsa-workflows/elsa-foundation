# Data model: Elsa 3 activity map (spec 236)

All types are in `Extensions/Elsa3/src/Mapping` (namespace `Elsa3.Mapping.*`) unless a different place is stated.

**Visibility.** §2.23.3 applies:
- implementations are `internal sealed`, tested through `InternalsVisibleTo` the project's own test assembly;
- the types #2705 and spec 235 consume are `public`: §1–§3 and §7–§8.

**Names.** Names follow §E6 R1–R8, and each one has at most four CamelCase components after the `Elsa3` prefix.

## 1. `Elsa3ActivityMapEntry` (public, immutable record)

One entry per Elsa 3 activity type.

| Field | Type | Meaning |
|---|---|---|
| `Elsa3TypeName` | string | e.g. `Elsa.WriteLine`; ordinal key |
| `Target` | `Elsa3ActivityTarget` | what it becomes (§2) |
| `Members` | `IReadOnlyList<Elsa3MemberRule>` | every Elsa 3 input, output, port and serialized property (§3) |
| `Outcomes` | `IReadOnlyList<Elsa3OutcomeRule>` | Elsa 3 outcome → Elsa 4 outcome(s); applied by #2705 (R8) |
| `Rewrite` | `Elsa3NodeRewriteKind?` | named strategy for logic that is not data (§6) |
| `BehaviourWarnings` | `IReadOnlyList<string>` | `ELS3-BEHAVIOUR-*` codes emitted for every node of this type (W) |

**Validation**:
- Every Elsa 3 member in the coverage fixture appears exactly once in `Members`.
- No two entries share `Elsa3TypeName`.

## 2. `Elsa3ActivityTarget` (public, closed hierarchy)

| Kind | Fields | Example |
|---|---|---|
| `CatalogActivity` | `ActivityTypeKey` (Elsa 4 CLR full name) | `Elsa.Activities.Primitives.Activities.WriteLine` |
| `Intrinsic` | `ActivityVersionId`, input keys | `elsa.intrinsic.set@1` |
| `GraphLevel` | `Reason`, owning issue | FlowFork, FlowJoin, Flowchart, Start (#2705) |
| `NoCounterpart` | `Reason` | ParallelForEach, StateMachine, … |

## 3. `Elsa3MemberRule` (public, immutable record)

| Field | Type | Meaning |
|---|---|---|
| `Elsa3Name` | string | Elsa 3 member name (matched case-insensitively against camelCase JSON) |
| `Kind` | `Elsa3MemberKind` | `Input`, `Output`, `Port`, `Property`, `CustomProperty` |
| `Rule` | `Elsa3MemberRuleKind` | `Carry` (R1), `ChildSlot` (R1), `DefaultFlip` (R2), `RemovedInput` (R3), `LostOutput` (R4), `NarrowedContract` (R5), `Structure` (R7), `GraphLevel` (R8), `DisplayOnly` (R9) |
| `Elsa4Key` | string? | exact Elsa 4 input or output key (ordinal), or slot id for `ChildSlot` |
| `Elsa3Default` | `JsonElement?` | Elsa 3 effective default; R2 emits it, R3 compares against it |
| `Contract` | `Elsa3ValueContract?` | R5 constraint: `LiteralOnly`, `StringOnly`, `JsonOnly`, `EnumNameToInt`, `SecretReferenceOnly`, `SingleHeaderValue`, `LiteralTimeSpanToString` |
| `Projection` | string? | R4 exact projection (e.g. `Request.Headers`) |
| `BehaviourWarning` | string? | `ELS3-BEHAVIOUR-*` code emitted when the member is bound (W) |

**Members outside the base Activity (R9, `DisplayOnly`).** The rules for `name`, `metadata` and `nodeId` are declared
once in a shared base list. The maintainer's ContentType decision is `DefaultFlip` with `Elsa3Default` set to
`application/json`, applied only when `Content` is bound.

## 4. `Elsa3OutcomeRule` (public, immutable record)

`Elsa3Outcome` (string) → `Elsa4Outcomes` (string list, e.g. If `Done` → [`True`, `False`]) or `Unsupported` (reason).
236 only carries the data; #2705 applies it to connections.

## 5. `Elsa3ResolvedActivity` (internal)

Result of `Elsa3ActivityResolver` for one node:
- `Entry` (nullable when aliased or verbatim);
- `ActivityVersionId`;
- `Inputs` (`InputDefinition`s, keyed by reference key);
- `Outputs`;
- `StructureFacet` (kind, schema, slots, initial payload), or null;
- `EmittedOutcomes`.

**Source**:
- a plan replacement;
- or the catalog version with the highest valid SemVer;
- or an intrinsic contract.

The resolver caches by Elsa 3 type name for the conversion scope.

## 6. Node rewrites: `Elsa3NodeRewriteKind` and `IElsa3NodeRewrite` (internal, Strategy)

| Kind | Applies to | Effect |
|---|---|---|
| `SwitchValueSynthesis` | Switch, FlowSwitch | MatchFirst only: emits `Value` returning the label of the first true case; `cases[].match` = label |
| `VariableReference` | SetVariable | Elsa 3 `variable{id}` → `VariableReference(referenceKey, declaringScopeId)` with the declaring scope computed from where the variable is declared |
| `ReusableOutputWrite` | SetOutput | In a reusable workflow, a declared output becomes the `set` intrinsic on the synthesized output variable (§8); otherwise the `set-output` intrinsic |
| `TerminalPlacement` | Complete, End | **Plain workflow**: root flowchart, ≤ 1 literal outcome → `finish`; else `ELS3-ACTIVITY-UNSUPPORTED-MODE`. **Reusable workflow**: at any depth, one `Elsa.Activities.Graph.Runtime.Activities.Complete` node with literal `outcome` = the outcome's reference key (§7a); End → `done`; several outcomes, an expression or a key collision → `ELS3-ACTIVITY-UNSUPPORTED-MODE`. Records each literal outcome for §7a |
| `ExecuteWorkflowAsDispatch` | ExecuteWorkflow | `DispatchWorkflow` with `WaitForCompletion = true` |
| `ParallelBranchNames` | Parallel, Fork | Branch `name` = `Branch {n}` (1-based document order); JoinMode → `threshold` |
| `LoopVariableReferences` | For, ForEach | Registers the loop's scoped variables for lowering (`index`; `currentItem`, `currentIndex`) |
| `HttpTriggerResponseMode` | HttpEndpoint | `ResponseMode = Sync` (R2) and the `Result`/`Headers`/`QueryStringData` projections |

## 7. `Elsa3ConversionRequest` and `Elsa3ConvertedDefinition` (public)

**`Elsa3ConversionRequest`**:
- `Definition` (`Elsa3WorkflowDefinition`; never mutated);
- `Replacements` (`IReadOnlyDictionary<string, Elsa3ActivityExactReplacement>`, default empty);
- `IsReusable` (bool);
- `DiagnosticMetadata` (`IReadOnlyDictionary<string,string>`).

**`Elsa3ConvertedDefinition`**:
- `State` (`WorkflowDefinitionState`);
- `OutputMappings` (`IReadOnlyList<Elsa3GraphOutputMapping>`; empty for a plain workflow);
- `BoundaryOutcomes` (`IReadOnlyList<ActivityOutcomeContract>`; empty for a plain workflow; §7a);
- `OutcomeMappings` (`IReadOnlyList<Elsa3GraphOutcomeMapping>`: `SourceOutcomeReferenceKey`,
  `BoundaryOutcomeReferenceKey`; for a reusable workflow always exactly `done → done`; §7a).

## 7a. Boundary outcomes of a reusable workflow (plan D-4 to D-7)

Outcome key rule (`Elsa3OutcomeKey`), for an Elsa 3 outcome string `s`:
- `s` is `"Done"` (ordinal) → reference key `done`, name `Done`;
- otherwise → reference key `s`, name `s` (verbatim, ordinal, case-sensitive).

Two distinct Elsa 3 strings that give the same reference key (only `"done"` and `"Done"`) make each `Complete`/`End`
naming either unconvertible (`ELS3-ACTIVITY-UNSUPPORTED-MODE`).

`BoundaryOutcomes` is built in ordinal reference-key order:
1. `done`/`Done`, emitted, always (D-5).
2. Each outcome a converted `Complete` names, emitted. If the definition's `Outcomes` list does not declare it, one
   `ELS3-BEHAVIOUR-OUTCOME-UNDECLARED` Warning is raised per `Complete` node (D-6).
3. Each outcome in `Elsa3WorkflowDefinition.Outcomes` that no `Complete` reaches, other than `Done`, is **left out**,
   with one `ELS3-BEHAVIOUR-OUTCOME-UNREACHED` Warning per outcome on the definition (D-7).

This matches the provider's schema-2 rules by construction: every emitted outcome is `done` (mapped) or named by a
`Complete` node.

Returned inside the existing `Elsa3MigrationResult<Elsa3ConvertedDefinition>`:
- `Success` carries Warnings;
- `Failure` carries every Error (FR-014).

## 8. `Elsa3GraphOutputMapping` (public record)

| Field | Meaning |
|---|---|
| `OutputReferenceKey` | the reusable workflow's declared output |
| `VariableReferenceKey` | synthesized graph variable `elsa3.output.{sha256(outputReferenceKey)[..16]}`, which cannot collide with an Elsa 3 variable id |
| `Type`, `StorageDriverKey` | from the declared output |

**How the materializer uses it**:
- appends the variables to the manifest's `variables`;
- writes `outputMappings: [{ outputReferenceKey, source: { syntax: "Variable", value: { referenceKey, declaringScopeId } } }]`;
- gives the wrapper a root `Sequence[graph node, set-output(O ← graph node output O) …]` instead of null outputs.

## 9. `Elsa3ReplacedActivityTypeIndex` (internal)

Built once per scope from `IActivityDefinitionVersionStore.ListAsync()`:
- highest valid-SemVer version per definition;
- facet `elsa.replaced-activity-types` → `typeName → definition id[]`;
- an entry with more than one id → `ELS3-ACTIVITY-AMBIGUOUS-ALIAS`.

## 10. Essentials: `ReplacesActivityTypeAttribute` and the facet

- **Attribute.** `Elsa.Activities.Runtime.Core.Attributes.ReplacesActivityTypeAttribute(string typeName)`:
  - `[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]`;
  - `TypeName` property; blank throws `ArgumentException`.
- **Facet.** `ActivityDesignFacet("elsa.replaced-activity-types", "1", { "typeNames": [ordinal-sorted, distinct] })`;
  absent when there are no declarations.

## 11. Additive changes in `Elsa3.Activities.Design.Import` and `Elsa3.Models`

- `ReusableActivityImportMutation.Warnings` and `ReusableActivityImportApplyResult.Warnings`: optional, default empty
  (research R-7).
- `Elsa3ActivityDiagnosticCodes`: public static class in `Elsa3.Models` (research R-3).

## State transitions

None. Conversion is a pure function of (input, catalog content) per call. The only state is the per-scope resolver
cache and alias index, both discarded with the DI scope.
