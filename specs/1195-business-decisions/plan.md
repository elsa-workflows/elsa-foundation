# Implementation Plan: Business decisions

**Branch**: `agent/codex/3602-business-decisions` | **Date**: 2026-11-02 | **Spec**: [spec.md](spec.md)
**Issue**: [#3602](https://github.com/elsa-workflows/elsa-foundation/issues/3602)
**State**: Approved 5 November 2026 by Sipke; implementation in progress. [Owner receipt](https://github.com/elsa-workflows/elsa-foundation/issues/3602#issuecomment-6670626201).

## Summary

Introduce optional `Elsa.Decisions`, whose purpose is **“Evaluates versioned business decisions and explains their results.”** Preserve `Elsa.Rules` and ADR 0088's finite-inference contracts. A pure bounded table evaluator runs inside its caller; it needs no separate worker, scheduler or durable-round engine.

Use an Elsa-owned compiler/evaluator for [the named DMN 1.3 subset](contracts/dmn-profile.md). Two pinned .NET package probes found reasons not to adopt either unchanged. Use one module-owned accessible table editor and preserve imported XML through targeted edits. Pinned dmn-js serves external-editor qualification, rather than a second production editor. The narrow editor avoids offering unsupported executable constructs; actual accessibility and preservation still need browser proof. Build on the delivered shared execution model from #2898/#3410 and #2907/#3603; [the integration contract](contracts/execution-integration.md) defines concrete reuse and avoids a parallel Decisions runtime.

## Technical Context

**Language/Version**: C#/.NET 10; TypeScript/React and existing Studio SDK.
**Dependencies**: Core uses BCL JSON values only. Default evaluator uses BCL XML and exact numeric comparison, with no CLR expression execution. Design/API use existing modularity/events/permissions/capabilities. EF uses shared persistence policy. dmn-js 18.0.1 is a qualification dependency, under its bpmn.io license.
**Storage**: Decisions-owned records with trusted tenant scope, revision CAS and immutable publication/test snapshots. Preserve existing databases; qualify on owned temporary hosts/stores.
**Testing**: Whole affected behavior suites; actual package consumer; two-tenant HTTP; normal Studio/native/BPMN; external-editor round trips and differential profile cases.
**Target**: Ordinary .NET applications and optional combined, Design-only and runtime-only hosts.
**Performance Goals**: None. Size/operation limits are correctness boundaries, not benchmarks.
**Constraints**: No runtime Design-store read, external effects/functions, sensitive-value leakage, full-DMN/FEEL or Camunda compatibility claim.
**Scope**: One independent table per executable document, scalar typed profile and five hit policies; complete asset/case lifecycle and three caller shapes.

## Constitution Check

Loaded framework v6.1.0, Elsa v6.3.0, both Gate indexes and [review checklist](../../docs/contributing/review-checklist.md). No provisional section is needed or ratified.

| Gate | Disposition |
| --- | --- |
| Elsa §E2.1 / Governance | **Approved**: ADR0095 admits the domain/purpose, with Elsa minor bump and sync-impact report under the explicit 5 November owner decision. |
| Framework §2.1 / §2.4 | Core contains contracts/immutable values, without XML/evaluator policy, EF, HTTP, workflow or Studio dependencies. |
| §2.6 / §2.6.4 / §2.7 | Named publication/permission/capability contributions; separate design selection and runtime evaluation; no hidden dispatch coupling. |
| §2.9 / §2.10 / §2.20 | Provider-independent invariants, separate commands/queries, CAS and immutability. Exact EF closure admission only. |
| §2.14 / §2.16.1 | Every package below has a concrete consumer/dependency boundary; no empty provider umbrella. |
| §2.19 / §2.23.3 | Stable feature names, internal implementations, tracked public contracts; no cross-domain friendship. |
| §2.21 / §2.23 | Preserve valid Rules/BPMN tests; risk-based behavior tests and causal red/green controls. No text assertions. ADR 0080 D1 leaves greenfield cadence discretionary; do not infer mandatory TDD. |
| §2.22 | Domain extension catalog, root index and cross-domain contribution README updates in the same unit. |
| Elsa §E2.2 / §E2.6 | Workflow runtime consumes frozen executable metadata without Decisions Design/store references; all three deployment shapes qualified. |
| Elsa §E5 / §E7 | Computed versions/bounded package ranges; new test projects in correct tier and solution filters. |

Post-design: the 5 November owner decision resolves domain admission. No test, review or CI gate is waived.

## Project Structure

| Proposed project under `Extensions/Decisions/` | Concrete consumer/responsibility |
| --- | --- |
| `src/Core/Elsa.Decisions.Core.csproj` | Frozen artifacts, compile/evaluate contracts, results/trace for direct callers and adapters. |
| `src/Elsa.Decisions/Elsa.Decisions.csproj` | Bounded compiler/evaluator and scoped feature; standalone consumer needs this plus Core only. |
| `src/Design/Elsa.Decisions.Design.csproj` | Draft validation/publication, cases/comparison and authorization orchestration. |
| `src/Persistence/Core/Elsa.Decisions.Persistence.Core.csproj` | Asset/version/case/test command/query contracts and invariants. |
| `src/Persistence/EntityFrameworkCore/Elsa.Decisions.Persistence.EntityFrameworkCore.csproj` | Relational records/mappings, CAS and atomic publication; engines remain host/test dependencies. |
| `src/Api/Elsa.Decisions.Api.csproj` | Trusted principal/tenant, operation permissions, capabilities and redacted views. |
| `src/Workflows/Design/Elsa.Decisions.Workflows.Design.csproj` | Resolve exact version at workflow publication and contribute frozen node metadata. |
| `src/Workflows/Runtime/Elsa.Decisions.Workflows.Runtime.csproj` | Ordinary `EvaluateDecision` using frozen content and Core; no Design dependency. |
| `studio/Elsa.Studio.Decisions/Elsa.Studio.Decisions.csproj` | Independent resource navigation, table authoring, cases and explanations. |

Create projects only with implemented slices. A separate BPMN package is unnecessary for a normal bound native child. Automatic vendor decisionRef import is outside the claimed interoperability profile.

## Design decisions

1. Separate Decisions: Rules exposes string facts, premises/conclusions and rounds. A language ID cannot make those shapes typed decision tables without changing meaning. Reuse shared mechanics/patterns, not Rules inference codecs/state.
2. Closed grammar: reject every unsupported expression before publication, including unselected rows. Meter parsing/evaluation and check cancellation between bounded operations. Task timeouts do not stop synchronous runaway work.
3. Exact identity: separate original XML SHA-256 from canonical executable SHA-256, profile ID and semantics ID. Bounded synchronous tests freeze candidate/case snapshots in memory and persist completed receipts, not pending durable jobs. Never reuse a semantics ID after changing its interpretation; retain old semantics for published artifacts. Tagged decimal-text values preserve exact numbers through browser/API/adapters.
4. Artifact-only workflow: use `IExecutableCompilationSource` and node metadata. `ExecutableDependencyClaim` denotes workflow executables; do not insert decision IDs. Specific/latest-at-publication both freeze at workflow publication.
5. Normal BPMN: bind `EvaluateDecision` beneath task/businessRuleTask and route on output. Preserve `BpmnDecision` gateway behavior. Do not replace global `IBpmnWorkBinder` or add Decisions switches to the interpreter.
6. Preserve XML: retain original bytes. Supported edits patch owned nodes and preserve covered opaque content. Unknown executable/ambiguous extensions block execution. Refuse edits that cannot preserve meaning/content safely.
7. Explanations: actual cell/rule truth and selection, with structural IDs and no routine raw-value echo. Protect outputs, source, comparisons and history according to asset sensitivity and operation grants.
8. One accessible editor: public Studio primitives/tokens, native labeled controls, table headers, keyboard access, focusable diagnostics, dirty-navigation guard and conflict handling. Backend profile validation is authoritative.
9. Shared execution authority: direct .NET evaluation is a pure library call; ordinary native/BPMN invocation uses the caller's current checkpoint/authority. First-class hosted Decisions executions are not required by #3602. If later admitted, contribute a model protocol/driver through existing RuntimeModelMaterial/model-state/checkpoint ports, never introduce a separate Decisions execution ledger or scheduler.

See [data-model.md](data-model.md), [profile](contracts/dmn-profile.md), [lifecycle](contracts/lifecycle.md) and [research](research.md).

## Delivery and review

One issue and one draft PR. Independently review spec → plan → tasks before product code. The current request authorizes routine design/implementation follow-through; record the explicit additive constitutional admission separately.

Implement compiler/evaluator + packaged direct witness, then persistence/API, Studio lifecycle, native/BPMN frozen publication, and complete qualification/docs. Keep all acceptance criteria open until their actual witnesses pass. Review correctness, standards and issue conformance independently on the draft. Fix in normal commits and re-review; mark ready after that loop and post required gate evidence.

Run whole affected suites with discovered/executed counts, architecture guards, generated maps, locked restore after references change, and exact-head CI. Merge through the current org protocol, verify resulting main and only then close #3602. [quickstart.md](quickstart.md) defines the finite acceptance scenarios.

## Complexity Tracking

The only proposed constitutional change is additive domain admission. Package splits reflect real dependency boundaries. No universal engine, general regression-suite system, actor provider, scheduler, new workflow value model or membership service is introduced.
