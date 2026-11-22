# Implementation Plan: Elsa 3 converter: a null activity member converts to what Elsa 3 ran with

**Branch**: `agent/claude/3200-elsa3-null-input` | **Date**: 2026-10-23 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/267-elsa3-null-members/spec.md`

## Summary

The activity map records, per member, what a `null` converts to (`OnNull`: no binding, typed null, Elsa 3's value, or
refuse). The normaliser's member pass applies it before the member rule, so no bare JSON `null` reaches the state
mapper. A refusal uses one new code, `ELS3-ACTIVITY-NULL-UNCONVERTIBLE`. The state mapper reads the typed null as a
`Literal` with a null value, which publication accepts for a nullable input. Outputs and members resolved by name get
no binding. A coverage test checks every mapped input against the real Elsa 4 catalog. Publication, the runtime and
Elsa 4 defaults do not change.

## Technical Context

**Language/Version**: C# / .NET 10

**Primary Dependencies**: none added.

**Storage**: N/A

**Testing**: xunit only (no FluentAssertions).
- Mapping tests in `Extensions/Elsa3/tests/Mapping/Tests`, through `ConversionServices` and `RealActivityCatalog`.
- One integration test class in `Extensions/Elsa3/tests/Activities/Design/Import/Integration/Tests`, through
  `Elsa3ImportHost`: import, publish, run.

**Target Platform**: library (`Elsa3.Mapping`)

**Project Type**: single library and its two test projects

**Performance Goals**: N/A

**Constraints**:
- Faithful or refuse.
- Converter only.
- Build and test only the touched projects.

**Scale/Scope**: about 120 production lines in five files (the rule type, the table, the normaliser, the state mapper,
the codes), one new code, one mapping test class, two coverage checks, one integration test class and one fixture.

## Constitution Check

*Loaded: `.specify/memory/constitution.md`, `.specify/memory/constitution-framework.md` and
`docs/contributing/review-checklist.md`. Re-checked after Phase 1: unchanged.*

| Gate | Status | Notes |
|---|---|---|
| §E2.7 Elsa 3 stays behind the import boundary | PASS | Every production change is in `Extensions/Elsa3/src/Mapping` and `Extensions/Elsa3/src/Models` |
| §E2.2 Runtime takes no Design reference | PASS | No project reference changes |
| §E5 versioning | PASS | `Elsa3MemberRule` gains an optional trailing parameter; one public constant is added to `Elsa3ActivityDiagnosticCodes`; nothing is removed or renamed |
| §2.6.2 replacement contracts | PASS | None added or changed |
| §2.17 duplication beats dependency | PASS | No new dependency |
| §2.24.2 sanctioned patterns | PASS | Existing patterns only: a member-rule field read by the normaliser's member pass, diagnostics added to the plan |
| §2.22 / §2.22.1 docs and extension points | ACTION | Mapping README (null members, the new code) and the spec 236 member map (FR-009). No extension point changes |
| §2.23.1–§2.23.3 tests and visibility | PASS | A test per table row (SC-004), the coverage check (FR-008) and the end-to-end test (SC-003). The new record is public like `Elsa3MemberRule` |
| §2.23.5 exception boundaries | PASS | Refusals are diagnostics; nothing new throws on input |

## Decisions

| # | Decision | Detail |
|---|---|---|
| D-1 | Per-member `OnNull` | `Elsa3MemberRule.OnNull` (`Elsa3NullConversion`), set in `Elsa3BuiltInActivityMap.Entries.cs` for every Carry and Narrowed-contract input, from the spec's table (R-6) |
| D-2 | Applied in the member pass | `ApplyRule` handles a null (JSON `null`, or a literal holding null, R-5) through `OnNull` before the rule; an R2/R5 default without `OnNull` keeps its existing path through `ApplyDefaults` (R-2) |
| D-3 | Typed null | `Elsa3TypedValues.CreateNull(alias)`, the Elsa 4 input's alias; `Elsa3ActivityToState` maps it to `ArgumentValue(null, "Literal")`. `ApplyDefaults` leaves it alone (R-3, R-8) |
| D-4 | Never keep a bare null | `Elsa3ActivityToState.Map` drops a value it cannot read, null included; the "a JSON null is kept" branch goes (R-2) |
| D-5 | One new code | `ELS3-ACTIVITY-NULL-UNCONVERTIBLE`; the node fails, so `CheckRequiredInputs` does not repeat it (R-4) |
| D-6 | Emitted values count as bound | `IsEmittedDefault` also accepts an `OnNull` emitted value (Correlate / SetName `""`) (R-9) |
| D-7 | Outputs and by-name members | A null output and a null member in `BindByName` get no binding (R-10) |
| D-8 | Coverage against the real catalog | Two checks in `Elsa3ActivityMapCoverageTests` (R-7) |
| D-9 | Deliberate null | Read as a JSON `null` (unchanged behaviour); the declared-default gap goes to #3281 (R-5). Spec FR-005 corrected |
| D-10 | Measurement | Throwaway import-host test, before and after (R-11) |

## Project Structure

### Documentation (this feature)

```text
specs/267-elsa3-null-members/
├── spec.md
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   └── null-member-diagnostic.md
├── checklists/
│   └── requirements.md
└── tasks.md            (speckit-tasks)
```

### Source Code (repository root)

```text
Extensions/Elsa3/src/Models/
└── Elsa3ActivityDiagnosticCodes.cs              + NullUnconvertible

Extensions/Elsa3/src/Mapping/
├── ActivityMap/Elsa3MemberRule.cs               + OnNull, Elsa3NullConversion, Elsa3NullRule
├── ActivityMap/Elsa3BuiltInActivityMap.Rules.cs + null helpers
├── ActivityMap/Elsa3BuiltInActivityMap.Entries.cs   OnNull per member (the spec's table)
├── Conversion/Elsa3TypedValues.cs               + CreateNull, IsNullLiteral
├── Conversion/Elsa3ActivityNormalizer.cs        D-2, D-3, D-5, D-6, D-7
├── Mappings/Elsa3ActivityToState.cs             D-3, D-4
└── README.md                                    null members, the new code

specs/236-elsa3-activity-map/member-map.md       FR-009: null conversions

Extensions/Elsa3/tests/Mapping/Tests/
├── Elsa3NullMemberTests.cs                      new: one case per table row, deliberate null, outputs, by-name
└── Elsa3ActivityMapCoverageTests.cs             + D-8

Extensions/Elsa3/tests/Activities/Design/Import/Integration/Tests/
├── NullMemberPublishTests.cs                    new: SC-003
└── Fixtures/null-members.json                   new
```

**Structure Decision**: library change inside the existing `Elsa3.Mapping` project; tests in the existing Mapping and
Import integration test projects.

## Complexity Tracking

None.

## #3281 follow-up plan

This S-0 amendment implements FR-011–FR-015 without reopening the original bare-null delivery. It uses the existing
proposal branch `agent/codex/3281-elsa3-default-parity` and spec directory. The proposal remains documentation only.

### Approval and delivery route

The initiative's recorded coordinator-only spec approval rule applies. The final requirements need coordinator
agreement and merge into `initiative/3052-elsa3-to-elsa4-conversion` before production work under D-18. No new ADR
decision is proposed: HTTP/scheduling/outcome/default behavior remains unchanged, and unsupported source behavior is
refused under the existing faithful-or-refuse rule. An actual ADR change, if discovered, gets its own agreement route.

Retain proposal #3945. After its approval and merge, cut a fresh implementation branch from the initiative and open
a separate implementation PR fixing #3281. The documentation PR does not complete the bug. Main synchronization
belongs to the existing coordinator/S-2 lane; do not create a competing main-sync proposal.

### Implementation route

1. Add optional init properties `OnAbsent`, `OnBlank` and `OnLiteralNull` to the existing `Elsa3MemberRule` body,
   reusing `Elsa3NullConversion` actions. Keep its positional constructor and unconfigured/custom-map behavior.
   Introduce no service, feature, dependency or general null-policy record.
2. Classify supported source shapes before literal-null/default normalization and before any `!IsBound` shortcut.
   Preserve the case-insensitive source inventory, original path, duplicate-binding checks and emitted-value markers.
   Resolved Literal-without-value belongs to literal-null; the named empty wrappers belong to the legacy default path.
3. Record only the named built-in corrections. Preserve existing `OnNull` values. Absent status lists emit `[200]`;
   literal-null/blank-to-null and explicit empty status lists refuse the current incompatible routing. A focused
   nonempty status-list contract must inspect explicit `[]` before unbound/default handling; no generic empty-list
   policy is added. Nonempty supported lists retain the merged outcome conversion.
4. Use focused narrowed contracts for positive Timer literals and response status values the target can reproduce.
   Preserve the existing generic `LiteralTimeSpanToString` / `EnumNameToInt` behavior for custom maps. Built-in
   status handling refuses 0/negative/invalid values and unproven computed ranges instead of normalizing to 200;
   Timer refuses zero/negative/invalid intervals instead of selecting one minute.
5. Null refusals retain their existing family; missing/blank/empty-list/invalid-value refusals use the existing
   contract/no-counterpart families with truthful shape-specific guidance. Mark the node failed so required-input
   checks do not add a second refusal. No imported record or publication is written for a refused selection.
6. Update Mapping README, the owning extension description and canonical spec236 member-map rows. Keep the original
   spec267 receipts intact and this follow-up's delivery status separate. Add no documentation/source assertion tests.

### Constitution and integration check

- Ratified framework v6.1.0 / Elsa v6.5.0: import boundary (§E2.7), Runtime/Design boundary (§E2.2), existing map data
  and normalizer; no project edge, replacement/contributor seam or dependency. No reliance on provisional framework
  §2.24 or Elsa §E2.8/§E2.9.
- ADR0080 risk-based tests / framework §2.23: observable conversion and actual import/runtime behavior, with causal
  red/green controls and complete affected projects. New logic stays internal/private; public map additions are
  reviewed/tracked without changing the existing constructor.
- S-3/S-7/S-8 are merged contracts, not pending proposals this work implements. Do not alter target outcomes, script
  write semantics or the bridge to accommodate a source member.
- Spec1232 owns generic null/missing/undefined, required-before-default, supported type defaults, null substitution,
  converter policies and their pins/hash. This follow-up authors the named legacy effective values or refuses them.
  Delay zero is an explicit converter-authored value, not an S-2 runtime type default. S-2's separate null-intrinsic
  amendment remains free to change its owned rows; no S-2 worktree or artifact is edited here.
- Framework §2.22: extend the existing owner/member-map documentation, rather than a new explanation of core concepts.

### Validation route

Use `Elsa3.Mapping.Tests` and the existing HTTP/SQLite import integration project. Matrix controls cover both HTTP
variants; missing/bare-null/supported-empty/Literal-null/blank/empty-list shapes; Literal-without-value; malformed
metadata; literal `"null"`; valid and invalid boundary values; case-insensitive keys; duplicate bindings; caller
immutability; original member paths; and no duplicate required-input refusal on an already refused node. Distinct
member-specific errors are not suppressed by a new first-error policy.

Owned disposable hosts and a local HTTP receiver prove For iteration counts, zero Delay due time, effective GET,
ContentType/request-body differences (including empty string Content), and default `[200]` paths on 200/404/500.
Cleared status lists are refused before any request or import write. Timer invalidity and raw HTTP status 0/negative
values are no-default controls. Preserve the existing accepted Fault.Message text difference.

Use the established isolated local/Linux path when Windows execution is blocked, the build-slot wrapper where
installed, bounded concurrency and load checks for timing failures. Freeze source/commands/results; compare discovery
with whole-project execution; run architecture Release/Debug graph restores, whole Architecture and generated-filter
check. Reuse the authorized read-only 36-definition corpus source and record real current measurements; do not assume
it is unavailable or borrow another issue's pass. Historical source inspection is not historical runtime execution.

Review/fix/review until an independent round has no remaining verified findings, without a numeric cap. CI waits are
deferred to initiative integration by the dispatch; do not poll it or infer success. Organizational Code Review and
coordinator acceptance/merge remain. Recheck the corrected members against S-2 policies on/off when that implementation
is available; record that integration as NOT RUN until exercised, without moving its ownership into S-0.
