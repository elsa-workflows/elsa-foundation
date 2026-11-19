# Implementation Plan: Expression capabilities (Elsa 4)

**Branch**: `claude/3052-s1-expression-capabilities` | **Date**: 2026-11-10, rechecked 2026-11-19
**Spec**: [spec.md](spec.md) | **Initiative**: [#3052](https://github.com/elsa-workflows/elsa-foundation/issues/3052) — spec **S-1** | **Issue**: [#3026](https://github.com/elsa-workflows/elsa-foundation/issues/3026)

**Status**: **Provisional** until the spec is approved. Every decision it applies is agreed (ADR 0045 §5 D-1/D-5/D-6, ADR 0083, ADR 0046 as amended, ADR 0089, ADR 0090, ADR 0092). Approval of the plan is approval of the semantic approach and contracts, not an implemented API.

**Inspected baseline**: `initiative/3052-elsa3-to-elsa4-conversion` at `b6a67a880` (2026-11-19), after specs 1221, 1222, 1230 and 1231 were implemented.

## Summary

Grant Elsa 4 JavaScript expressions the everyday capabilities workflow authors expect — the current time, randomness, GUIDs, date conversion between time zones, and JSON-exchanging .NET functions — and convert the matching Elsa 3 functions faithfully instead of refusing them. The capability was withheld to enforce replay-determinism; that doctrine is cancelled (D-1), and expressions are safe to be non-deterministic under the execution contract (D-2).

Four moving parts, all in existing projects:

1. **Profile** — rename `binding-pure-v1` to `binding-readonly-v1` everywhere (Expressions.Core, evaluators, converter, executable hash), with no alias; regenerate fixtures; drop the `Inline` replay comment.
2. **Engine** — re-admit `Date`, `Intl`, `Temporal` and `Math.random` in the isolated Jint engine and the portable evaluator, wire the clock to the host `TimeProvider` (UTC, invariant), and accept a source time zone as the one evaluator option a converted expression may carry.
3. **Functions** — extend spec 1222's host-function declaration with `Effectful` and an implementation, install declared functions in the engine, enforce the naming rule, warn on lossy number arguments, and register the built-ins (GUID family incl. `parseGuid`, Base64 pair). No `toJson`: scripts serialize with `JSON.stringify`, and the converter rewrites Elsa 3's `toJson` with a warning.
4. **Converter and publish** — flip the faithful Elsa 3 functions, classify `Date` usage by zone dependence, and add a .NET-usage analysis in Essentials that the converter (with Elsa 3 types) and publish validation both call.

[Research](research.md) records the findings and decisions. [Data model](data-model.md) defines the profile, the function catalog and the classification rules. [contracts/](contracts/) hold the function contract and the conversion/diagnostic contract. [Quickstart](quickstart.md) is the verification inventory.

## Technical Context

| Area | Existing technology and planned boundary |
|---|---|
| Profile | `ExpressionCapabilityProfiles.BindingPureV1` and `ExpressionEvaluationCapabilities` in `Essentials/src/Expressions/Core/Models/ExpressionDefinition.cs`; checked in the portable, Jint, Liquid and runtime evaluators; emitted by `Elsa3ExpressionRewriter` and `Elsa3ValueFlowLowerer`; serialized as `capabilityProfile`; hashed by `WorkflowExecutableHasher`. Planned: rename to `BindingReadonlyV1`, allow time and randomness, regenerate fixtures and golden hashes. |
| Expression engine | C#/.NET 10; **Jint 4.9.1** at `Essentials/src/Expressions/JavaScript/Jint`. `IsolatedJintEngine.DisableAmbientCapabilities` overwrites `Date`/`Temporal`/`Intl` and shadows `Math.random`; `JintPortableJavaScriptEvaluator` keeps `StrippedAmbientCapabilities` and rejects every evaluator option. Planned: remove the strip in both, set `TimeSystem` (over `TimeProvider`), `TimeZone`, `Culture=Invariant`; accept the source time zone option. |
| Functions | `JavaScriptHostFunctionDeclaration(Name, ReadsVariables)` in `Expressions.JavaScript.Core/Contracts` (spec 1222); `variables`/`getVariable` installed by `IsolatedJintEngine.SetReadOnlyVariables`. Planned: add `Effectful` and an implementation delegate; install declared functions; naming checks at startup; V9 warning. |
| Analysis | Essentials JavaScript analysis services (`JavaScriptVariableReadAnalyzer`, Acornima) and `ScriptOutcomeValidator` (`Scripts/*` errors). Planned: a .NET-usage analyzer there, called by publish validation (`Scripts/DotNetMemberUsage`, `Scripts/ReservedName` extended) and by the converter. |
| Converter | Acornima analysis in `Extensions/Elsa3/src/Mapping/Services/Elsa3ExpressionRewriter.cs` (`JavaScriptAnalyzer.VisitNode`, `RefusedHostFunctions`, `ElsaThreeGlobals`, `IsNondeterministicCall`), `Internal/Elsa3JavaScriptNames.cs`, `Elsa3ImportDiagnosticCodes` (`VF-IMP-001..011`). Planned: flip faithful functions, `Date` classification, call the shared analyzer with Elsa 3 types, add `VF-IMP-012`. |
| Clock | Host `System.TimeProvider` wrapped in a Jint `ITimeSystem`. Date semantics belong to ADR 0046. |
| Randomness | Native `Math.random`; Jint's RNG is not injectable (accepted). GUIDs via `Guid.NewGuid()`. |
| Serialization | JSON values only at the function boundary (ADR 0046 V10 default). One serializer for scripts: the engine's `JSON.stringify` (also used for script results). No `toJson`; no dependency on the JSON-integrity ADR. |
| Testing | xUnit only (**no FluentAssertions**). Unit tests in the existing JavaScript, Expressions, Workflows Design and Elsa3 mapping test projects; no new integration tier. |
| Target | Design-only, Runtime-only and combined compositions; both expression surfaces. No new project or package. |
| Compatibility | Faithful to Elsa 3 (Elsa.JavaScript 3.5.3) for every converted function. No compatibility for pre-release Elsa 4 formats: the profile rename and the evaluator-option change regenerate fixtures in place. |

## Constitution Check

Loaded framework **v6.1.0** and Elsa **v6.4.0**, both Gate indexes, and the [review checklist](../../docs/contributing/review-checklist.md). This is a design assessment; re-check these gates on the implementing PR. No constitutional exception or amendment is proposed.

| Gates | Plan assessment and required implementation evidence |
|---|---|
| Framework §2.1, §2.3, §2.17 | The profile rename touches `Expressions.Core` (a constant and a capabilities record, no new dependency). The analyzer lives in the existing Essentials JavaScript implementation project that already references Acornima; the Elsa 3 extension calls it through an Essentials contract and passes the Elsa 3 types. No new heavy package in a `.Core`, no new cross-domain reference. Run affected architecture/maps checks. |
| Framework §2.5, §2.23.1; Elsa §E2.2 | Features still compose and every registered service resolves (`FeatureCompositionTests`). Built-in function registrations and the analyzer are registered by the existing JavaScript feature as part of the default strategy; duplicate or reserved names fail at startup, tested. Runtime takes no Design dependency. |
| Framework §2.21–2.23; Elsa §E7 | Behaviour tests cover restored intrinsics, clock, date helpers, each built-in's Elsa 3 text, registration rules, the V9 warning, each converter flip, `Date` classification and each analyzer rule (refuse and allow cases). Unit tier only; `test-tiers.json` changes only if a project is added. |
| Framework §2.13, §2.16, §2.23.3; Elsa §E5 | No new package; computed versions. New public members (`Effectful`, the implementation delegate, `BindingReadonlyV1`) are public-surface deltas tracked with `ElsaTrackPublicApi` where the project opts in. |
| Framework §2.22, §2.25 | Update `Extensions/Elsa3/src/Mapping/README.md`, spec 247's R-6 rows this spec supersedes, the JS feature docs, and `EXTENSION_POINTS.md` for the host-function registration. |
| Elsa §E2.8 (draft), §E2.9 | Not touched. |

**Governance note.** If implementation finds that an applied ADR is wrong or incomplete (for example Jint's `Temporal` not resolving IANA zones on a host), the finding goes to that ADR as an amendment PR and the spec records it as an open question; the change does not quietly diverge.

## Project Structure

```text
specs/1223-expression-capabilities/
  spec.md, plan.md, research.md, data-model.md, quickstart.md
  contracts/host-functions.md
  contracts/conversion-and-diagnostics.md
  checklists/requirements.md
  tasks.md

Essentials/src/Expressions/Core/Models/ExpressionDefinition.cs     # profile constant + capabilities
Essentials/src/Expressions/JavaScript/Core/Contracts/              # JavaScriptHostFunctionDeclaration (+Effectful, implementation)
Essentials/src/Expressions/JavaScript/Jint/Services/
  IsolatedJintEngine.cs                # remove strip; ITimeSystem/TimeZone/Culture; install declared functions
  JintPortableJavaScriptEvaluator.cs   # remove StrippedAmbientCapabilities; accept the source time zone option
  (new) TimeProviderTimeSystem, built-in function implementations
Essentials/src/Expressions/JavaScript/Elsa.Expressions.JavaScript/Services/
  (new) JavaScript .NET-usage analyzer; reserved names extended
Essentials/src/Workflows/Design/Validations/.../Validators/        # publish errors Scripts/DotNetMemberUsage, Scripts/ReservedName
Essentials/src/Expressions/Liquid/Services/PortableLiquidExpressionHandler.cs   # profile rename only
Essentials/src/Activities/Primitives/Activities/Inline.cs          # drop the binding-pure-v1 replay comment

Extensions/Elsa3/src/Mapping/
  Services/Elsa3ExpressionRewriter.cs  # flips, Date classification, call the analyzer, VF-IMP-012, guidance texts
  Internal/Elsa3JavaScriptNames.cs     # (if needed)
  README.md                            # conversion table

Essentials/tests/… and Extensions/Elsa3/tests/…  # xUnit tests; regenerated fixtures and golden hashes
```

**Structure Decision**: Existing projects only. The analyzer moves to Essentials because publish needs it and the converter's walker is private to the Elsa 3 extension.

## Complexity Tracking

No constitutional violations to justify. The design avoids new complexity: no data-flow engine (direct use and plain aliases only), no function metadata beyond `ReadsVariables` (existing) and `Effectful` (D-6), no interop switch, no date host functions (native `Temporal`/`Intl`), and reuse of both diagnostic pipelines.
