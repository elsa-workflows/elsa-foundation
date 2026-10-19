# Implementation Plan: Elsa 3 JavaScript host functions

**Branch**: `2717-elsa3-js-host-functions` | **Date**: 2026-10-19 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/247-elsa3-js-host-functions/spec.md`; research in
[research.md](research.md). The Elsa 3.5.0 source was read at tag `3.5.0` (`d72c4974d`), and the Elsa 4 binding
pipeline was traced in this worktree.

## Summary

**What.** Elsa 3 JavaScript converts where Elsa 4 runs it the same, and the rest is refused with specific guidance.

What converts:
- a top-level `return`
- generated `get{Name}()` accessors
- `getCorrelationId()` and `getWorkflowInstanceId()`
- `isNullOrEmpty` and `isNullOrWhiteSpace`

Everything else spec 236 refuses stays refused, with sharper messages and a written migration story.
`ELS3-ACTIVITY-GRAPH-CONVERSION-PENDING` is retired.

**How.**
- **Top-level return (R-1).** Parse with return allowed outside a function, as Elsa 3 does. A body with a top-level
  `return` is wrapped in a function. Its last statement must be a `return` or an expression (which becomes a `return`);
  otherwise it stays refused with `VF-IMP-006`.
- **Generated accessors (R-2, R-3).** A new ambient reference kind, `GeneratedAccessor`, carries the name after `get`.
  The value-flow lowerer resolves it the way Elsa 3 generates the names, in this order:
  1. An output accessor shadows everything, so a match is refused.
  2. An exact workflow-input name.
  3. A variable whose Pascal-cased name matches exactly.

  The result is the same binding that `getInput` or `getVariable` produces. If nothing matches, the call is still an
  "unknown host function".
- **Instance metadata (R-4, R-5).** A new Elsa 4 Core binding, `WorkflowInstanceExpressionParameterBinding(member)`,
  has two members: `correlationId` and `instanceId`.
  - Runtime resolves it from committed state. The correlation ID comes from the identity projection, which is now
    seeded at start. The instance ID is `WorkflowExecutionId`.
  - The rewriter lowers the two Elsa 3 functions to it.
- **Helpers (R-6).** `isNullOrEmpty` and `isNullOrWhiteSpace` are inlined, using .NET's exact whitespace set. `toJson`,
  `parseGuid` and the byte/stream helpers stay refused, each with an Elsa 3 rewrite in the guidance.
- **Refusal messages (R-7).** A refusal table in the rewriter gives each refused name its own message and guidance.
  The codes are unchanged.
- **Cleanup (R-8).** Retire the code in the docs. Removing the pending-graph code paths is proposed, pending approval
  (D-6).

## Spec corrections from research

Research against the Elsa 3 source corrects four spec statements. `spec.md` is updated in the same commit as this
plan.

1. **FR-006 / US2 scenario 3.** Accessor names match **exactly as Elsa 3 generates them**, not case-insensitively. A
   variable `myVar` gives `getMyVar` (Pascal-cased). An input `myVar` gives `getmyVar` (verbatim). JavaScript names are
   case-sensitive, so `getmyvar()` was undefined in Elsa 3 and stays refused.
2. **FR-005.** An input accessor shadows a variable accessor only when the generated names are identical. Output
   accessors (`get{Output}From{Activity}`) are registered last and shadow both, so a name matching an output accessor
   is refused, never lowered to an input or a variable.
3. **FR-015.**
   - `toJson` stays refused. Elsa 3 uses System.Text.Json, which escapes `<>&'"+` as `\u00XX` and keeps CLR
     PascalCase, so `JSON.stringify` differs.
   - `parseGuid` stays refused. It returns a CLR `Guid` with .NET members and accepts five formats.
   - Both get an Elsa 3 rewrite in their guidance.
4. **SC-002.** The corpus target drops from 8 to 7 definitions: `javascript-json-methods` uses `toJson` (R-9).

## Technical Context

**Language/Version**: C# / .NET (repo SDK, `global.json`), nullable enabled.

**Primary Dependencies**: Acornima (already used by the rewriter) and System.Text.Json. No new package.

**Storage**: No new store. The correlation ID seed adds one durable value, `identity:correlationId`, at workflow start,
on the existing identity channel (`RuntimeMetadataKeys.IdentityName`).

**Testing**:
- xunit only.
- `Extensions/Elsa3/tests/Mapping/Tests`: converter tests against `RealActivityCatalog`. The SC-003 equivalence tests
  run converted expressions through the real Jint portable evaluator (a test-only reference, if not already present)
  and compare the results with Elsa 3 results recorded in research.
- `Essentials/tests/Expressions/Tests`: the binding wire-shape theory and the pinned fingerprint fixture.
- `Essentials/tests/Workflows/Runtime/Tests`: binding resolution, the start seed, the value after `SetCorrelationId`,
  and a replay re-materialization case.

**Target Platform**: Server libraries: Elsa 4 Core, Runtime, and the Elsa 3 import extension.

**Project Type**: Libraries inside the modular solution.

**Performance Goals**: No measurable change. One extra durable value per started instance. Accessor resolution is
linear in the number of declared names.

**Constraints**:
- Faithful or refuse (spec principle). No shim for time or randomness.
- Deterministic conversion output (spec 236's guarantee, SC-006).
- binding-pure-v1 is unchanged (ADR 0083). The new value is a declared parameter, so no new capability profile.
- The new value is read from committed state, never from live memory. Input re-materialization on replay, or on a
  `While` pass, then sees the committed value (ADR 0045 §3, #977).

**Scale/Scope**:
- 1 new Core binding type.
- 2 runtime switch cases and 1 context field.
- 1 seed change.
- 1 new ambient reference kind.
- Rewriter changes: parse options, the completion rule, accessor/built-in dispatch, helper inlining, the refusal
  table.
- Docs: the Mapping README migration story, the ADR 0045 amendment, the spec 095 contract amendment,
  `EXTENSION_POINTS.md` wording, and the diagnostic catalog.

## Constitution Check

*Loaded: `.specify/memory/constitution.md`, `.specify/memory/constitution-framework.md` and
`docs/contributing/review-checklist.md`. Re-checked after Phase 1: unchanged.*

| Gate | Status | Notes |
|---|---|---|
| §E2.2 Runtime takes no Design reference | PASS | The binding lives in `Elsa.Expressions.Core`; resolution lives in `Elsa.Workflows.Runtime(.Core)`. No Design reference is added |
| §E2.7 Elsa 3 stays behind the import boundary | PASS | Accessor naming, helper semantics and refusal text stay in `Extensions/Elsa3`. The Core binding is Elsa 4 vocabulary |
| §E2.6.1 executable-always-runs | PASS, risk noted | An older runtime build cannot read the new discriminator in a published executable (mixed-version or rollback). Elsa 4 is pre-GA (ADR 0080) and has no supported mixed-version deployment. Recorded in the ADR 0045 amendment |
| §E2.6.2 artifact-only runtime | PASS | Values come from runtime state, not design data |
| §E5 versioning | PASS | Additive: a new record and discriminator, a new enum member `Elsa3AmbientReferenceKind.GeneratedAccessor`, a new context property. `PublicAPI.Unshipped.txt` is updated. Removals under D-6 happen only with approval |
| §E6 naming | PASS | `WorkflowInstanceExpressionParameterBinding` follows the `{Source}ExpressionParameterBinding` family |
| §2.6.2 replacement contracts | PASS | None added |
| §2.17 duplication beats dependency | PASS | Elsa 3 Pascal-casing is a few lines in `Elsa3.Mapping`, not a Humanizer dependency (R-3) |
| §2.24.2 sanctioned patterns | PASS | The polymorphic binding record pattern, and ADR 0045's in-place amendment pattern |
| §2.22 / §2.22.1 docs and extension points | ACTION | Runtime `EXTENSION_POINTS.md` (resolver sources), Expressions `EXTENSION_POINTS.md` (instance metadata as a declared parameter), Mapping `EXTENSION_POINTS.md` (D-6), the Mapping README, and the value sources in the glossary entry "Activity input binding" |
| §2.23.1–§2.23.3 tests and visibility | PASS | One test per story, per refusal and per binding path. The new Elsa 3 helpers are internal |
| §2.23.5 exception boundaries | PASS | A missing correlation ID binds `null`. Nothing new throws on input |
| Decision record | ACTION | ADR 0045 amendment (#2717): workflow-instance metadata joins the canonical parameter-binding kinds. The spec 095 contract's "exactly one of" the four kinds is amended to match |

## Decisions

| # | Decision | Detail |
|---|---|---|
| D-1 | A top-level `return` wraps the body in a function | Applies to Expression and Script mode. The last statement must be a `return` or an expression; otherwise `VF-IMP-006`. Bodies without a top-level `return` are unchanged (R-1) |
| D-2 | Accessors resolve as Elsa 3 generates them | An output accessor wins and is refused. Standalone: then an exact input name, then an exact Pascal-cased variable name. Reusable: then a Pascal-cased input or variable name, because Elsa 3 declares inputs as variables there (R-2). A generated name equal to a built-in gives `VF-IMP-002` (R-3) |
| D-3 | One Core binding for instance metadata | `WorkflowInstanceExpressionParameterBinding(string member)`, with members `correlationId` and `instanceId`. An unknown member is rejected at construction. Discriminator `"WorkflowInstance"` (R-4) |
| D-4 | The correlation ID comes from the identity projection, seeded at start | `RuntimeWorkflowStateSeed.BuildSeedChanges` writes `identity:correlationId` from the dispatch payload. Resolution reads `RuntimeIdentityStateProjection.CorrelationId` from committed values; when absent, it binds JSON `null`. The instance ID is `WorkflowExecutionId` (R-5) |
| D-5 | Inline two helpers, refuse the rest | `isNullOrEmpty(x)` → `x == null \|\| x === ""`. `isNullOrWhiteSpace(x)` → a regex over .NET's `char.IsWhiteSpace` set. Both are emitted as a local-function prelude so each argument is evaluated once (R-6) |
| D-6 | **Proposed, needs approval:** remove the pending-graph path | Remove `Elsa3ActivityTarget.GraphLevel`, the resolver case, `IsDeferred`, and `Elsa3NodeConversionContext.Deferred` with its constructor parameter. They are public but unused, and broken since spec 239 (R-8). If not approved, keep them and fix their docs |
| D-7 | Refusal guidance is data | A static table in the rewriter maps each refused name to its message and guidance. The codes are unchanged (FR-023) |

## Project Structure

### Documentation (this feature)

```text
specs/247-elsa3-js-host-functions/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   ├── javascript-conversion.md     # what converts and to what; the refusal table with guidance
│   └── instance-metadata-binding.md # the new Core binding: wire shape, resolution, determinism
└── tasks.md                          # /speckit-tasks
```

### Source Code (repository root)

```text
Essentials/src/Expressions/Core/
├── Models/ExpressionDefinition.cs              # + WorkflowInstanceExpressionParameterBinding, JsonDerivedType
└── PublicAPI.Unshipped.txt
Essentials/src/Workflows/Runtime/Core/Models/
└── RuntimeInputBindingResolutionContext.cs     # + CorrelationId
Essentials/src/Workflows/Runtime/Elsa.Workflows.Runtime/Services/
├── Values/RuntimePortableExpressionEvaluator.cs        # cases in MaterializeParametersAsync and DetermineDependencyPolicy
├── Values/RuntimeActivityInputSnapshotMaterializer.cs  # fill CorrelationId from the identity projection
├── …/WorkflowIntrinsicExecutor.cs                      # fill CorrelationId for intrinsics
└── …/RuntimeWorkflowStateSeed.cs                       # seed identity:correlationId at start
Extensions/Elsa3/src/Mapping/
├── Services/Elsa3ExpressionRewriter.cs         # parse options, completion, accessors, built-ins, helpers, refusals
├── Services/Elsa3ValueFlowLowerer.cs           # GeneratedAccessor resolution
├── Internal/Elsa3JavaScriptNames.cs            # new, internal: Pascalize, name validity, built-in names
├── ActivityMap/Elsa3ActivityTarget.cs          # D-6
├── Resolution/Elsa3ActivityResolver.cs         # D-6
├── Conversion/Elsa3DefinitionConverter.cs      # D-6
├── Conversion/IElsa3NodeConverter.cs           # D-6
└── README.md, EXTENSION_POINTS.md
Extensions/Elsa3/src/Models/Elsa3ActivityDiagnosticCodes.cs    # retire GRAPH-CONVERSION-PENDING
docs/adr/0045-…md                                              # amendment (#2717)
specs/095-value-flow-redesign/contracts/expression-and-import-contract.md
specs/236-elsa3-activity-map/contracts/diagnostic-codes.md     # mark retired
```

Tests:
- `Extensions/Elsa3/tests/Mapping/Tests`: a new `Elsa3JavaScriptHostFunctionTests.cs`, plus updates to
  `Elsa3JavaScriptConversionTests.cs`, `Elsa3ExpressionRewriterTests.cs` and `Elsa3ActivityResolverTests.cs`.
- `Essentials/tests/Expressions/Tests/ExpressionParameterBindingWireTests.cs`.
- `Essentials/tests/Workflows/Runtime/Tests`.

**Structure Decision**: Everything stays inside existing projects. No project or package is added.

## Implementation phases

1. **Top-level return** (US1). Rewriter only; unblocks everything else.
2. **Generated accessors** (US2). Lowerer and rewriter.
3. **Instance metadata binding** (US3).
   - Core and Runtime first, with their own tests.
   - Then the converter lowering.
   - Then the ADR 0045 and spec 095 amendments.
4. **Helpers and refusal table** (US4, US5), plus the Mapping README migration story.
5. **Cleanup** (US6). Retire the code; D-6 removals happen only after approval.
6. **Measure** (SC-001, SC-002). Rerun the throwaway corpus probe (never committed) and put the numbers in the PR.
7. **Follow-up** (FR-022). Draft the issue for an Elsa 4 time and unique-value capability; file it only with the
   maintainer's approval.

## Complexity Tracking

No constitution violations to justify.
