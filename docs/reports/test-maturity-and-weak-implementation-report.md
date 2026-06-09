# Test Maturity and Weak Implementation Report

Status: point-in-time verification report created from the current constitution gates, generated maps, source markers, and test surface.

## Scope

This report is the first concrete run of the AI-provider-neutral [Verify Codebase Against Constitution](../skills/catalog.md#verify-codebase-against-constitution) workflow for test maturity and weak/stub implementation risk.

Inputs:

- [Framework constitution](../../.specify/memory/constitution-framework.md), especially `§2.21` and `§2.23`.
- [Elsa constitution](../../.specify/memory/constitution.md), especially `§E2.2`, `§E2.6`, `§E2.8`, and `§E2.9`.
- [Test map](../maps/test-map.md).
- [Project reference map](../maps/project-reference-map.md).
- [Architecture reference map](../maps/architecture-reference-map.md).
- [Maps v2 findings](maps-v2-findings.md).
- Source search for `TODO`, `DEFERRED`, `deferred`, `pending`, `stub`, `placeholder`, `NotImplementedException`, `future`, and `follow-up`.

This report does not claim line or branch coverage. It classifies evidence into review findings that can become work units.

## Gate Summary

The relevant gates are:

- Framework `§2.21.1`: refactors preserve existing tests unless removal is explicitly approved.
- Framework `§2.23.1`: feature classes require registration tests.
- Framework `§2.23.2`: logic-bearing implementations require branch-covered unit tests with stubbed dependencies.
- Framework `§2.23.6`: integration testing remains out of scope, so weak unit-test coverage cannot be excused by a future integration-test policy.
- Elsa `§E2.2`: `Elsa.Workflows.Runtime.*` must not directly depend on `Elsa.Workflows.Design.*`.
- Elsa `§E2.6`: runtime must eventually execute runnable artifacts without loading design-side data.
- Elsa `§E2.9.4`: `WorkflowDefinitionState` scope enforcement by analyzer is deferred; current enforcement is review discipline.

## Evidence Snapshot

Generated test-map facts:

- Test projects: 6.
- Source projects directly referenced by at least one test project: 37.
- Source projects not directly referenced by test projects: 31.

Observed test count by test project, counted by `[Fact]` and `[Theory]` markers:

| Test project | Test markers |
|---|---:|
| `Elsa.Activities.Composition.Tests` | 4 |
| `Elsa.Activities.Design.Tests` | 76 |
| `Elsa.Activities.Design.Tests.ClrFixture` | 0 |
| `Elsa.Activities.Runtime.Tests` | 7 |
| `Elsa.Workflows.Design.Tests` | 136 |
| `Elsa.Workflows.Publishing.Api.Tests` | 3 |

The mature test surface is concentrated in recent activity/design and workflow/design work. That is good evidence for those units, but it leaves broad domains without direct test-project references.

## Findings

### F1 - Runtime core is explicitly stub-like

Classification: weak/stub implementation.

Evidence:

- `Elsa.Workflows.Runtime.Core` is listed as a stub in Elsa `§E2.1` and `§E2.2.2`.
- [WorkflowExecutionContext](../../src/Elsa/Workflows/Runtime/Core/WorkflowExecutionContext.cs) implements `IWorkflowExecutionContext`, but every public property or method throws `NotImplementedException`.
- [Elsa.Workflows.Runtime.Core](../maps/test-map.md#source-projects-without-direct-test-reference) has no direct test-project reference.

Risk:

The runtime contract is central to Elsa `§E2.6`, but the current implementation is not executable. This is acceptable only while Runtime is intentionally deferred. It should stay classified as an architecture follow-up, not as ordinary missing polish.

Next action:

Use the [runtime execution pre-spec handoff](runtime-execution-pre-spec-handoff.md) before implementing runtime behavior. The architect-owned Speckit work unit should define the minimal executable artifact contract, required unit tests, allowed design/runtime crossing points, execution-context lifetime model, graph/node naming, and activity/workflow I/O model boundaries.

### F2 - Runtime JavaScript has a known deferred Workflows.Design reference

Classification: known deferred architecture debt.

Evidence:

- [Architecture reference map](../maps/architecture-reference-map.md#direct-designruntime-signals) reports one `runtime-to-design` signal.
- [Elsa.Workflows.Runtime.JavaScript.csproj](../../src/Elsa/Workflows/Runtime/JavaScript/Elsa.Workflows.Runtime.JavaScript.csproj) directly references `Elsa.Workflows.Design.Core`.
- Elsa `§E2.2` says there must be no direct dependency from `Elsa.Workflows.Runtime.*` to `Elsa.Workflows.Design.*`.
- Follow-up review found no active `Elsa.Workflows.Design.*` source usage in `Elsa.Workflows.Runtime.JavaScript`; the reference exists because JavaScript function declarations are currently contributed across both designer and runtime surfaces instead of being split into stable phase-owned packages.

Risk:

This remains a constitution-boundary exception candidate because the constitution text treats the Workflows runtime-to-design direction as a hard rule. The current preference is to avoid code changes until the Elsa brain / workspace split is stable, so this should not trigger an immediate refactor. The durable risk is rediscovery: future reviews may treat the generated-map signal as accidental drift unless the deferred shortcut stays documented.

Next action:

Keep this as a named deferred boundary item until Elsa brain / workspace ownership is stable. The [runtime execution pre-spec handoff](runtime-execution-pre-spec-handoff.md) preserves this classification so future runtime planning does not treat the generated-map signal as accidental drift. Future resolution should split design-time JavaScript declarations from runtime JavaScript bindings, likely by moving designer-facing declarations into a design/workspace-side package and keeping runtime pre/post processors in `Elsa.Workflows.Runtime.JavaScript`. Share only neutral shape records from a stable `.Core` or primitives package if the split needs common data.

### F3 - Workflow-as-activity is construct-only and intentionally non-executable

Classification: weak/deferred implementation.

Evidence:

- [WorkflowDefinitionActivity](../../src/Elsa/Activities/Composition/Runtime/Activities/WorkflowDefinitionActivity.cs) documents Unit 006 as construct-only.
- Its `Execute` override throws `NotSupportedException` and states execution is deferred to the consumer/pinning unit.
- The composition tests cover constructor/identity behavior, not load-and-run execution.

Risk:

This is not accidental. The risk is that the catalog and construction path can look complete while the runtime behavior remains absent. Future consumers may treat workflow-backed activities as executable before the pinning/runtime unit exists.

Next action:

Keep this as a named follow-up under the consumer/pinning/runtime execution unit. Use the [runtime execution pre-spec handoff](runtime-execution-pre-spec-handoff.md) to decide artifact, pinning, nested execution, and cycle-guard questions before adding executable behavior tests.

### F4 - Test maturity is uneven across domains

Classification: missing/weak tests.

Evidence:

- [Test map](../maps/test-map.md#source-projects-without-direct-test-reference) lists 31 source projects without direct test references.
- Untested domains include Caching, much of Expressions, HTTP, Locking.FileSystem, Tasks, Workflows.Runtime, Elsa3 import/mapping, and the host.
- Several untested projects expose feature classes or logic-bearing implementations, for example `MemoryCacheFeature`, `EventsFeature` support components, JavaScript/Jint services, HTTP content handlers, and runtime HTTP services.

Risk:

Framework `§2.23.1` and `§2.23.2` make new feature registration and implementation tests the default. The current surface suggests recent feature work has followed that discipline better than older or peripheral domains.

Next action:

Create a targeted "registration-test coverage pass" before broad behavior testing. Start with projects that expose `IShellFeature` or `FastEndpointsFeatureBase` classes and have no direct test-project reference.

### F5 - Some public or logic-bearing classes still throw `NotImplementedException`

Classification: weak/stub implementation.

Evidence:

- [WorkflowDesignContext](../../src/Elsa/Workflows/Design/Core/WorkflowDesignContext.cs) throws `NotImplementedException` for both exposed members.
- [VariableExpressionDescriptor](../../src/Elsa/Expressions/Services/VariableExpressionDescriptor.cs) throws `NotImplementedException` for `HandlerFactory` and `Properties`.
- [MultiDownloadableContentHandler](../../src/Elsa/Http/Services/MultiDownloadableContentHandler.cs) throws `NotImplementedException` for `Priority`.
- [ScriptExecutionContext](../../src/Elsa/Workflows/Runtime/JavaScript/Activities/RunJavaScript/TestClasses/ScriptExecutionContext.cs) throws `NotImplementedException` in runtime-facing test-class code.

Risk:

Some of these may be placeholder types outside a shipped path. Others sit in feature implementation projects with no direct tests. The report cannot decide intent from the throw alone, but these are stronger signals than ordinary TODO comments because they are executable failure paths.

Next action:

Classification follow-up exists in [NotImplemented classification](notimplemented-classification.md). Use that report to choose the focused implementation or planning unit; remove or quarantine placeholders that are not part of an accepted deferred unit.

### F6 - Required input/output validation has a deliberate workflow-level gap

Classification: missing test/known domain gap.

Evidence:

- [RequiredInputOutputValidator](../../src/Elsa/Workflows/Design/Validations/Validators/RequiredInputOutputValidator.cs) documents that workflow-level `State.Inputs` and `State.Outputs` are deliberately skipped until a workflow-level binding surface exists.
- Tests cover the activity-level validation branches and note the workflow-level branch is deferred.
- [Unfinished work](unfinished-work.md) records the required input/output data-shape follow-up as inventory.

Risk:

This is well documented and lower risk than the runtime placeholders. The main risk is losing the link between the validation gap and the future Unit D/E data-shape work.

Next action:

Keep the existing follow-up. Do not add tests that guess workflow-level binding semantics before the data shape exists.

### F7 - Event and mediator pipeline coverage is intentionally smoke-level in some places

Classification: missing/weak tests.

Evidence:

- [EventPublisherSmokeTests](../../tests/Elsa.Workflows.Design.Tests/Unit/EventPublisherSmokeTests.cs) says branch coverage of strategy/invoker middleware is deferred to `Elsa.Events.Tests`.
- [CrossFeatureValidatorSubscriptionTests](../../tests/Elsa.Workflows.Design.Tests/Unit/CrossFeatureValidatorSubscriptionTests.cs) notes production event-pipeline coverage is deferred to a mediator/events follow-on.
- `Elsa.Events.Strategies` has no direct test-project reference in the generated test map.

Risk:

The event strategy machinery is a framework-sanctioned composition pattern. Smoke tests are useful, but the shared event pipeline deserves its own unit tests because many domains depend on it.

Next action:

Plan `Elsa.Events.Tests` and possibly `Elsa.Mediator.Tests` as shared-infrastructure test work, not as part of a feature-specific validation unit.

## Suggested Priority Order

1. Runtime execution pre-spec handoff for `Elsa.Workflows.Runtime.Core`, then architect-owned Speckit planning for the execution seam.
2. Registration-test coverage pass for untested feature classes.
3. Shared event/mediator pipeline test unit.
4. `NotImplementedException` classification pass.
5. Keep validation workflow-level binding gap attached to Unit D/E.
6. Keep the Runtime JavaScript design-reference shortcut documented until Elsa brain / workspace ownership is stable.

## What This Report Does Not Do

- It does not require immediate implementation of runtime execution.
- It does not turn every untested project into a violation.
- It does not prescribe integration testing.
- It does not expand generated maps.
- It does not create a new official skill. The workflow should be promoted to an executable skill only after this report shape is reviewed and accepted.
