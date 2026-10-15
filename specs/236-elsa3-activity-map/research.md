# Research: Elsa 3 activity map (spec 236)

Verified against `origin/main` 6768753c7 (Elsa 4) and elsa-core `5b0c2d73` (Elsa 3), 2026-10-14. Line numbers drift.

## R-1 A `Complete`/`End` outcome inside a reusable workflow → graph boundary outcome

**Decision (2026-10-15, supersedes the 2026-10-14 hold)**: emit spec 237's `Complete` activity. Spec 237
([#2750](https://github.com/elsa-workflows/elsa-foundation/issues/2750), PR #2791, merged 2026-10-15 as `4764f246a`)
adds the catalog activity `Elsa.Activities.Graph.Runtime.Activities.Complete`. It has one required input, `outcome`:
one literal boundary-outcome reference key. It ends the nearest reusable activity from any depth, through every
composite, and durably cancels the rest of the body. An Elsa 3 `Complete`/`End` in a reusable workflow becomes one such
node, wherever it sits (`TerminalPlacement`, data-model §6). The conversion choices are recorded as D-1 to D-10 in
plan.md, "Complete and End conversion decisions".

**Verified on main `4764f246a`** (`specs/237-graph-boundary-completion/contracts/complete-activity.md`):

- The type key and input are `Essentials/src/Activities/Graph/Runtime/Activities/Complete.cs` (`[ActivityInput(Key = "outcome")]`,
  `[Required]`).
- Publish validation applies in `GraphActivityProvider`:
  - `Complete` in a schema-1 body → `activity.graph.complete.schema-unsupported`;
  - a non-literal or unknown key → `outcome-literal-required` / `outcome-unknown`;
  - an emitted outcome reached by neither a mapping nor a `Complete` → `activity.graph.outcome-mapping-required`.
- Schema 1 also requires exactly one emitted outcome, `done`.
- At run time `GraphActivity` resolves the key through the compiled descriptor's `CompleteOutcomes`.
- With no `outcomeMappings`, a body that ends without reaching a `Complete` faults
  (`activity.graph.entry-completed-without-mapping`). D-5 avoids this.
- A `Complete` in a plain workflow fails draft validation (`Graph/CompleteOutsideGraph`) and faults at run time.
- Outcome reference keys and names have no format rule. Matching is ordinal. Names are what the runtime emits and what
  caller ports bind to.

**Findings that led to #2750** (historical, 2026-10-14):

- `GraphActivity` schedules only its entry node and accepts completion only from it. The boundary outcome is the
  entry's single outcome, translated through schema-2 `outcomeMappings`
  (`Essentials/src/Activities/Graph/Runtime/Activities/GraphActivity.cs` L52-92, L198-204).
- `GraphActivityProvider` validates mapping sources only against the entry's emitted outcomes (L496-518).
- A Flowchart completes only with `Done` or `Break` (`Flowchart.cs` L29-30). An unconnected outcome ends its path and
  does not bubble up (`FlowchartExecutionEngine.cs` L147-161, L264-272).
- `control` completes only its own node; `return` completes with `Done`; `finish` ends the whole workflow.
- Specs 138 (L112) and 221 (FR-038) deferred "explicit return / descendant completion" on purpose.

**Alternatives considered**:

- A `Break` → mapped boundary outcome rewrite. Rejected: it carries at most one non-Done outcome, a loop body swallows
  `Break`, a real Elsa 3 `Break` would be conflated with it, and a Flowchart still waits for its other paths unless
  `Break` fires. Partial support conflicts with the maintainer's "faithful or hold" decision.
  - **Superseded (2026-10-15, D-8).** `Complete` removes the reason this alternative existed, and nothing in this
    spec maps a `Complete`/`End` onto `Break`. An Elsa 3 `Break` still converts to Elsa 4 `Break`, with
    `ELS3-BEHAVIOUR-BREAK-STOPS-AT-PARALLEL`. Spec 237 leaves `Break` unchanged and unrelated to `Complete`.

## R-2 Custom-activity alias: attribute, recording, query

**Decision**:

- A generic, Elsa-3-agnostic attribute `ReplacesActivityTypeAttribute(string typeName)` in
  `Elsa.Activities.Runtime.Core.Attributes` (next to `VersionAttribute`):
  - `AllowMultiple = true`, `Inherited = false`;
  - throws on a blank name.
- The CLR scanner records it as design facet kind `elsa.replaced-activity-types`, schema `1`, payload
  `{ "typeNames": [...] }`:
  - names trimmed, de-duplicated and ordinally sorted;
  - read from the class only, not from base types;
  - no facet when the attribute is absent, so every existing catalog hash is unchanged;
  - the payload never has a `ports` property, because `ActivityAuthoringCatalogReader.ToPorts` treats any facet with
    `ports` as ports.
- The converter builds an index once per conversion scope from `IActivityDefinitionVersionStore.ListAsync()`:
  - highest valid-SemVer version per definition;
  - each `typeNames` entry → that definition;
  - two definitions claiming one name → "ambiguous activity alias".
  - `Elsa3.Mapping` already references `Elsa.Activities.Design.Persistence.Core`.
  - Reads use the ambient tenant, the same rows `GetDefinition` sees.

**Rationale**:

- §E2.7 keeps Elsa 3 knowledge inside `Elsa3.*`. A generic "replaces type" claim in Essentials, interpreted only by
  `Elsa3.Mapping`, adds no reference and passes both guard tests (`Elsa3MigrationBoundaryTests`,
  `Runtime_projects_do_not_reference_elsa3_compatibility_projects`).
- Activity authors already reference `Elsa.Activities.Runtime.Core`.
- The name avoids "Alias", which already means a CLR or value type alias (`TypeAliasConvention`).
- "Replaces" matches the existing `Elsa3ActivityExactReplacement` (§E6 R6).
- Neither project is on Line A, so there are no `PublicAPI.*.txt` updates.

**Consequence (now in spec FR-009 and US3)**:

- Design facets are part of the version content hash (`DefaultActivityDefinitionHasher` L64-75).
- Adding or removing the attribute therefore needs a new activity version; otherwise `ActivityVersionHashMismatchException`,
  by design (Model X).
- Old versions keep their facet. Reading only the highest version makes "remove it in a new version" end the alias.

**Alternatives rejected**:

- An Elsa-3-named attribute in Essentials (against §E2.7's intent).
- An Elsa 3 attribute plus a new scanner facet-contributor contract: new public extension surface, and the
  reconciliation shell would have to load an Elsa 3 module. This is the runner-up if architects want no import-flavoured
  attribute in Essentials.
- Storing the alias outside the hash: no such field exists, and a sibling table is an operational sibling, which Model X
  forbids.
- A facet query on `IActivityDefinitionLookup`: a contract change for a small catalog read once per import.

## R-3 Diagnostic codes

**Decision**: Two new families, following `ELS3-<AREA>-<REASON>` (`Elsa3MigrationDiagnosticCodes`,
`ReusableActivityImportDiagnosticCodes`).

- `ELS3-ACTIVITY-*` for node-level conversion errors.
- `ELS3-BEHAVIOUR-*` for the runtime-difference warnings (W).
- Constants live in a new public static class `Elsa3ActivityDiagnosticCodes` in `Elsa3.Models`, beside the existing
  code classes, so spec 235 can match on them without referencing `Elsa3.Mapping` internals.

Full list: [contracts/diagnostic-codes.md](contracts/diagnostic-codes.md).

**Rationale**:

- `VF-IMP-*` stays for value-flow lowering, unchanged.
- `ELS3-MIGRATION-DEFINITION-MAPPING-FAILED` remains only as the last-resort wrapper for an unexpected exception.
  FR-012 should make it unreachable from input, and a test asserts that.

## R-4 Map representation, placement and coverage test

**Decision**:

- **Representation.** The map is C# data in `Elsa3.Mapping`, folder `ActivityMap/`:
  - one immutable `Elsa3ActivityMapEntry` per Elsa 3 type, built in a static table class `Elsa3BuiltInActivityMap`;
  - members, outcomes and node rewrites are declared as data;
  - logic that cannot be data (Switch value synthesis, SetVariable reference scope, SetOutput graph rewrite,
    End/Complete placement) is a named `Elsa3NodeRewrite` strategy (§2.24.2 Strategy) that the entry references.
- **Ordering.** No reflection, no embedded files, no ordering dependence: lookups are ordinal dictionaries built once.
- **Coverage, Elsa 3 side.** `Elsa3ActivityMapCoverageTests` loads a **test-owned** Elsa 3 surface fixture
  (`tests/Mapping/Tests/Fixtures/elsa3-activity-surface.json`).
  - It derives from the parity evidence, with the spurious `Result` outputs removed and `SetOutput`, the plain
    properties and the ports added.
  - It asserts every activity and member is classified, as data, not by reading `member-map.md`. That respects
    §2.23.2 (no test asserts on markdown).
- **Coverage, Elsa 4 side.** A catalog test builds a real catalog by running the CLR scanner over the Elsa 4 activity
  assemblies. It asserts every Elsa 4 type key, member key, slot and outcome the map targets exists with that exact
  (ordinal) key. This is SC-002's real-catalog requirement.

**Rationale**:

- Reviewable in a diff.
- Deterministic (FR-020).
- Any change shows up in converter output, which is what spec 235's golden test needs.
- A JSON resource would need its own loader and schema, and gives no type safety.

**Alternatives rejected**:

- An embedded JSON map.
- Reflection over attribute-annotated rule classes, which is order-sensitive and hides the table.
- Asserting against `member-map.md`, which §2.23.2 forbids.

## R-5 One resolution for both lookup sites

**Decision**:

- `Elsa3ActivityResolver` (scoped, internal; §E6 R4 "Resolver maps key → value") resolves an `Elsa3Activity` to an
  `Elsa3ResolvedActivity` (target kind, activity version id, declared inputs, outputs, slots, outcomes, map entry) or to
  diagnostics.
- **Resolution order:** plan replacement (FR-017) → built-in map entry → `ReplacesActivityType` index → verbatim catalog
  key → "unmapped activity type".
- **Version pick:** the highest valid-SemVer version via `IActivityDefinitionLookup.ListVersions` + `SemVer.TryParse` /
  `CompareTo`; build metadata is ignored for ordering.
- **Known limitation (PR #2805 review):** the pick ignores version lifecycle. For a CLR activity every version is
  active, so this does not matter. For a verbatim or alias match that lands on a reusable (non-CLR) activity, the
  highest version can be `Retired` or `Revoked`, or differ from the recommended version the picker offers. Excluding
  revoked versions, or preferring the recommended active one, for non-CLR sources is left to the work that imports or
  publishes reusable activities (#2796).
- **Caching:** results are cached per conversion by Elsa 3 type name.
- **Who uses it:**
  - `Elsa3MemoryReferenceGraph.GetVersionAsync` and `Elsa3ActivityToState.GetVersion` are replaced by calls to the
    resolver (FR-011).
  - Intrinsic targets resolve without the catalog, using the intrinsic descriptor contract keys.

## R-6 Native structures instead of the opaque Elsa 3 structure (found during planning)

**Finding**:

- Today the mapper puts every child into `elsa3.imported-activity.structure`, an Elsa-3-only kind that, as its own
  remarks say, "the executable compiler compiles … as an opaque structure".
- Children of imported containers are therefore never walked or executed by Elsa 4.
- Port children (`then`, `else`, `body`, `cases`, `default`, `branches`) are not read at all.

**Decision**:

- A container node gets the **native structure of its Elsa 4 target**, built from the target version's own structure
  design facet: kind, schema, slots (`property`, `collectionProperty`, cardinality) and initial payload.
- Map entries supply the per-item fields of collection slots: Parallel branch `name`, Switch case `match`.
- Elsa 3 `activities` and port properties are routed to slots by the entry's member rules.
- The **Flowchart** structure (connections, `startNodeId`, join policies) is #2705's.
  - Until #2705, a Flowchart node reports `ELS3-ACTIVITY-GRAPH-CONVERSION-PENDING`, with or without connections.
  - Its children are still mapped per node (FR-006), so #2705 only adds the graph.
- `Elsa3ImportedActivityStructure` stops being written. Its reader `Nodes()` is replaced, in the collection import's
  credential-literal admission, by the shared design tree walk over native structures.
  - Nothing persisted depends on it: imports never ran these children.

**Rationale**: FR-002 class R1 "child slot" cannot be met with an opaque structure, and the converted workflow would not
run.

**Alternatives rejected**:

- Keep the opaque structure and register a handler for it. Its own remarks explain why that changes every validator and
  publication.
- Hard-code payload shapes per activity. The catalog facet already describes them, and it follows Elsa 4 changes.

## R-7 Warnings on the collection path

**Finding**: `ReusableActivityImportMutation` and `ReusableActivityImportApplyResult` have no diagnostics channel.

**Decision**: Add an optional `IReadOnlyList<Elsa3MigrationDiagnostic> Warnings` (default empty) to both records.

- The materializer fills it.
- `ReusableActivityCollectionImporter` copies it into the apply result.
- The apply endpoint returns it.

These are additive record members in `Elsa3.Activities.Design.Import`; the analyzer is untouched (FR-019). The plan's
persisted receipt does not store warnings: a replayed apply returns the receipt as today.

## R-8 Lowering on the collection path and the lowerer's input dependency

**Finding**: `Elsa3ValueFlowLowerer.Lower` takes `Elsa3WorkflowDefinitionImportInput`, which the collection path does
not have; it is used only for diagnostic metadata.

**Decision**: The lowerer takes a diagnostic-metadata dictionary instead. `ImportAsync` builds it from its input as
today, and the collection path builds it from the plan item.

**Order inside one conversion**:

1. Clone the definition.
2. Normalize: resolve each node, rename members to Elsa 4 keys, apply R2–R5 and the strategies, route ports.
3. Collect the memory-reference graph, then lower.
4. Map to `WorkflowDefinitionState`.

Normalizing first means lowering sees Elsa 4 member names (`Collection`, not `Items`). Loop `CurrentValue` references
lower to the Elsa 4 scoped variables (`currentItem`, `currentIndex`, `index`) with the loop node as declaring scope.

## R-9 Determinism and spec 235

- The map and the strategies are pure.
- The alias index and version picks depend on catalog content.
- Generated keys (synthesized output variables, Parallel branch names) derive from Elsa 3 ids and positions with
  `ReusableActivityImportIdentity`-style hashing, never from GUIDs or time.
- Diagnostics are ordered by (definition id, JSON path, code).
- Catalog dependence is recorded in the spec's "Dependencies and notes for spec 235".

## R-10 Imported reusable activities are never published (found 2026-10-15)

**Decision**: split out to [#2796](https://github.com/elsa-workflows/elsa-foundation/issues/2796) (maintainer decision,
2026-10-15). Spec 236 produces a correct schema-2 manifest and contract. It proves them by running
`GraphActivityProvider.ValidateAsync` over converted output in tests. (Planned with `CompileAsync` too; during
implementation compilation turned out to need the activity published, so it moved to #2796.) It does not change how the
collection import persists or publishes.

**Findings** (on `4764f246a`):

- The materializer writes the catalog row directly:
  - `DescriptorPayload` is the design manifest (`{rootActivity, variables, outputMappings}`);
  - `ProviderSchemaVersion = "1"`;
  - `DesignFacets = []`.
  `EfReusableActivityImportCommand` writes no `ActivityDefinitionVersionPublication`.
- The contract (inputs, outputs, outcomes) lives on the publication (`ReusableActivityEntities.cs` L78-118). Callers
  read outcome ports from the `elsa.outcomes` facet, which the publisher writes for emitted outcomes only
  (`ActivityDefinitionPublisher.cs` L779-794). An imported version therefore has no persisted outcome source.
- The manifest becomes a `GraphActivityDescriptor` only at activity publish (`ActivityTemplateCompiler` →
  `GraphActivityProvider.CompileAsync` → `BuildBoundaryPayload`).
- A version with no publication is compiled as a raw row (`WorkflowExecutableCompiler.cs` L145-152). Its raw manifest
  lacks the descriptor's required `definitionId`, `templateHash` and entry node (`GraphActivityDescriptor.cs` L90-96).
  So imported reusables do not run as graphs today, with or without `Complete`.
- No provider validation runs on the import path.

**Consequences for this spec**:

- `Elsa3ConvertedDefinition` returns the contract outcomes (`BoundaryOutcomes`, data-model §7) next to the manifest
  content. Persisting them is #2796's work.
- The materializer moves to schema 2 (`ProviderSchemaVersion = "2"`, manifest `outcomeMappings`). It writes nothing
  else new.
- SC-006 is a run-time criterion. Here it is proven by provider validation (the manifest carries the output and
  outcome mappings). It is proven by compilation and at run time once #2796 lands.

**Alternatives considered**: widening spec 236 to publish through `ActivityDefinitionPublisher` (rejected: a different
bounded context, the Import package and publishing, much larger scope); holding spec 236 again (rejected: the
converter's output is independently verifiable).

## R-11 Elsa 3 JavaScript surface versus the converter and the Elsa 4 engine (found 2026-10-15)

**Decision**: spec 236 closes the silent gaps: everything Elsa 4 cannot run is refused at conversion with a stable code.
Making more Elsa 3 functions convert is a follow-up issue and spec (#2717). See plan D-14 to D-16.

**Findings** (elsa-core `5b0c2d73`; `w236` on `47e02fd42`):

- **Elsa 3 surface** (`Elsa.Expressions.JavaScript/Handlers/ConfigureEngineWithCommonFunctions.cs`,
  `ConfigureEngineWithVariablesAndInputOutputAccessors.cs`):
  - `getVariable`/`setVariable`, `getInput`, `getOutputFrom`, `getLastResult`;
  - workflow and correlation accessors;
  - helpers (`isNullOrEmpty`, `toJson`, `newGuid`, base64/bytes);
  - generated `get{Var}()`/`set{Var}()`, `get{Input}()` and `get{Output}From{Activity}()`;
  - a `variables` object, CLR type globals (`DateTime`, `Guid`, …) and optional lodash/moment;
  - RunJavaScript only: `setOutcome(s)`.
  
  Elsa 3 evaluates source as a statement script whose completion value is the result. `getHttpRequestBody` (110 JTest
  uses) is **not** an Elsa 3 core function; it comes from a host extension.
- **Elsa 4** expressions run as `"use strict"; (<source>)` (`JintPortableJavaScriptEvaluator.cs:47`), so the source must
  be one expression. They see frozen `args`, plus `getVariable`, `variables` and generated `get<Name>()` for referenced
  variables; `Date`, `Math.random` and `Intl` are removed. Scripts run as `"use strict"; (() => { <source> })()`
  (`JintJavaScriptScriptEvaluator.cs:20`), see only `args`, and must `return`; no `setOutcome`.
- **The rewriter** (`Elsa3ExpressionRewriter.cs`) lowers `getVariable("x")`, `getInput("x")` and `variables.x` to
  parameter bindings read as `args.<name>`. It refuses every other bare call with VF-IMP-005 (L324), generated
  accessors included. Gaps it lets through, which then fail at run time:
  - `new Date()`;
  - member calls on CLR globals or libraries (`DateTime.Now`, `Guid.NewGuid()`, `_.map`);
  - Elsa 3 `args.x`;
  - mutating a read value;
  - multi-statement expressions.
- **RunJavaScript scripts are never rewritten**: Elsa 3 stores `script` as a Literal, and lowering returns early for
  literals (`Elsa3ValueFlowLowerer.cs:249-255`). A converted script silently fails, or returns null where Elsa 3 used a
  completion value. member-map.md's RunJavaScript row promised otherwise.
- **Silent-mismatch risks for the follow-up:**
  - an Elsa 3 input accessor shadows a variable accessor of the same name;
  - Elsa 3 hands scripts mutable CLR values (`newGuid` returns a Guid), Elsa 4 frozen JSON;
  - the rewriter matches variable names ignoring case.
