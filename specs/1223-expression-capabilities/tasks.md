# Tasks: Expression capabilities (Elsa 4) — S-1

**Feature**: [spec.md](spec.md) | [plan.md](plan.md) | [data-model.md](data-model.md) | [research.md](research.md) | contracts: [host-functions](contracts/host-functions.md), [conversion-and-diagnostics](contracts/conversion-and-diagnostics.md) | [quickstart.md](quickstart.md)

**Status**: Approved with the spec (2026-11-19; initiative #3052, S-1). Regenerated 2026-11-19 against `b6a67a880`. Test cadence: not greenfield, no TDD declared — tests are risk-based and may land with or after the implementation they cover (xUnit only; **no FluentAssertions**).

**Touched code**: `Essentials/src/Expressions/Core/Models/ExpressionDefinition.cs`; `Essentials/src/Expressions/JavaScript/Core/Contracts/JavaScriptHostFunctionDeclaration.cs`; `Essentials/src/Expressions/JavaScript/Jint/Services/IsolatedJintEngine.cs`, `JintPortableJavaScriptEvaluator.cs` (+ new time-system and built-in files); `Essentials/src/Expressions/JavaScript/Elsa.Expressions.JavaScript/Services/` (new analyzer, reserved names); the Design validators (`ScriptOutcomeValidator` or a sibling); `Essentials/src/Expressions/Liquid/Services/PortableLiquidExpressionHandler.cs`; `Essentials/src/Activities/Primitives/Activities/Inline.cs`; `Extensions/Elsa3/src/Mapping/Services/Elsa3ExpressionRewriter.cs`, `Services/Elsa3ValueFlowLowerer.cs`, `Internal/Elsa3JavaScriptNames.cs`, `README.md`; corresponding xUnit test projects and fixtures.

---

## Phase 1: Setup

- [ ] T001 [P] Add Elsa 3 parity fixtures under the Elsa3 mapping test project: for fixed inputs, the exact Elsa.JavaScript 3.5.3 output of the short-GUID algorithm, `Guid.Parse(...).ToString()` for each accepted form, and `stringToBase64`. Used by T009, T011 and T019.

## Phase 2: Foundational (blocking prerequisites)

- [ ] T002 Rename `ExpressionCapabilityProfiles.BindingPureV1` to `BindingReadonlyV1` (value `binding-readonly-v1`) in `Essentials/src/Expressions/Core/Models/ExpressionDefinition.cs` and update `ExpressionEvaluationCapabilities` so the profile allows time and randomness; update every user (portable, Jint, Liquid and runtime evaluators; `Elsa3ExpressionRewriter`; `Elsa3ValueFlowLowerer`; `BindingPureArgsDeclarationContributor`). No alias or reader for the old value (FR-031).
- [ ] T003 Regenerate stored fixtures and golden executable hashes that carry `capabilityProfile` in place; verify with the affected test projects (FR-031).
- [ ] T004 Remove the `Inline` comment that grounds replay safety in `binding-pure-v1` (`Essentials/src/Activities/Primitives/Activities/Inline.cs`); reference the D-2 contract instead (FR-031).
- [ ] T005 Extend `JavaScriptHostFunctionDeclaration` with an implementation delegate (no effect metadata, FR-012) taking and returning JSON values; keep `ReadsVariables` (FR-012, FR-029).

## Phase 3: User Story 1 — Current time, randomness, GUIDs in expressions (P1)

**Goal**: An author gets current time, randomness, new GUIDs and zone conversion in input bindings and scripts with no configuration.
**Independent test**: quickstart A1–A5a, B6, B7.

- [ ] T006 [US1] Remove `DisableAmbientCapabilities` in `IsolatedJintEngine.cs` and `StrippedAmbientCapabilities` plus the "deterministic binding sandbox" rethrow in `JintPortableJavaScriptEvaluator.cs`, so `Date`, `Intl`, `Temporal` and native `Math.random` are admitted (FR-001, FR-004, FR-005).
- [ ] T007 [US1] Implement a Jint `ITimeSystem` over the host `System.TimeProvider`; set `Options.TimeSystem`, `Options.TimeZone = UTC`, `Options.Culture = Invariant` on engine construction (FR-002).
- [ ] T008 [US1] Install every registered `JavaScriptHostFunctionDeclaration` in `IsolatedJintEngine` as a non-writable global, as `SetReadOnlyVariables` installs `getVariable`; marshal arguments and results as JSON values (FR-011, FR-029).
- [ ] T009 [P] [US1] Register the GUID built-ins `newGuid`, `newGuidString`, `getGuidString`, `newShortGuid`, `getShortGuid` and `parseGuid` with `ReadsVariables = false` (FR-006, FR-026; T001 fixtures).
- [ ] T010 [P] [US1] xUnit: intrinsics restored; clock reads a controlled `TimeProvider` (`Date`, `Temporal.Now`); UTC and invariant formatting; `Math.random` permitted to differ — quickstart A1–A4, SC-003.
- [ ] T011 [P] [US1] xUnit: GUID built-ins return strings in Elsa 3's forms; `parseGuid` accepted forms and invalid text — quickstart B6, B7.
- [ ] T012 [P] [US1] xUnit: `Temporal` and `Intl.DateTimeFormat` convert between UTC and `Europe/Amsterdam` in winter and summer; record the result on the host OS used by CI (FR-025) — quickstart A5a.

## Phase 4: User Story 4 — Built-in helpers & .NET functions, consistent across surfaces (P2)

**Goal**: Functions behave identically on both surfaces, exchange only JSON, follow the naming rule and warn on precision loss.
**Independent test**: quickstart A5, B8–B11b.

- [ ] T013 [US4] Register `stringToBase64`, `stringFromBase64` (invalid Base64 fails, invalid UTF-8 → U+FFFD) as built-ins (FR-007); no `toJson` global (FR-009); map other script arguments as a script result is mapped (FR-011).
- [ ] T014 [US4] Enforce the naming rule at startup: reserved names (`variables`, `input`, `activities`, `workflow`, `args`, the built-ins, namespace roots), namespace prefix for host functions, duplicate detection; failure names the function. Install a namespace as a read-only global object holding its functions (FR-027).
- [ ] T015 [US4] Extend the publish check (`JavaScriptReservedNames`): declaring `variables` → the existing error `Scripts/ReservedName`; `args` unchanged (spec 1222); `complete` unchanged (spec 1208); declaring `input`, `activities`, `workflow`, a built-in name or a namespace root → the new warning `Scripts/ShadowedFunction` (FR-027).
- [ ] T016 [US4] Warn on a number argument that carries spec 1228's loss information, naming the function and the argument path; never refuse, never stringify (FR-028).
- [ ] T017 [P] [US4] xUnit: Base64 round trip and Elsa 3 parity; no `toJson` global; no live .NET object in or out — quickstart B8–B10, SC-005, SC-008.
- [ ] T018 [P] [US4] xUnit: naming failures, reserved-name warning, no fallback read marker for built-ins, no effectful option on the registration, V9 warning — quickstart B11–B11b.
- [ ] T019 [P] [US4] xUnit: surface parity — each intrinsic and built-in identical in an input binding and a `RunJavaScript` script (FR-003) — quickstart A5, SC-004.

## Phase 5: User Story 2 — Elsa 3 time / GUID / string functions convert faithfully (P1)

**Goal**: Elsa 3 workflows using the faithful functions convert without refusal; genuinely unavailable ones are refused with guidance.
**Independent test**: quickstart C, D.

- [ ] T020 [US2] In `Elsa3ExpressionRewriter.cs`, remove `Math.random`/`Date.now` from `IsNondeterministicCall` (keep `crypto.randomUUID`), take `Date` out of the refused globals, move the GUID family, `parseGuid` and the Base64 pair out of `RefusedHostFunctions`, rewrite `toJson(x)` to `JSON.stringify(x)` with a quoted behaviour warning naming the differences (FR-009), and reword `TimeGuidance`/`UniqueValueGuidance` (FR-014, FR-015).
- [ ] T021 [US2] Classify `Date` usage (contract D1a): zone-independent converts; zone-dependent usage and non-literal parses follow the date interpretation — refused under `Refuse` with guidance naming `SourceTimeZone` and `AssumeUtc`, converted with a behaviour warning under `AssumeUtc`, converted under `SourceTimeZone`; generic method names count only on a known `Date` receiver (FR-021, FR-022, FR-024).
- [ ] T042 [US2] Rewrite each direct read of a `DateTime`/`DateTimeOffset`-typed Elsa 3 variable, input or activity output to `v == null ? v : new Date(v)` and mark it a known `Date` (FR-032).
- [ ] T043 [US2] Refuse locale-dependent calls without an explicit locale (`toLocale*String`, `localeCompare`, `Intl` constructors) with a quoted diagnostic (FR-033).
- [ ] T044 [US2] Add the date interpretation setting to the Elsa 3 converter feature (`Refuse` default, `AssumeUtc`, `SourceTimeZone` + IANA ID), record it on each converted workflow and in its conversion contract; S-9 applies it at the JSON to .NET boundary (FR-034).
- [ ] T022 [US2] Under `SourceTimeZone`, carry the zone into the converted expression as an evaluator option; accept that option in `JintPortableJavaScriptEvaluator` (it rejects all options today) and apply it as Jint `Options.TimeZone`; the option enters the executable hash (FR-023).
- [ ] T023 [P] [US2] xUnit for D1a: `Refuse` refusal and its guidance text; `AssumeUtc` conversion with warning; `SourceTimeZone` = `Europe/Amsterdam` winter and summer equal Elsa 3; zone-independent usage under every value; non-literal parse per value; date-only literal; date-typed value rewrite; locale refusal — quickstart C12–C12f, SC-007.
- [ ] T024 [P] [US2] xUnit conversion tests (faithful): `new Date()`, `Date.now()`, `Math.random()`, GUID family, `parseGuid`, Base64 pair and GUIDs used as text convert; `toJson` is rewritten with the warning; `isNullOrEmpty`/`isNullOrWhiteSpace` still inline (FR-010) — quickstart C13, C14, SC-008.
- [ ] T025 [P] [US2] xUnit conversion tests (refused): bytes/streams, `getConfig`/`getConfiguration`, `crypto.randomUUID`; `Guid`/`DateTime`/`Random` keep `VF-IMP-005` with the new guidance (FR-013, FR-015) — quickstart D16, D17.
- [ ] T026 [US2] SC-001 coverage check against the available corpus — quickstart C15.
- [ ] T027 [P] [US2] Update the conversion table in `Extensions/Elsa3/src/Mapping/README.md` (refused → converted rows; add `VF-IMP-012`) — quickstart F26.
- [ ] T028 [P] [US2] Record in spec 247 (`spec.md` and `research.md` R-6) that spec 1223 supersedes its refusals; update the R-6 rows that S-1 supersedes (GUID family, `parseGuid`, Base64; `toJson` rewritten to `JSON.stringify`) — quickstart F27.

## Phase 6: User Story 3 — Relied-on live .NET objects fail clearly (P1)

**Goal**: Member use on values that were .NET objects in Elsa 3, and .NET type references, are refused with a quoted diagnostic at conversion and publish.
**Independent test**: quickstart E.

- [ ] T029 [US3] Add the .NET-usage analyzer in `Essentials/src/Expressions/JavaScript/Elsa.Expressions.JavaScript/Services/` behind a contract the Elsa 3 extension can call: .NET-type rule, PascalCase-invocation rule with the JS-global receiver, same-script-member, script-declared-name and registered-function/namespace exemptions, the list/dictionary native-member allowance, and the known-.NET-object rule fed by caller-supplied value sources; quotes the offending expression from node offsets (FR-016 to FR-019).
- [ ] T030 [US3] Add the .NET type catalog and the JS-global allow set (data-model §3.2) as data in the analyzer; add the wrapped-.NET-type catalog beside the Elsa 3 type map in the converter (data-model §3.1).
- [ ] T031 [US3] In the converter, call the analyzer with the known .NET-object sources (`newGuid()`/`parseGuid()` results; variables, inputs and outputs whose Elsa 3 type is in the wrapped catalog) and their `const`/`let` aliases; emit `VF-IMP-012` (new constant in `Elsa3ImportDiagnosticCodes`); leave `ElsaThreeGlobals` on `VF-IMP-005` (FR-016, FR-017).
- [ ] T032 [US3] Call the analyzer from publish validation and emit `Scripts/DotNetMemberUsage` (FR-017, FR-018).
- [ ] T033 [US3] Register the analyzer as part of the default JavaScript strategy in the existing feature; no setting (FR-030).
- [ ] T034 [P] [US3] Feature-registration test (§2.23.1): the analyzer and the built-ins resolve in the default composition.
- [ ] T035 [P] [US3] Analyzer tests (refuse): GUID member calls, `toByteArray()` through an alias, a `FormFile` variable's members, `toJson` of a non-GUID .NET value, .NET type references, PascalCase invocations; each message quotes the offending expression (SC-006) — quickstart E18–E20.
- [ ] T036 [P] [US3] Analyzer tests (allow): PascalCase property reads incl. `variables.Order.Status`, native calls incl. `Intl.DateTimeFormat('en-GB')`, computed dispatch, same-script members, GUIDs as text — quickstart E21–E22a.
- [ ] T037 [P] [US3] SC-002 check over the corpus and representative cases; one analyzer used at conversion and publish — quickstart E23, E24.

## Phase 7: Polish & cross-cutting

- [ ] T038 [P] Update the JS feature docs (capabilities, built-ins, registration rules) and `EXTENSION_POINTS.md` (host-function registration) — quickstart F28.
- [ ] T039 Confirm new tests are in the unit tier; update `tools/ci/test-tiers.json` only if a test project is added — Elsa §E7.
- [ ] T040 Public-surface review: `BindingReadonlyV1`, the implementation delegate, the analyzer contract — framework §2.23.3.
- [ ] T041 Regenerate solution filters only if project references changed (expected: none); verify with `git diff --stat`.

---

## Dependencies & execution order

- **Setup (T001)** and **Foundational (T002–T005)** block everything; T003 follows T002.
- **US1 (T006–T012)** depends on Foundational; T008 depends on T005.
- **US4 (T013–T019)** depends on T008.
- **US2 (T020–T028, T042–T044)** depends on US1 and T013 (the converter resolves to registered built-ins).
- **US3 (T029–T037)** depends on Foundational; its converter task T031 shares `Elsa3ExpressionRewriter.cs` with US2 — run T020–T022 before T031.
- **Polish (T038–T041)** after the stories.

## Parallel opportunities

- T010, T011, T012 after T006–T009.
- T017, T018, T019 within US4.
- T023–T025, T027, T028 within US2 (distinct files).
- T029/T030 (Essentials) can run alongside US2's converter work; T031 waits for T020–T022.
- T034–T037 within US3.

## Implementation strategy

- **MVP = Foundational + US1**: the renamed profile, restored intrinsics, clock and GUID built-ins.
- Then **US4** (registration rules, remaining built-ins) and **US2** (faithful conversion), which unblocks the Elsa 3 migration.
- Then **US3** (.NET-usage analysis) protects migrations from silent breakage.
