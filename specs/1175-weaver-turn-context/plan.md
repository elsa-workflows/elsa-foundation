# Implementation Plan: Authorized module context on every Weaver turn

**Branch**: `agent/codex/3439-weaver-turn-context` | **Date**: 2026-10-31 | **Spec**: [spec.md](spec.md)

**Input**: [#3439](https://github.com/elsa-workflows/elsa-foundation/issues/3439), [spec.md](spec.md)

**Note**: This template is filled in by the `/speckit-plan` command. See `.specify/templates/plan-template.md` for the execution workflow.

## Summary

Prove the reported lifecycle gaps, then repair generic context admission, bounded collection and request delivery while retaining module-owned resource semantics. Reuse M1's actual adapters and independent package consumers. Every #3439 acceptance item stays in scope; [research](research.md) distinguishes confirmed source behavior from experiments still pending.

## Technical Context

**Language/Version**: Repository-pinned .NET/C#, TypeScript and React; existing Python/Node qualification tools.
**Primary Dependencies**: Elsa Agent Core/implementation/API, existing Copilot and Anthropic adapters, Studio SDK/Hosting, owning Weaver integrations, existing Nuplane composition and IAM services.
**Storage**: Existing Agent session/proposal stores and module-owned resource stores. No new persistence system.
**Testing**: Existing Agent xUnit tests, Hosting/Workflows Vitest suites, architecture/maps checks and M1 native package-consumer/provider-protocol witness.
**Target Platform**: Composed Studio/backend and supported independently packaged consumers on existing macOS/Linux qualification paths.
**Project Type**: Modular libraries and Studio application.
**Correctness Bounds**: Configured collection deadlines and context volume limits, cancellation, exactly one request disposition and no repeated effect. No performance benchmark or SLO work.
**Constraints**: One active implementation leaf; no completed proof-root mutation; no client authority; original invocation binding; genuine owner receipts; generic provider transport; no releases, deployment, publisher activation or credential work.
**Scale/Scope**: Six #3439 acceptance items, two existing providers, workflow and independent non-workflow resource. Durable session/approval recovery stays #3441; full module journeys stay M3/M4.

## Constitution Check

Pre-research and post-design review use framework6.1.0, Elsa6.3.0 and `docs/contributing/review-checklist.md`:

- Keep domain contracts in existing Agent/Studio SDK ownership. No new project, runtime engine, universal resource schema or concrete-provider dependency.
- Preserve the one named Sequential context fan-in and its aggregator. Contributor-specific timeouts/typed failure classification belong at the contributor boundary; do not shield arbitrary Sequential event handlers or change global event delivery.
- Defaults stay internal; externally implemented/called models and optional configuration remain deliberate public surface. Update public API baselines, extension catalog and module-author documentation for actual contract deltas.
- Current authorization comes from native server identity and owning services; client inputs are untrusted. Installed guidance is admitted by its owner; no provider resource switches.
- Use behavior tests for risky decisions, causal failures before repairs, and composed/package evidence. No documentation/source-text tests, blanket test expansion or performance suites.
- Preserve valid existing tests and normal workflow validation/atomic edit/whole undo/explicit save. Note any intentionally changed failure contract in the PR.
- Gate maps/architecture and affected project checks; honor build slots and classify skips honestly. No draft/provisional constitution section is used to authorize a new design.

No known constitution exception is required. A failed gate stops the dependent design; routine implementation refinement remains authorized.

## Project Structure

```text
specs/1175-weaver-turn-context/
  spec.md, plan.md, research.md, data-model.md, quickstart.md
  contracts/context-lifecycle.md
  tasks.md
Extensions/Agent/src/{Core,Elsa.Agent,Api}/
Extensions/Agent/tests/Tests/
Studio/src/essentials/Elsa.Studio.Hosting/{Client,StudioSdk}/
Studio/src/essentials/Elsa.Studio.Workflows/Client/
Extensions/Weaver/
tools/studio/Weaver.Conformance.Probe/
docs/program-goals/weaver-across-studio.md
```

Use existing ownership and test projects. Only touch owning-module implementations when needed to implement their existing semantics; keep provider-specific changes limited to transport if an observed generic contract requires them.

## Delivery Sequence

1. Reproduce native acceptance-to-dispatch and accepted-effect-to-continuation authority changes using fresh normal authentication and owner denial/readback. Reproduce frontend admission display, active surface, noncompletion/cancellation, permission advertisement and simultaneous requests with bounded component tests.
2. Implement the smallest generic repairs supported by those failures: current re-admission before disclosure, accurate admitted-context response/display, per-request surface, contributor bounds/typed outcomes and delivery isolation as needed. Reuse current module admission and tool authorization; fail closed if authorized historical evidence cannot be established. Preserve receipts and original targets.
3. Exercise required/optional failures, redaction/untrusted input, budgets, navigation/editor/module/identity transitions and tool policy/scope parity. Update only actual external contract deltas in the [contract note](contracts/context-lifecycle.md).
4. Run full affected tests and native qualification through both adapters and independent package consumers. Verify owner effects and all existing M1 controls. Record deterministic versus actual-adapter protocol evidence separately; no live-provider claim.
5. Reconcile docs/spec/task status, publish a scoped draft PR with gate evidence, converge independent nonauthor review, request normal Code Review and merge through the authorized process. Verify resulting main and then reconcile #3439/M2/Program/Project. Do not close from isolated tests or a prototype.

## Design Review

Planning review covers all FR/SC and six owning acceptance items. Native reproduction remains an explicit task before product contract changes. The [contract note](contracts/context-lifecycle.md) records minimum behavior, not a universal resource model. Reevaluate this check after observed fixes and before PR readiness.
