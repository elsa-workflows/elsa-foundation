# Implementation Plan: JSON data integrity baseline

**Branch**: `agent/claude/3052-s9-json-integrity` | **Date**: 2026-11-03 | **Spec**: [spec.md](spec.md)

**Status**: provisional, like the spec. The ADR PRs it waits on are listed in the spec's *Approval* section. Nothing
here is implemented until #3052's specs are defined and this spec is approved.

## Summary

Make "data passes through unchanged" a rule the code enforces, as ADR 0091 says. The technical approach (research
R-1 to R-13):

- **One baseline.** A read-only `JsonBaseline.Options` lives in `Elsa.Serialization.Core`. It has camelCase member
  names, no key policy, no number handling, nulls kept, exact member reads, the relaxed encoder, and a raw
  `JsonElement` converter.
- **API contexts derive from it.** Every owner context and API group context is constructed from a copy, and the two
  host-wide `Configure` calls become resolver-only insertions.
- **Conversions derive from it.** Every data-to-.NET conversion on the value path uses the target type's conversion
  contract (default or scoped), in both directions. A contract-aware pre-scan warns about unmatched keys, and a
  date/time value without an offset needs an explicit interpretation in the contract, else it is refused.
- **Hash inputs stay frozen.** They keep their own options as register entries; changing one is a separate decision.
  Equality checks of serialized values move to one semantic comparison helper.
- **Git files keep data text.** The Git hash keeps its current, registered input.
- **The runtime store gets the baseline writer.** It gains a byte-level golden fixture. There is no version bump and
  no migration if fixed examples show old and new code read old and new rows the same; otherwise a stamped change.
- **A guard reports deviations.** An architecture test combines a source scan with reflection, and reads the register
  in `docs/serialization.md` plus a legacy list (ratchet file) for option-less calls on Elsa's own contracts.
- **The Elsa 3 converter** rewrites literal keys that differ only in case.

## Technical Context

**Language/Version**: C# / .NET 10 (`global.json` SDK 10.0.300); Studio TypeScript (no change expected, R-3).

**Primary Dependencies**: System.Text.Json, NativeEndpoints 1.0.0-preview.6, EF Core (four providers), CShells
features.

**Storage**: runtime EF store (`RuntimeArtifactJson` and five store option sets), design EF store (`EfDesignSupport`,
`IPayloadSerializer`), Git reconciliation files. No schema change.

**Testing**: xUnit (no FluentAssertions); the architecture suite (`Essentials/tests/Architecture/Tests`); serializer
tests (`Essentials/tests/Serialization/Tests`); contract tests in Publishing and Activities Design; behavioural drives.

**Target Platform**: Elsa 4 server hosts (Foundation host, Workbench).

**Project Type**: libraries (serialization, workflows, activities, APIs) plus the Elsa 3, ETL, Agent, Rules and
ExtensionBuilder extensions.

**Performance Goals**: raw `JsonElement` writes skip re-encoding (`skipInputValidation: true`), so they cost no more
than today. The unmatched-key pre-scan is one pass over the keys of each converted object.

**Constraints**:
- No ADR 0038 artifact hash and no stored activity-definition hash changes (preimages frozen, R-1).
- Stored rows stay readable by old and new code (R-9).
- Elsa 3 knowledge stays in `Extensions/Elsa3` (§E2.7).

**Scale/Scope**: R-10 counts 187 option constructions in 175 files, 32 source-generation attributes and about 267
option-less serializer calls. The value path (about 25 sites, R-10) and the API contexts (about 30) migrate in this
spec. The remaining option-less calls on Elsa's own contracts are listed in the legacy list, which may only shrink (Q3).

## Constitution Check

*Loaded: `.specify/memory/constitution.md`, `.specify/memory/constitution-framework.md` and
`docs/contributing/review-checklist.md`. Re-checked after Phase 1: unchanged.*

| Gate | Status | Notes |
|---|---|---|
| §2.24.2 / §2.24.3 sanctioned patterns | ACTION | A shared read-only baseline with a deviation register is a new cross-cutting pattern (ADR 0091 *Consequences*). It goes through the §2.24.3 gate together with ADR 0091. T002 checks that this happened before any code lands. |
| §E2.2 Runtime takes no Design reference | PASS | The baseline is in `Elsa.Serialization.Core`, which Runtime.Core already references (R-11). |
| §2.17 duplication beats dependency | PASS | The canonical helper and the pre-scan live once in `Elsa.Serialization.Core`; every caller already references it. |
| §E2.6.1 executable-always-runs | PASS | An unmatched key is a warning, never a refusal (decision 2). Executable hashes do not move (R-1). |
| §E5 / §4.2 versioning | PASS | Additive surface in `Elsa.Serialization.Core` (`JsonBaseline`, `JsonConversionContract`, `SemanticJson`, `UnmatchedMemberScanner`). The wire changes are fixes under matrix row 21, before GA. |
| §E7 CI tiers | PASS | The architecture guard runs in the Fast tier, as the other baseline guards do. |
| §2.22 docs | ACTION | `docs/serialization.md` gains the baseline, the data rule, the register and the ratchet. Its FastEndpoints wording goes. The glossary gains *baseline* and *baseline deviation*. |
| §2.23.1–§2.23.2 tests | PASS | One test per acceptance scenario, the guard's negative tests, and hash-stability tests. |
| ADR 0038, spec 180 FR-028 | PASS | Identity-hashed formats are untouched; preimages are register entries. |
| ADR 0091 (#3408), ADR 0046 *Dates* (#3422) | WAIT | Applied as drafted. Findings F-1 to F-6 are raised on #3408, and this plan follows any change before agreement. |

## Decisions

| # | Decision | Detail |
|---|---|---|
| P-1 | `JsonBaseline` in `Elsa.Serialization.Core` | FR-001 to FR-003, FR-007, FR-009. The baseline is built once and `MakeReadOnly()`; `JsonBaseline.CreateCopy()` returns a mutable copy. Its `RawJsonElementConverter` uses `WriteRawValue(GetRawText(), skipInputValidation: true)`. |
| P-2 | API contexts derive from it | FR-002, FR-006, FR-017. Owner `WireContext`s and every `MapEndpointGroup` context get a constructed instance from a copy (R-5). Generated `.Default` instances are replaced by constructed ones. The `ConfigureHttpJsonOptions` calls insert resolvers only (R-6). |
| P-3 | Conversion contracts | FR-004, FR-012 to FR-014, FR-016. `JsonConversionContract` (default and scoped) builds a baseline copy with the contract's naming, matching, mappings and date/time interpretation; it is recorded with a version in the published conversion contract and used at every R-10 site, in both directions. The default is camelCase with exact matching. The date/time converter uses the value's offset or the contract's declared source time zone or assume-UTC policy, and refuses otherwise. |
| P-4 | Unmatched-key pre-scan | FR-005, Q1, R-13. `UnmatchedMemberScanner` runs against the effective contract (naming, case sensitivity, mappings, dictionaries, extension data); a custom converter supplies matching information or the type is reported as not covered. Codes: `json-unmatched-member` (warning), `json-unmatched-member-refused` (strict contract), `json-unmatched-coverage-unavailable` (info). |
| P-5 | Hash inputs frozen | FR-021, Q4, R-1. Each existing hash input keeps its options and preimage and gets a register entry with its input contract. Equality checks that compare serialized text move to `SemanticJson` (semantic comparison). Direct-text hashes are listed and stay as they are; changing one is a separate decision. |
| P-6 | Git files keep data text | FR-008, FR-022, Q5, R-7. The exporter indents with a writer that writes `JsonElement` raw. `GitCanonicalJson.HashFile` keeps its current input and is registered. |
| P-7 | Runtime durable writers | FR-023, Q7, R-9. `RuntimeArtifactJson` and the five store option sets derive from the baseline and register their frozen converters. Fixed serialized examples and compatibility checks show old and new code read old and new rows with the same meaning; then no stamp bump, otherwise a stamped format change. A byte-level golden pins one stored value. |
| P-8 | Content type | FR-010, Q6, R-12. Elsa's JSON APIs send `application/json` and `nosniff`. A workflow-authored response uses its author's content type; with none, text is `text/plain`, anything else `application/json`. |
| P-9 | Guard and register | FR-019, FR-020, Q2, Q3. `JsonBaselineArchitectureTests` scans every source root and reads the register table in `docs/serialization.md` and the legacy list `Baselines/json-optionless-calls.txt` (may only shrink, never a workflow-data path; a new unlisted call fails). |
| P-10 | Elsa 3 case-only keys | FR-015. The rewrite happens at literal lowering in `Extensions/Elsa3`. The ambiguous case is refused with `ELS3-LITERAL-KEY-AMBIGUOUS`. |
| P-11 | Diagnostics | [contracts/json-baseline.md](contracts/json-baseline.md). |

## Project Structure

### Documentation (this feature)

```text
specs/1207-json-data-integrity-baseline/
├── spec.md
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/json-baseline.md
├── checklists/requirements.md
└── tasks.md
```

### Source Code (repository root)

```text
Essentials/src/Serialization/Core/
  ├── JsonBaseline.cs                      # new: baseline, CreateCopy, DataBoundary
  ├── RawJsonElementConverter.cs           # new
  ├── SemanticJson.cs                      # new: semantic value comparison
  └── UnmatchedMemberScanner.cs            # new
Essentials/src/Serialization/SystemText/Services/JsonPayloadSerializer.cs   # derive, keep nulls
Essentials/src/Workflows/Runtime/Api/WorkflowsRuntimeJsonContext.cs         # no key policy
Essentials/src/Workflows/Publishing/Api/WorkflowsPublishing{JsonContext,ApiFeature}.cs
Essentials/src/Activities/Design/Api/ActivitiesDesign{JsonContext,ApiFeature}.cs
Essentials/src/{Connections,Secrets}/Api/*                                   # no key policy
Essentials/src/Workflows/Design/Api/Services/AuthoringSchemaExporter.cs
Essentials/src/Activities/Runtime/Elsa.Activities.Runtime/Services/
  ├── ActivityValueJson.cs                # replaced by the default conversion contract (#3801)
  └── ActivityInputHydrator.cs             # pre-scan
Essentials/src/Workflows/Runtime/Elsa.Workflows.Runtime/Services/Values/RuntimeActivityInputMaterializer.cs
Essentials/src/Workflows/Publishing/Elsa.Workflows.Publishing/Services/RuntimeInputBindingCompiler.cs
Essentials/src/Workflows/Runtime/Persistence/EntityFrameworkCore/Stores/
  ├── RuntimeArtifactJson.cs, EfBookmarkStateStore.cs, …   # derive, register frozen converters
  └── EfWorkflowTriggerBindingStore.cs                     # canonical compare
Essentials/src/Workflows/Design/Reconciliation/Git/Services/{GitCanonicalJson,GitWorkflowExporter}.cs
Extensions/Elsa3/src/Mapping/…                             # case-only key rewrite
Extensions/Etl/src/Core/EtlArtifactJson.cs                 # canonical or registered
Essentials/tests/Architecture/Tests/JsonBaselineArchitectureTests.cs       # new
Essentials/tests/Architecture/Tests/Baselines/json-optionless-calls.txt    # new
docs/serialization.md
Essentials/apps/Elsa.Workbench/Constants/JsonSerializerOptionsCache.cs     # delete
```

**Structure Decision**: existing projects only. There are four new files in `Elsa.Serialization.Core`, one new test
class and one ratchet file.

## Complexity Tracking

| Violation | Why needed | Simpler alternative rejected because |
|---|---|---|
| New cross-cutting pattern (§2.24.3) | One baseline is the point of ADR 0091 | Fixing the key policy only leaves re-escaping and independent option sets (ADR 0091, *Alternatives*) |
