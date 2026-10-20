# Implementation Plan: Expressions read the current time and unique values through recorded parameters

**Branch**: `agent/claude/3026-deterministic-expression-capability` | **Date**: 2026-10-20 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/260-expression-clock-and-unique-id/spec.md`; decisions in
[ADR 0088](../../docs/adr/0088-expressions-read-time-and-unique-values-through-recorded-parameters.md) D1 to D4.

## Summary

The work adds two parameter kinds, `Clock` and `UniqueId`, resolved where declared parameters are already resolved
(`RuntimePortableExpressionEvaluator`). Their source is an evaluation instant that the resolution context now carries:
the snapshot's `materializedAt` for activity inputs and each `While` pass, and an instant read once per intrinsic
evaluation for intrinsics. Values are pinned through the evaluated inputs, which the existing commits already make
durable.

It also adds a `binding-pure-v2` JavaScript profile. Its engine runs a clockless time system that is UTC-only and
parses ISO strings only, plus a frozen prelude that replaces the removed `Date` forms with functions that throw
guidance `TypeError`s. Plain-source JavaScript is stamped v2 at publish; stored executables keep their stamp. One rule
table drives the runtime messages, the authoring validator and the editor typings. Research: [research.md](research.md).

## Technical Context

**Language/Version**: C# on .NET 10 (`net10.0`)

**Primary Dependencies**: Jint 4.9.1 and Acornima 1.6.1 (already pinned); no new package

**Storage**: none new. Values are pinned inside existing `ActivityInputSnapshot` and intrinsic commits

**Testing**: xUnit only (no FluentAssertions), in the existing test projects listed below

**Target Platform**: Elsa 4 runtime hosts (Linux and Windows servers)

**Project Type**: libraries (Essentials)

**Performance Goals**: no measurable change. v1 engines are untouched; a v2 engine runs one small prelude per evaluation

**Constraints**: v1 behavior and stored fingerprints are unchanged (SC-005); messages are stable text (FR-017)

**Scale/Scope**: about 15 source files in 5 projects, plus tests and docs

## Constitution Check

*Loaded: `.specify/memory/constitution.md`, `.specify/memory/constitution-framework.md` and
`docs/contributing/review-checklist.md`. Re-checked after Phase 1: unchanged.*

| Gate | Status | Notes |
|---|---|---|
| §E2.2 Runtime takes no Design reference | PASS | Kinds live in `Elsa.Expressions.Core`; resolution in `Elsa.Workflows.Runtime(.Core)`; typings in `Workflows.Design.JavaScript`. No new references |
| §E2.6.1 executable-always-runs | PASS, risk noted | An older runtime cannot read the new discriminators or v2. Pre-GA (ADR 0080), recorded in ADR 0088 Consequences |
| §E2.6.2 artifact-only runtime | PASS | The instant comes from runtime state, not design data |
| §E2.7 Elsa 3 behind the import boundary | PASS | No Elsa 3 change; the converter is the D5 follow-up |
| §E5 versioning | PASS | Additive public types and constants; `PublicAPI.Unshipped.txt` updated where the projects track it |
| §E6 naming | PASS | `ClockExpressionParameterBinding` and `UniqueIdExpressionParameterBinding` follow `{Source}ExpressionParameterBinding` |
| §2.6.2 replacement contracts | PASS | None added |
| §2.17 duplication beats dependency | PASS | `ClocklessDateRules.cs` is a linked file, like `JavaScriptBindingSyntax.cs`, not a new reference |
| §2.24.2 sanctioned patterns | PASS | Polymorphic binding record; linked shared source; ADR pointer amendment |
| §2.22 / §2.22.1 docs and extension points | ACTION | Expressions, JavaScript, Liquid and Runtime `EXTENSION_POINTS.md`; Design JavaScript README; spec 095 contract; ADR 0045 pointer (done) |
| §2.23.1–§2.23.3 tests and visibility | PASS | A test per story, per removed form and per evaluation site; new helpers are internal |
| §2.23.5 exception boundaries | PASS | A missing instant is a stable `InvalidOperationException`; JS removals are `TypeError`s surfaced through the existing `JavaScriptBindingEvaluationException` |
| Decision record | DONE | ADR 0088 (proposed, pending owner approval) |

## Design decisions

| # | Decision | Detail |
|---|---|---|
| P-1 | Two parameter records | `ClockExpressionParameterBinding()` and `UniqueIdExpressionParameterBinding()`, discriminators `"Clock"` and `"UniqueId"`, no members (R-6) |
| P-2 | Instant on the context | `RuntimeInputBindingResolutionContext.EvaluatedAt` (`DateTimeOffset?`). `MaterializeSnapshotAsync` sets it to `materializedAt` and throws if a caller-supplied instant differs. The intrinsic executor reads `TimeProvider.GetUtcNow()` once per `MaterializeValueAsync` (R-2) |
| P-3 | Value formats | `Clock`: `EvaluatedAt.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", InvariantCulture)`. `UniqueId`: `Guid.CreateVersion7(EvaluatedAt).ToString("D")`. No policy, not sensitive |
| P-4 | Missing instant | `Clock` or `UniqueId` without `EvaluatedAt` throws `InvalidOperationException` with code `VF-EXP-CLOCK-001`, naming the parameter, node and input (FR-012) |
| P-5 | Profile constants | `ExpressionCapabilityProfiles.BindingPureV2` and `IsBindingPure(string)`; `ExpressionEvaluationCapabilities.Resolve` gives v2 the pure grant (R-4) |
| P-6 | Profile checks | The dispatcher, the runtime evaluator and the Jint evaluator accept v1 and v2; Liquid stays v1-only (FR-022) |
| P-7 | Publish stamping | `CompileExpressionInput` stamps v2 for `JavaScript`; the deserialization default stays v1 (FR-021) |
| P-8 | Clockless engine | `IsolatedJintEngine.Create(options, token, clocklessDate: false)`. When true: `TimeSystem = ClocklessTimeSystem`, invariant `Culture`, then the prelude from `ClocklessDateRules.Prelude`, then the rest of `DisableAmbientCapabilities` without `Date` (R-1) |
| P-9 | One rule table | `ClocklessDateRules` (internal, linked into Jint) holds the six forms, their messages, the invalid-date message and the prelude (R-5) |
| P-10 | Validation | `JavaScriptExpressionToolingProvider` walks the Acornima AST after a successful parse and adds diagnostics from `ClocklessDateRules` with locations: `toLocale*` as warnings, the rest as errors |
| P-11 | Typings | `BindingPureArgsDeclarationContributor` also declares a `Date` interface and constructor limited to the allowed members |
| P-12 | Stripped-capability diagnostic | Under v2 `Date` is not in the stripped list; prelude `TypeError`s surface with their own message |

## Project Structure

### Documentation (this feature)

```text
specs/260-expression-clock-and-unique-id/
├── spec.md
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   └── clock-uniqueid-and-binding-pure-v2.md
├── checklists/requirements.md
└── tasks.md            (speckit-tasks)
```

### Source Code (repository root)

```text
Essentials/src/Expressions/Core/
├── Models/ExpressionDefinition.cs                 # P-1 records + discriminators; P-5 BindingPureV2
└── Contracts/ExpressionEvaluationCapabilities.cs  # P-5
Essentials/src/Expressions/Elsa.Expressions/Services/PortableExpressionEvaluator.cs   # P-6
Essentials/src/Expressions/JavaScript/Elsa.Expressions.JavaScript/Services/
├── ClocklessDateRules.cs                          # P-9 (new)
└── JavaScriptExpressionToolingProvider.cs         # P-10
Essentials/src/Expressions/JavaScript/Jint/
├── Elsa.Expressions.JavaScript.Jint.csproj        # link ClocklessDateRules.cs
├── Services/ClocklessTimeSystem.cs                # P-8 (new)
├── Services/IsolatedJintEngine.cs                 # P-8
└── Services/JintPortableJavaScriptEvaluator.cs    # P-6, P-12
Essentials/src/Workflows/Runtime/Core/Models/RuntimeInputBindingResolutionContext.cs  # P-2
Essentials/src/Workflows/Runtime/Elsa.Workflows.Runtime/Services/Values/
├── RuntimePortableExpressionEvaluator.cs          # P-1 arms, P-3, P-4, P-6
├── RuntimeActivityInputMaterializer.cs            # P-2
└── WorkflowIntrinsicExecutor.cs                   # P-2
Essentials/src/Workflows/Publishing/Elsa.Workflows.Publishing/Services/RuntimeInputBindingCompiler.cs  # P-7
Essentials/src/Workflows/Design/JavaScript/Contributors/BindingPureArgsDeclarationContributor.cs     # P-11

Tests:
Essentials/tests/Expressions/Tests/                   # wire, fingerprint, capability resolution, validator
Essentials/tests/Expressions/JavaScript/Jint/Tests/   # v2 sandbox surface, every message, time zone/culture, v1 unchanged
Essentials/tests/Workflows/Runtime/Tests/             # resolver arms, missing instant, publish stamping
Essentials/tests/Activities/Runtime/Tests/            # run-level: retry, intrinsic Set, redelivery
Essentials/tests/Activities/ControlFlow/Tests/While/  # per-pass Clock/UniqueId
Essentials/tests/Workflows/Design/...                 # typings
```

**Structure Decision**: No new project. Every change sits beside the `WorkflowInstance` precedent (spec 247) or in the
JavaScript sandbox it extends.

## Complexity Tracking

None. The only non-obvious mechanism is the prelude that replaces `Date`. It is justified in R-1, because only it
gives a distinct message per removed form.
