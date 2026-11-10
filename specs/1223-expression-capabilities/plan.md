# Implementation Plan: Expression capabilities (Elsa 4)

**Branch**: `claude/3052-s1-expression-capabilities` | **Date**: 2026-11-10
**Spec**: [spec.md](spec.md) | **Initiative**: [#3052](https://github.com/elsa-workflows/elsa-foundation/issues/3052) — spec **S-1** | **Issue**: [#3026](https://github.com/elsa-workflows/elsa-foundation/issues/3026)

**Status**: **Provisional.** This plan is written as if S-1's dependency ADRs are agreed. Approval of the plan takes effect on PR merge into the initiative branch; it is approval of the semantic approach and contracts, not an implemented API or a passed production journey. The one unsettled dependency that shapes the design (D-6) is carried as an open question in the spec (OQ-1) and surfaced on ADR 0045 §5's PR (#3384).

**Inspected baseline**: `initiative/3052-elsa3-to-elsa4-conversion` synced to main `f4760aa0c` (2026-11-10). Converter infrastructure inspected at the current worktree head.

## Summary

Grant Elsa 4 JavaScript expressions the everyday capabilities workflow authors expect — the current time, randomness, GUIDs, a few built-in helpers, and JSON-exchanging .NET functions — and convert the matching Elsa 3 functions faithfully instead of refusing them. The capability was previously withheld to enforce replay-determinism; that doctrine is cancelled (D-1), and expressions are safe to be non-deterministic under the execution contract (D-2: a recorded step never re-runs).

Three moving parts, all in existing code:

1. **Engine profile** — restore the native JavaScript intrinsics (`Date`, `Intl`, `Temporal`, `Math` incl. `Math.random`) that the isolated Jint engine currently strips, wire the native clock to the host time source (UTC, invariant), and give the read-only surface the name `binding-readonly-v1`. One engine serves both input bindings and `RunJavaScript` scripts.
2. **Host functions** — register the JSON-exchanging .NET functions (`newGuid` family, `stringToBase64`/`stringFromBase64`, `now`, `toJson`) on that surface. Everything in and out is a JSON value.
3. **Converter** — in the Elsa 3 → Elsa 4 rewriter, flip the faithful functions from "refused" to "converted", keep the genuinely unavailable ones refused, and add a **proxy-dependent-usage detector** that refuses, with a clear quoted diagnostic, any script that relied on a live .NET object (a proxy) while proxies are disabled — active only while proxies are off (A-1), and reused at publish as well as convert.

[Research](research.md) records the engine/Jint and converter findings and the decisions behind each choice. [Data model](data-model.md) defines the capability surface, the host-function catalog, and the detector's classification rules. [contracts/](contracts/) hold the host-function contract and the converter/diagnostic contract. [Quickstart](quickstart.md) is the verification inventory.

## Technical Context

| Area | Existing technology and planned boundary |
|---|---|
| Expression engine | C#/.NET 10; **Jint 4.9.1** isolated engine at `Essentials/src/Expressions/JavaScript/Jint`. Today `IsolatedJintEngine.DisableAmbientCapabilities` overwrites `Date`/`Temporal`/`Intl` with `undefined` and shadows `Math.random`; engine options set no clock/time-zone/culture. Planned: re-admit those intrinsics, set `TimeSystem` (over the host `TimeProvider`), `TimeZone=UTC`, `Culture=Invariant`; register JSON host functions; introduce the `binding-readonly-v1` profile constant in code. |
| Converter | **Acornima 1.6.1** static AST analysis at `Extensions/Elsa3/src/Mapping/Services/Elsa3ExpressionRewriter.cs` (`JavaScriptAnalyzer.VisitNode`), driven by `Elsa3ValueFlowLowerer`. Name authority: `Internal/Elsa3JavaScriptNames.cs`. Diagnostics: `Elsa3ExpressionRewriteIssue(Code, Message, Guidance)` + `Elsa3ImportDiagnosticCodes` (`VF-IMP-001..009`). Planned: flip faithful functions, add proxy detector, add `VF-IMP-010`. |
| Clock | Host `System.TimeProvider`, wrapped in a custom Jint `ITimeSystem` so native `Date`/`Date.now()` read it. UTC, invariant culture. Date/time *semantics* defer to ADR 0046 (#3422); this spec wires the source, not new date math. |
| Randomness | Native `Math.random` restored. Jint 4.9.1's `MathInstance` news up `new Random()` with no injection hook, so the RNG is uncontrolled — an accepted limitation (tests must not depend on a fixed sequence). GUID randomness is cryptographic via `Guid.NewGuid()`. |
| Serialization | Every boundary value is a JSON value; `toJson` builds on the shared canonical read-only encoder (ADR 0091 / A-5). No live .NET objects cross the boundary (A-1 proxies off). |
| Testing | xUnit only (**no FluentAssertions** — constitutionally pinned). Unit tests for engine capabilities, host-function JSON round-trips, converter flips (faithful + still-refused), and the proxy detector's positive/negative/boundary cases. New tests land in the existing JS-engine and Elsa3-mapping test projects; no new integration tier expected. |
| Target | Existing Design-only, Runtime-only and combined compositions; both expression surfaces. No new project, no new provider, no persistence change. |
| Compatibility | `newShortGuid` must reproduce Elsa 3's exact short-GUID encoding; faithful conversions must produce output equivalent to Elsa 3. Public-surface deltas (profile constant, any new public registration/API) tracked per §2.23.3. |

## Constitution Check

Loaded framework **v6.1.0** and Elsa **v6.4.0**, both Gate indexes, and the [review checklist](../../docs/contributing/review-checklist.md). This is a design assessment, not a claim that architecture tests ran; re-check these gates on the implementing PR(s). No constitutional exception or amendment is proposed.

| Gates | Plan assessment and required implementation evidence |
|---|---|
| Framework §2.1, §2.3, §2.17 | All changes live in implementation projects (`…JavaScript.Jint`, `…Elsa3.Mapping`), not in any `.Core`; no new heavy package in a `.Core`; no new cross-domain reference. The new catalogs (CLR-type names, host-function list) are small data in the owning implementation, preferring duplication over a new dependency. Run affected architecture/maps checks. |
| Framework §2.5, §2.23.1; Elsa §E2.2 | The JS feature(s) must still compose and every service they register must resolve (`FeatureCompositionTests`). The `binding-readonly-v1` profile and the `!ProxiesEnabled` gate are wired through existing feature registration; missing/duplicate registration fails composition rather than being skipped. Runtime must not take a Design dependency. |
| Framework §2.21–2.23; Elsa §E7 | Behaviour tests cover: restored intrinsics, clock source, each host function's JSON contract, each converter flip, and the detector (invocation refused, type-ref refused, PascalCase property read allowed, dynamic dispatch allowed, detector inactive when proxies enabled). Unit tests are **not** integration tier; keep them out of Testcontainers/child-host tiers so `test-tiers.json` stays correct (`test-tiers-check`). Add any new test project to `tools/ci/test-tiers.json` in exactly one tier. |
| Framework §2.13, §2.16, §2.23.3; Elsa §E5 | No new package; versions stay computed (no hand-edited `<Version>`). Internal by default; the profile constant and any new public registration API are reviewed as public-surface deltas and tracked via `ElsaTrackPublicApi` where the project opts in. Preserve existing constructor signatures. |
| Framework §2.22, §2.25 | Update the owning feature docs and the Elsa3 mapping conversion table (`Extensions/Elsa3/src/Mapping/README.md`) and the research R-6 notes in spec 247 where this spec supersedes them (time/GUID/Base64/`toJson` move from refused to converted). No unrelated artifact retirement. |
| Elsa §E2.8 (draft), §E2.9 | Not touched — no activity-versioning or triplet change. |

**Provisional-governance note.** Accepted **ADR 0045** governs the execution contract; this spec implements its §5 expression-capability text (PR #3384, provisional). ADR 0046 (dates), ADR 0083 (`binding-readonly-v1`, PR #3390), ADR 0091 (encoder) and the A-1 proxy decision are depended on as stated in the spec; if implementation turns up a finding that contradicts one, it is flowed back to that ADR's PR as a comment and recorded as an open question — the scoped change stops for its governing process rather than quietly diverging. **D-6 is unsettled**; the plan builds the no-metadata position (no function profiles, no classification gating a call) and does not invent an attribute mechanism.

## Project Structure

```text
specs/1223-expression-capabilities/
  spec.md, plan.md, research.md, data-model.md, quickstart.md
  contracts/host-functions.md          # the JSON-exchanging .NET function catalog + contract
  contracts/conversion-and-diagnostics.md  # converter flips + proxy detector + VF-IMP-010
  checklists/requirements.md
  tasks.md                             # /speckit.tasks output (after plan review)

Essentials/src/Expressions/JavaScript/Jint/
  Services/IsolatedJintEngine.cs       # re-admit intrinsics; wire ITimeSystem/UTC/invariant;
                                       #   binding-readonly-v1 profile; register host functions
  (new) host-function registrations + a custom ITimeSystem over TimeProvider

Extensions/Elsa3/src/Mapping/
  Services/Elsa3ExpressionRewriter.cs  # flip faithful funcs; add proxy detector in JavaScriptAnalyzer
  Internal/Elsa3JavaScriptNames.cs     # (if needed) data the detector/flip consult
  Conversion/ or Services/             # VF-IMP-010 in Elsa3ImportDiagnosticCodes
  README.md                            # conversion table updated (refused -> converted rows)

Essentials/tests/… and Extensions/Elsa3/tests/…
  xUnit tests for engine capabilities, host functions, converter flips, proxy detector
tools/ci/test-tiers.json               # only if a new test project is introduced
```

**Structure Decision**: Single-repo, implementation-project-local changes. No new project or package; the two touched implementation projects already exist and own these concerns. The detector extends the existing `JavaScriptAnalyzer` walker rather than introducing a new analysis component.

## Complexity Tracking

No constitutional violations to justify. The design deliberately avoids new complexity: no taint/dataflow engine (a syntactic heuristic over the existing walker), no function-metadata system (D-6 no-metadata position), no new project, and reuse of the existing diagnostic pipeline (one new code value).
