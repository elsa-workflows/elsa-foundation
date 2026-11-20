# Implementation Plan: JSON data integrity baseline

**Branch**: `agent/claude/3052-s9-json-integrity` | **Date**: 2026-11-03, rechecked 2026-11-20 | **Spec**: [spec.md](spec.md)

**Status**: ready with the spec. ADR 0091 (`771ba6679f`) and the ADR 0046 amendment are merged into the initiative
branch. Implementation starts on its own branch once the spec is approved and merged.

## Summary

Make "data passes through unchanged" a rule the code enforces, as ADR 0091 says. The technical approach (research
R-1 to R-14):

- **One baseline.** A read-only `JsonBaseline.Options` lives in `Elsa.Serialization.Core`. It has camelCase member
  names, no key policy, no number handling, nulls kept, exact member reads, the relaxed encoder, and a raw
  `JsonElement` converter.
- **API contexts derive from it.** Every owner context and API group context is constructed from a copy, and the two
  host-wide `Configure` calls become resolver-only insertions.
- **Conversions derive from it.** Every data-to-.NET conversion on the value path uses the target type's conversion
  contract (default or scoped), in both directions. The default is camelCase with exact matching; a scoped contract is
  an attribute on the type or input member, with nothing stamped into executables (Q9). A contract-aware pre-scan
  warns about unmatched keys, and a date/time value without an offset is refused; there is no date interpretation
  setting for run-time values (Q10). Elsa's own APIs accept dates only with `Z` or an offset.
- **One hash input.** Every hash over JSON moves once to `JsonHashInput`: explicit settings, SHA-256 over the
  canonical form, a golden test per hash, and a guard that fails on a hash that bypasses it (Q11). Equality checks of
  serialized values use the same canonical form (ADR 0091 decision 3); the ADR 0092 value digest
  (`VariableValueDigest`) moves to `JsonHashInput` with the other hashes.
- **Git files keep data text.** The Git hash is `JsonHashInput` over the file.
- **The runtime store gets the baseline writer.** It gains a byte-level golden fixture. There is no version bump and
  no migration if fixed examples show old and new code read old and new rows the same; otherwise a stamped change.
- **A guard reports deviations.** An architecture test combines a source scan with reflection, and reads the register
  in `docs/serialization.md` plus a legacy list (ratchet file) for option-less calls on Elsa's own contracts.
- **The Elsa 3 converter** rewrites literal keys that differ only in case, and gives date literals their offset from
  S-1's one date interpretation setting.
- **Studio has a time zone setting** (default: the browser's zone). Every date it shows or sends uses that zone with
  a visible offset; the engine stays in UTC.

## Technical Context

**Language/Version**: C# / .NET 10 (`global.json` SDK 10.0.300); Studio TypeScript (the time zone setting, a shared
date formatter and the Run dialog's date input, FR-016b; no other change, R-3).

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
- Hashes change once, to `JsonHashInput` (Q11), with fixtures and goldens regenerated; after that a golden test pins each one.
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
| §E2.6.1 executable-always-runs | PASS | An unmatched key is a warning by default; only a strict scoped contract refuses (decision 2). Executable hashes move once, to `JsonHashInput` (Q11). |
| §E5 / §4.2 versioning | PASS | Additive surface in `Elsa.Serialization.Core` (`JsonBaseline`, `JsonConversionContract`, `JsonConversionAttribute`, `JsonCanonicalForm`, `JsonHashInput`, `StrictDateTimeConverters`, `UnmatchedMemberScanner`). The wire changes are fixes under matrix row 21, before GA. |
| §E7 CI tiers | PASS | The architecture guard runs in the Fast tier, as the other baseline guards do. |
| §2.22 docs | ACTION | `docs/serialization.md` gains the baseline, the data rule, the register and the ratchet. Its FastEndpoints wording goes. The glossary gains *baseline* and *baseline deviation*. |
| §2.23.1–§2.23.2 tests | PASS | One test per acceptance scenario, the guard's negative tests, and hash-stability tests. |
| ADR 0038, spec 180 FR-028 | PASS | Artifact hashes change once, in this spec, as the explicit hash-input decision ADR 0091 decision 1 allows (Q11). Before the first stable release that costs regenerated fixtures, goldens and stored executables, not an in-place format change for stored releases. |
| ADR 0091, ADR 0046 *Adapter rules* and *Dates* | PASS | Both merged into the initiative branch (`771ba6679f`; #3422 on 16 Nov). Findings F-1 to F-6 are in ADR 0091. |
| Compatibility from first stable | PASS | No shim, upcaster or format-version check; stored fixtures and migrations are regenerated in place (FR-022a, FR-023). |

## Decisions

| # | Decision | Detail |
|---|---|---|
| P-1 | `JsonBaseline` in `Elsa.Serialization.Core` | FR-001 to FR-003, FR-007, FR-009. The baseline is built once and `MakeReadOnly()`; `JsonBaseline.CreateCopy()` returns a mutable copy. Its `RawJsonElementConverter` uses `WriteRawValue(GetRawText(), skipInputValidation: true)`. |
| P-2 | API contexts derive from it | FR-002, FR-006, FR-017. Owner `WireContext`s and every `MapEndpointGroup` context get a constructed instance from a copy (R-5). Generated `.Default` instances are replaced by constructed ones. The `ConfigureHttpJsonOptions` calls insert resolvers only (R-6). |
| P-3 | Conversion contracts | FR-004, FR-004a, FR-012 to FR-014, FR-016. `JsonConversionContract` (default and scoped) builds a baseline copy with the contract's naming, matching and mappings, and is used at every R-10 site, in both directions. The default is camelCase with exact matching. A scoped contract is read from one attribute, `[JsonConversion]`, on the type or the input member, plus `[JsonPropertyName]`; it is not stamped into the executable or checked at run time (Q9). Contracts are cached per type and member. Dates go through `StrictDateTimeConverters`: an offset or `Z` is required, a `DateTime` is normalized to UTC, a `DateTimeOffset` keeps its offset; otherwise `json-datetime-offset-missing` (Q10). On write, an `Unspecified` `DateTime` follows the converter's mode: refused in conversion contracts, written as UTC in API wire contexts. |
| P-4 | Unmatched-key pre-scan | FR-005, Q1, R-13. `UnmatchedMemberScanner` runs against the effective contract (naming, case sensitivity, mappings, dictionaries, extension data); a custom converter supplies matching information or the type is reported as not covered. Codes: `json-unmatched-member` (warning), `json-unmatched-member-refused` (strict contract), `json-unmatched-coverage-unavailable` (info). |
| P-5 | One hash input | FR-021, FR-022, FR-022a, Q11, R-1, R-14. `JsonHashInput.Hash<T>(value)` serializes with its own explicit options (camelCase, nulls kept, string enums, max depth 1000; no reference to `Web`, `.Default` or the baseline) and hashes the canonical form with SHA-256; `Hash(JsonElement)` skips the serialization. Every hash over JSON moves to it, including `WorkflowExecutableHasher` and its context, `ActivityContract`, `DefaultActivityDefinitionHasher`, `ExecutableActivityTemplateBehaviorHasher`, the design-store and library fingerprints, `GitCanonicalJson`, `EtlArtifactJson` and `VariableValueDigest`; hand-written writers hash their output through it. `LegacyFingerprint` and the V1 verification in `WorkflowExecutableHasher.MatchesHash` are deleted. Each hash gets a golden test; fixtures, goldens and stored executables are regenerated once; nothing verifies an old value. Equality checks move to `JsonCanonicalForm.Equals`. |
| P-5a | One canonical form | FR-021a. `JsonCanonicalForm` in `Serialization/Core/` writes the canonical form (members ordered by unescaped UTF-8 name, strings unescaped, numbers as coefficient and exponent, arrays in order, `null` members kept) and offers `Equals(JsonElement, JsonElement)`. The writer moves from `VariableValueDigest`, which moves to `JsonHashInput` (P-5); its digests are regenerated once. |
| P-6 | Git files keep data text | FR-008, FR-022, Q5, R-7. The exporter indents with a writer that writes `JsonElement` raw. `GitCanonicalJson.HashFile` becomes `JsonHashInput` over the parsed file. |
| P-7 | Runtime durable writers | FR-023, Q7, R-9. `RuntimeArtifactJson` and the five store option sets derive from the baseline and register their frozen converters. Fixed serialized examples and compatibility checks show old and new code read old and new rows with the same meaning; then no migration, otherwise regenerated migrations and fixtures in place. A byte-level golden pins one stored value. |
| P-8 | Content type | FR-010, Q6, R-12. Elsa's JSON APIs send `application/json` and `nosniff`. A workflow-authored response uses its author's content type; with none, text is `text/plain`, anything else `application/json`. |
| P-9 | Guard and register | FR-019, FR-020, Q2, Q3. `JsonBaselineArchitectureTests` scans every source root and reads the register table in `docs/serialization.md` and the legacy list `Baselines/json-optionless-calls.txt` (may only shrink, never a workflow-data path; a new unlisted call fails). |
| P-10 | Elsa 3 literals | FR-015, FR-016a. Both rewrites happen at literal lowering in `Extensions/Elsa3` (`Elsa3ValueFlowLowerer`, which already parses literals bound to date members). Case-only keys: the ambiguous case is refused with `ELS3-LITERAL-KEY-AMBIGUOUS`. Dates: the lowerer reads `Elsa3ConversionOptions.DateInterpretation` and `SourceTimeZone` (S-1) and writes the offset, `Z`, or refuses. |
| P-10a | Studio time zone | FR-016b. A `timeZone` appearance preference (`"browser"` by default, `"UTC"` or an IANA ID), on Settings › Appearance with the other preferences. One shared formatter in `workflowFormatting.tsx` (`Intl.DateTimeFormat` with `timeZone` and `timeZoneName: "longOffset"`) shows every date with its offset; the `toLocaleString()` date displays move to it. `workflowRunInputs.ts` turns `datetime-local` text into ISO 8601 with that zone's offset on that date; the dialog shows the zone. Vitest covers winter and summer dates and the UTC setting; the composed host is checked per Studio UX acceptance. |
| P-10b | API date rule | FR-016c. The API wire contexts and the conversion contracts carry `StrictDateTimeConverters` (not the store option sets, which read only what Elsa wrote); an offset-less value fails binding with a 400 problem response carrying `json-datetime-offset-missing` and the path. Instants Elsa produces are written in UTC. |
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
  ├── JsonBaseline.cs                      # new: baseline, CreateCopy
  ├── RawJsonElementConverter.cs           # new
  ├── JsonCanonicalForm.cs                 # new: canonical form (moved from VariableValueDigest)
  ├── JsonHashInput.cs                     # new: the one hash input over JSON
  ├── JsonConversionContract.cs            # new: default and scoped conversion contracts
  ├── StrictDateTimeConverters.cs          # new: offset required (own contracts and conversions)
  ├── JsonConversionAttribute.cs           # new: scoped conversion contract
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
Essentials/src/Workflows/Runtime/Core/Models/VariableValueDigest.cs       # hashes through JsonHashInput
Extensions/Elsa3/src/Mapping/Services/Elsa3ValueFlowLowerer.cs             # case-only keys, date literals
Studio/src/essentials/Elsa.Studio.Hosting/Client/src/app/{settings/AppearancePage.tsx,themes/appearance.ts}  # time zone setting
Studio/src/essentials/Elsa.Studio.Workflows/Client/src/{workflowFormatting.tsx,workflowRunInputs.ts,workflow-editor/WorkflowRunInputDialog.tsx}
Studio/src/**/  # the ~30 `toLocaleString()` date displays move to the shared formatter
Extensions/Etl/src/Core/EtlArtifactJson.cs                 # JsonHashInput; equality via JsonCanonicalForm.Equals
Essentials/tests/Architecture/Tests/JsonBaselineArchitectureTests.cs       # new
Essentials/tests/Architecture/Tests/Baselines/json-optionless-calls.txt    # new
docs/serialization.md
Essentials/apps/Elsa.Workbench/Constants/JsonSerializerOptionsCache.cs     # delete
```

**Structure Decision**: existing projects only. There are eight new files in `Elsa.Serialization.Core`, one new test
class and one ratchet file.

## Complexity Tracking

| Violation | Why needed | Simpler alternative rejected because |
|---|---|---|
| New cross-cutting pattern (§2.24.3) | One baseline is the point of ADR 0091 | Fixing the key policy only leaves re-escaping and independent option sets (ADR 0091, *Alternatives*) |
