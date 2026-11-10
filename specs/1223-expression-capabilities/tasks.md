# Tasks: Expression capabilities (Elsa 4) — S-1

**Feature**: [spec.md](spec.md) | [plan.md](plan.md) | [data-model.md](data-model.md) | [research.md](research.md) | contracts: [host-functions](contracts/host-functions.md), [conversion-and-diagnostics](contracts/conversion-and-diagnostics.md) | [quickstart.md](quickstart.md)

**Status**: Provisional (initiative #3052, S-1). Tasks implement the design on the agreed-as-if positions; D-6 (OQ-1) stays open. Test cadence: not greenfield, no TDD declared — tests are risk-based and may land with or after the implementation they cover (xUnit only; **no FluentAssertions**).

**Touched code**: `Essentials/src/Expressions/JavaScript/Jint/Services/IsolatedJintEngine.cs` (+ new host-function / `ITimeSystem` files); `Extensions/Elsa3/src/Mapping/Services/Elsa3ExpressionRewriter.cs`, `Internal/Elsa3JavaScriptNames.cs`, `Elsa3ImportDiagnosticCodes`, `README.md`; corresponding xUnit test projects.

---

## Phase 1: Setup

- [ ] T001 [P] Add a short-GUID parity reference fixture (a known GUID → its exact Elsa 3 short-GUID string, verified against Elsa 3 `ShortGuid`) under the Elsa3 mapping test project for use by T006/T010/T016.

## Phase 2: Foundational (blocking prerequisites)

- [ ] T002 Introduce the `binding-readonly-v1` profile constant in `Essentials/src/Expressions/JavaScript/Jint/Services/IsolatedJintEngine.cs`, superseding the `binding-pure-v1` doctrine reference (profile-constant rename deferred to S-1 by PR #3390).
- [ ] T003 Add `ProxiesEnabled` setting plumbing (A-1 default `false`) that the converter/publish detector registration in Phase 6 consumes; no proxy implementation (proxies stay disabled).

## Phase 3: User Story 1 — Current time, randomness, GUIDs in expressions (P1)

**Goal**: An author gets current time, randomness and new GUIDs in input bindings and scripts with no configuration.
**Independent test**: Evaluate a binding and a script that each read time, a random number and a new GUID; all yield well-formed values, no refusal (quickstart A, B6/B7/B9).

- [ ] T004 [US1] Remove the `DisableAmbientCapabilities` strip in `IsolatedJintEngine.cs` so `Date`, `Intl`, `Temporal` and native `Math.random` are admitted on the read-only surface.
- [ ] T005 [US1] Implement a custom Jint `ITimeSystem` over the host `System.TimeProvider`; set `Options.TimeSystem`, `Options.TimeZone = UTC`, `Options.Culture = Invariant` on engine construction.
- [ ] T006 [P] [US1] Register the GUID host functions (`newGuid`, `newGuidString`, `newShortGuid`, `getGuidString`, `getShortGuid`) returning JSON strings; `newShortGuid` reproduces the Elsa 3 short-GUID encoding (use the T001 fixture).
- [ ] T007 [P] [US1] Register the `now()` host function returning an ISO-8601 UTC string from the injected clock.
- [ ] T008 [P] [US1] Register the Base64 string pair (`stringToBase64`, `stringFromBase64`) and `toJson` (canonical encoder, ADR 0091) host functions — registered here as the earliest story that needs them; their contract is verified in US4.
- [ ] T009 [US1] xUnit behaviour tests: intrinsics restored (`Date`/`Intl`/`Temporal`/`Math.random` not `undefined`); clock reads a controlled `TimeProvider`; UTC + invariant formatting; `Math.random` permitted to differ (no fixed-sequence assertion) — quickstart A1–A4.
- [ ] T010 [P] [US1] xUnit behaviour tests: GUID functions return strings; `newShortGuid` matches the parity fixture; `now()` equals the controlled clock — quickstart B6, B7, B9.

## Phase 4: User Story 4 — Built-in helpers & .NET functions, consistent across surfaces (P2)

**Goal**: Host functions behave identically in bindings and scripts and exchange only JSON.
**Independent test**: Call each host function from both surfaces; confirm identical behaviour and JSON-only values (quickstart A5, B8, B10, B11).

- [ ] T011 [US4] xUnit tests: `stringFromBase64(stringToBase64(x)) === x` for UTF-8 `x`; `toJson(v)` equals the canonical encoder output for representative values; every host function returns a JSON value and accepts only JSON — quickstart B8, B10, B11.
- [ ] T012 [US4] xUnit tests: surface parity — each native capability and each host function available and identical in an input binding and in a `RunJavaScript` script — quickstart A5.

## Phase 5: User Story 2 — Elsa 3 time / GUID / string functions convert faithfully (P1)

**Goal**: Elsa 3 workflows using the faithful functions convert without refusal; genuinely unavailable ones are refused with guidance.
**Independent test**: Convert representative/corpus workflows; faithful ones succeed, refused ones produce a guided refusal (quickstart C, D).

- [ ] T013 [US2] In `Elsa3ExpressionRewriter.cs`, remove the `IsNondeterministicCall` refusal for `Math.random`/`Date.now` and the `new Date()`/`Date` globals refusal; admit them as faithful conversions.
- [ ] T014 [US2] Move the GUID family, `stringToBase64`/`stringFromBase64` and `toJson` out of `RefusedHostFunctions` into resolved host-function calls.
- [ ] T015 [US2] Verify and retain refusals for `Guid`/`DateTime` constructors and static access, `parseGuid`, byte/stream helpers, and `getConfig`/`getConfiguration`.
- [ ] T016 [P] [US2] xUnit conversion tests (faithful): `new Date()`, `Date.now()`, `Math.random()`, GUID family, Base64 pair, `toJson` convert without refusal and preserve shape — quickstart C12–C14.
- [ ] T017 [P] [US2] xUnit conversion tests (refused): `parseGuid`, bytes/streams, `getConfig`, `Guid`/`DateTime` refused with guidance — quickstart D16–D17.
- [ ] T018 [US2] SC-001 coverage check: 100% of available-corpus workflows whose JS uses only faithful functions convert without refusal — quickstart C15.
- [ ] T019 [P] [US2] Update the conversion table in `Extensions/Elsa3/src/Mapping/README.md` (refused → converted rows; add the `VF-IMP-010` row) — quickstart F26.
- [ ] T020 [P] [US2] Update spec 247 `research.md` R-6 notes where S-1 supersedes them (time/GUID/Base64/`toJson`) — quickstart F27.

## Phase 6: User Story 3 — Relied-on live .NET objects fail clearly (P1)

**Goal**: While proxies are disabled, proxy-dependent usage is refused with a clear quoted diagnostic at convert and publish, not a cryptic runtime failure.
**Independent test**: Convert/publish scripts with CLR-member usage and with legitimate JSON access; confirm the verdicts in quickstart E.

- [ ] T021 [US3] Add `VF-IMP-010` (proxy-dependent usage) to `Elsa3ImportDiagnosticCodes`, distinct from `VF-IMP-009 LiveInstanceState`.
- [ ] T022 [US3] Add the CLR-type deny catalog and the JS-global allow set (data-model §3) as data in the converter; include a small "returned a CLR object in Elsa 3" note-map for message enrichment.
- [ ] T023 [US3] Extend `JavaScriptAnalyzer.VisitNode`: refuse PascalCase method invocations (and curated CLR methods) with `VF-IMP-010`, message quoting the offending expression (reconstructed from node `.Start`/`.End`).
- [ ] T024 [US3] Extend the walker: refuse CLR-type identifier static access and `new T(...)` with `VF-IMP-010`; allow JS-global identifiers; allow property reads of any casing; do not classify computed/dynamic member invocation.
- [ ] T025 [US3] Register the detector only while `!ProxiesEnabled` (T003), and run the shared analysis pass at **publish** as well as convert.
- [ ] T026 [P] [US3] Feature-registration test (logic-bearing wiring, §2.23.1): detector active when `ProxiesEnabled=false`, not registered when `true`.
- [ ] T027 [P] [US3] Detector tests: PascalCase invocation refused with quoted snippet; CLR-type reference refused — quickstart E18, E19.
- [ ] T028 [P] [US3] Detector tests: PascalCase property read allowed; native JS invocations and `new Date()` allowed; computed dispatch allowed — quickstart E20–E22.
- [ ] T029 [P] [US3] Detector tests: runs at publish; inactive when proxies enabled; SC-002 — no proxy/CLR-dependent script reaches run time undetected — quickstart E23–E25.

## Phase 7: Polish & cross-cutting

- [ ] T030 [P] Update owning JS-expression feature docs to note the `binding-readonly-v1` capabilities — quickstart F28.
- [ ] T031 Confirm new tests are in the unit tier (not Testcontainers/child-host); update `tools/ci/test-tiers.json` only if a new test project was introduced — Elsa §E7.
- [ ] T032 Public-surface review: track the profile constant and any new public registration API via `ElsaTrackPublicApi` where the project opts in — framework §2.23.3.
- [ ] T033 Regenerate `dependency-map.json` / `.slnf` if project references changed (expected: none); verify with `git diff --stat`.

## Phase 8: Governance surfaces (coordinator — non-code, external posts need Joey's OK)

- [ ] T034 Post the D-6 no-metadata observation + future-attribute-path note on ADR 0045 §5's PR (#3384) — quickstart G29.
- [ ] T035 Post the two findings (native intrinsics admitted; Jint 4.9.1 RNG not injectable) on PR #3390 — quickstart G30.
- [ ] T036 Open the draft PR against `initiative/3052-elsa3-to-elsa4-conversion` (body "Part of #3052", approval plan Sipke + Frans, request both) and add the pointer comment on #3052 — quickstart G31.

---

## Dependencies & execution order

- **Setup (T001)** → **Foundational (T002–T003)** block everything.
- **US1 (T004–T010)** depends on Foundational; registers all engine capabilities (intrinsics, clock, random, GUID, `now`, and — earliest-needed — Base64/`toJson`).
- **US4 (T011–T012)** depends on US1 (verifies the host-function contract + surface parity).
- **US2 (T013–T020)** depends on US1 (the converter resolves to the functions US1 registered).
- **US3 (T021–T029)** depends on Foundational (`ProxiesEnabled`) and shares the converter file with US2 — run US2 then US3 to avoid edit collisions in `Elsa3ExpressionRewriter.cs`.
- **Polish (T030–T033)** after the stories. **Governance (T034–T036)** after the code is pushed; T034/T035 are external posts requiring the coordinator's approval.

## Parallel opportunities

- T006, T007, T008 (distinct host-function registrations) can be written in parallel after T004/T005.
- T016, T017, T019, T020 (distinct test/doc files) in parallel within US2.
- T027, T028, T029 (distinct detector test cases) in parallel within US3; T026 alongside.
- US2 and US3 both edit `Elsa3ExpressionRewriter.cs`, so their **implementation** tasks (T013–T015 vs T023–T025) are **not** parallel with each other.

## Implementation strategy

- **MVP = US1** (engine capabilities): restores the core author-facing capability and is independently demonstrable.
- Then **US2** (faithful conversion) unblocks the Elsa 3 migration — the initiative's purpose.
- Then **US3** (proxy detector) protects migrations from silent breakage.
- **US4** is verification-heavy (JSON contract + parity) and can land with US1 or just after.
- **Governance (Phase 8)** closes the loop: provisional surfaces on the dependency ADRs + the draft PR and #3052 pointer.
