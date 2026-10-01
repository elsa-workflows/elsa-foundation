# Implementation Plan: Workflow secret safety

**Branch**: `188-workflow-secret-safety` | **Date**: 2026-10-01 | **Spec**: [spec.md](spec.md)

**Input**: Specification for issue [#2211](https://github.com/elsa-workflows/elsa-foundation/issues/2211), phase 0
of [the Connections and Secrets model](../../docs/plans/connections-and-secrets-model.md) (decisions D1 and D10).
Source evidence read at `main` `057adc44f`. This plan is a contract for implementation, delivered as ten slices
([tasks.md](tasks.md#delivery-slices)); it changes no production code.

## Summary

Make a `Secret` binding resolve at the point of use, and keep secret material out of definitions and out of
persisted or emitted runtime output. A `Secret` binding compiles to a new `SecretRead` binding role. The persisted
input snapshot records a withheld envelope (reference plus marker, no value), and the single activation choke point,
`ActivityActivator`, resolves it through a runtime-owned `IRuntimeSecretResolver`. Resolution happens for the
execution's own partition on every activation, so resumption and retries re-resolve. A new bridge project,
`Elsa.Secrets.Workflows`, implements that contract over the existing `ISecretValueResolver` and owns the
transient/permanent classification of its failure codes. Activities declare sensitive and credential inputs on
`[ActivityInput]`; the declaration reaches the catalog, the authoring view and the compiled policy, and an author
cannot downgrade it. One predicate decides whether a binding on a credential input is acceptable. It is enforced at
the seven definition entry points through three integration points: the EF design commands' guarded state writer,
the input-binding compiler, and the git exporter. Values whose policy requires encryption are withheld at the
producer and refused at the checkpoint-commit backstop. Values resolved during an activation are masked in fault,
incident, log and telemetry text. A canary test proves all of it end to end, with a per-protection bite-proof.
Studio gets the descriptor flags, a secret-only editor for credential inputs and a masked editor.

## Technical Context

**Language/Version**: C# on .NET 10 at current repository pins; TypeScript 5.6, React 19 and vitest 2 in
`elsa-foundation-studio`. No SDK or package upgrade.
**Primary Dependencies**: Existing only: System.Text.Json, CShells feature composition, EF Core with the SQLite
provider for tests, and `System.Diagnostics.ActivityListener` to capture runtime spans in tests. No new package.
**Storage**: Existing runtime and design EF stores. No schema or migration change: new data rides inside existing
JSON payloads (`ContentJson`, `StateSource`, catalog `Inputs`). Elsa 4 is unreleased; no migration or compatibility
shim for definitions that already hold literals on newly guarded inputs.
**Testing**: xUnit in the existing projects listed per slice, plus one new project
`tests/essentials/Secrets/Workflows/Tests` (bridge unit tests, two-tenant and lifecycle integration, canary).
Architecture guard and maps check. vitest for Studio.
**Target Platform**: The existing Workbench and embedded hosts on macOS, Linux and Windows.
**Project Type**: Modular .NET backend (essentials modules) plus a separate React Studio repository.
**Performance Goals**: None claimed or measured. Performance measurement is retired (#1668, ADR 0073); no
benchmark, perf test or perf gate is planned. Resolution adds one resolver call per secret-bound input per activation
and no caching (spec assumption).
**Constraints**: The tenant always comes from the executing instance (R2). Business rules stay in the application
layer; stores keep only storage integrity (R7, R8). Every behavioral claim gets a revert or mutation bite-proof.
Every PR that touches a `ProjectReference` refreshes maps (`dotnet run --project tools/maps/Elsa.Maps.Generator -c
Release -- all`) and stages `docs/maps/manifest.json` when it changes. Build only touched projects on the shared
machine.
**Scale/Scope**: Activity inputs only; workflow-level inputs, variables and outputs gain no declaration. No built-in
activity is annotated in phase 0 (R5). Connections, auth schemes, OAuth, external stores and caching are out.

All unknowns are resolved in [research.md](research.md); none remain marked NEEDS CLARIFICATION.

## Constitution Check

*GATE: checked before research and again after design. Result: pass, with the provisional sections named.*

Framework constitution v4.0.0:

- **§2.1 three-layer separation**: the only new contracts sit in `.Core` projects (`IRuntimeSecretResolver` and
  models in `Elsa.Workflows.Runtime.Core`; `ICredentialLiteralValidator` in `Elsa.Workflows.Design.Validations.Core`;
  the acceptance predicate in `Elsa.Workflows.Design.Core`). The bridge is Layer 3 and references only
  `Elsa.Secrets.Core` and `Elsa.Workflows.Runtime.Core`. No implementation-to-implementation reference across
  unrelated domains is added. No `.Core` gains a heavy package.
- **§2.2 naming**: `Elsa.Secrets.Workflows` follows the secondary-domain sub-rule (the model-owning domain, Secrets,
  is the prefix), with `Elsa.Secrets.Nuplane` as precedent.
- **§2.5 and §2.5.1**: the new feature class is public, not sealed, with a virtual `ConfigureServices`; collaborators
  are registered against contracts. The resolver and the mask are scoped (they execute or hold per-execution state);
  no new singleton.
- **§2.6.2 replacement contracts**: `IRuntimeSecretResolver`, `ICredentialLiteralValidator` and
  `IRuntimeSecretMask` are declared as replacement contracts in their XML documentation and registered once; each
  feature registration test asserts a single implementation.
- **§2.6.4 design/runtime split**: the design-time rule (credential literal) and the runtime contract (secret
  resolution) are separate contracts with no shared runtime concern.
- **§2.7 adapter**: the bridge adapts Secrets to the runtime contract. No sync-contributor exception (§2.6.5) is used.
- **§2.10 CQS**: no persistence contract changes shape; the design commands gain a guard dependency only.
- **§2.11 DependsOn**: `SecretsWorkflows` depends on `Secrets` and the activation feature;
  `WorkflowsDesignEntityFrameworkCore` and `WorkflowsDesignGitReconciliation` depend on `WorkflowDesignValidations`
  so a missing rule fails at composition, not silently at write time.
- **§2.12 and Elsa §E4 (deferred)**: no configuration or settings classification is introduced. The bridge has no
  settings; the tenant comes from the execution (R2), unlike `SecretsNuplane`, which must name a tenant.
- **§2.16.1**: `Elsa.Secrets.Workflows` will likely be under 100 lines and is exempt as class 6 (cross-domain
  contribution seam; folding it into either domain creates a forbidden dependency).
- **§2.17**: the `"Secret"` expression-type literal is duplicated in the compiler and the predicate rather than
  creating a Publishing or Design dependency on Secrets; a test pins equality.
- **§2.21.1 golden rule**: no test objective is removed. Extracting `IsBound` from `RequiredInputOutputValidator` is a
  refactor under its existing tests. Hosts that construct EF design commands are rewired to compose the validator;
  their objectives are unchanged.
- **§2.22 and §2.22.1**: update `src/essentials/Workflows/Runtime/EXTENSION_POINTS.md` (new overridable contracts),
  `src/essentials/Secrets/EXTENSION_POINTS.md` (Runtime Integration section rewritten to the shipped mechanism, bridge
  listed), `src/essentials/Workflows/Design/Validations/EXTENSION_POINTS.md` and `README.md` (new contract and rule),
  `src/essentials/Activities/Runtime/README.md` (attribute flags, activation resolution), and a README for the bridge
  with a "Cross-domain contributions" section.
- **§2.23.1 and §2.23.2**: a registration test for `SecretsWorkflowsFeature`; branch-covered unit tests with stubbed
  dependencies for every new logic-bearing class (bridge mapping per code, predicate per row, activator branches,
  mask, commit rule, compiler branches). Tests are written first and must fail before implementation.
- **§2.23.3**: logic-bearing implementations are `public sealed`.
- **§2.23.5 exception boundaries**: domain exceptions (`RuntimeSecretResolutionException`,
  `CredentialLiteralRefusedException`, `SecretMaskedException`) live in `.Core` projects; the bridge never lets a
  store exception or its text escape.
- **§2.24 (draft, pending ratification)**: only catalogued patterns are used (three-layer separation, replacement
  contracts, adapter). No new pattern is introduced, so the provisional status does not block this plan.
- **§4.2 SemVer**: adding `ValuePresence.Withheld` and `RuntimeInputBindingSource.SecretRead` changes behavior that
  exhaustive consumers depend on. Elsa 4 is pre-release; the computed-version magnitude gate (ADR 0067) classifies
  the bump. No hand-edited version.

Elsa constitution v4.2.0:

- **§E2.2 Design/Runtime split**: no Runtime project gains a Design reference. The bridge references Runtime.Core
  only. Publishing already references both sides and gains no new reference.
- **§E2.2.3 deployment shapes**: runtime-only hosts without Secrets still run; a secret-bound activity waits with an
  activation-failure incident (R4). Design-only hosts enforce the rule without the runtime.
- **§E2.6.1 executable-always-runs**: a missing bridge is a missing module, so it parks the activity instead of
  faulting it; secret lifecycle failures are domain gates and fault.
- **§E2.6.2 artifact-only runtime**: the artifact carries the `SecretRead` binding; the runtime needs nothing from
  Design to resolve it.
- **§E2.8 activity catalog (Model X provisional)**: `InputDefinition` gains nullable flags that keep existing
  catalog hashes stable. The sentence saying the CLR scanner honors only `[Version]` and `[Required]` is already
  behind the code and is not edited here (research R5).
- **§E2.9 and §E2.9.7 (provisional)**: `WorkflowDefinitionState` gains no member; a secret reference is authored
  content, a withheld marker is runtime state. The credential guard runs inside the draft-mutation commands, next to
  the in-lock validation gate that §E2.9.7 already places there; the rule itself lives in Design.Validations.
- **§E6 type names**: `IRuntimeSecretResolver`, `RuntimeSecretReference` and `RuntimeSecretResolution` use the
  `Runtime` qualifier to disambiguate from Secrets' `ISecretValueResolver` and `SecretReference` (R2 allows it);
  `ICredentialLiteralValidator` uses the `Validator` suffix for a findings-returning contract (R4);
  `RuntimeInputBindingSource.SecretRead` mirrors `VariableRead`; all names are within four components (R1). No banned
  word (R3).

No constitutional exception is requested. Re-checked after Phase 1 design: unchanged.

## Project Structure

### Documentation (this feature)

```text
specs/188-workflow-secret-safety/
  spec.md, plan.md, research.md, data-model.md, quickstart.md, tasks.md
  contracts/runtime-secret-resolution.md
  contracts/input-sensitivity-declaration.md
  contracts/credential-literal-rule.md
  contracts/withheld-values-and-masking.md
  contracts/studio-input-descriptor.md
  contracts/acceptance-proof-matrix.md
  checklists/requirements.md
```

### Source Code (repository root)

Existing projects changed (paths verified at `057adc44f`):

- `src/essentials/Workflows/Runtime/Core/`: `Models/RuntimeInputBinding.cs` (source `SecretRead`, payload
  `Secret`), `Models/ValueEnvelope.cs` (presence `Withheld`, `WithheldValue`), new `Contracts/IRuntimeSecretResolver.cs`,
  `Contracts/IRuntimeSecretMask.cs`, new exceptions under `Exceptions/`, `Models/ValuePolicyCombiner.cs` (declaration
  and downgrade helper).
- `src/essentials/Workflows/Runtime/`: `Services/Values/RuntimeActivityInputMaterializer.cs` (withheld emission),
  `Services/Values/RuntimeExternalEnvelopeStorage.cs` (producer-side withholding),
  `Services/Checkpoints/RuntimeCheckpointCommitValidator.cs` (backstop),
  `Services/Executables/WorkflowExecutableHasher.cs` (binding format),
  `Services/Incidents/ActivityActivationFailureHandler.cs` (missing resolver),
  `Services/Incidents/DefaultRuntimeFaultCapturePolicy.cs` (masked exception type), the scoped mask implementation,
  and `Extensions/RuntimeCoreServiceCollectionExtensions.cs` (registrations).
- `src/essentials/Activities/Runtime/`: `Services/ActivityActivator.cs` (resolution at activation),
  `Services/ActivityFaultIncidentRecorder.cs` (retryable from exception),
  `Services/WorkflowInvokeActivitySchedulerWorkHandler.cs`, `Services/WorkflowResumeBookmarkSchedulerWorkHandler.cs`,
  `Services/StructuralParentEvaluationSupport.cs` (masking boundary), `Services/ActivityFaultProjection.cs` callers,
  `Services/ActivityExecutionInspection.cs` (withheld tolerance).
- `src/essentials/Activities/Runtime/Core/Attributes/ActivityInputAttribute.cs` (flags).
- `src/essentials/Activities/Design/Core/Models/InputDefinition.cs`,
  `src/essentials/Activities/Design/Reconciliation/Clr/Services/ClrAssemblyScanner.cs`,
  `src/essentials/Activities/Design/Api/Models/ActivityAuthoringCatalogView.cs`,
  `src/essentials/Activities/Design/Api/Services/ActivityAuthoringCatalogReader.cs`,
  `src/essentials/Activities/Design/Api/Services/IntrinsicAuthoringDescriptorProvider.cs`.
- `src/essentials/Workflows/Publishing/Services/RuntimeInputBindingCompiler.cs` (`SecretRead`, rule, plan) and
  `Services/ExecutableNodeCompiler.cs` (policy from declaration, intrinsic refusal).
- `src/essentials/Workflows/Design/Core/` (predicate and rule id), `Validations/Core/` (contract, exception),
  `Validations/` (validator, registration, shared `IsBound`), `Persistence/EntityFrameworkCore/Commands/EfWorkflowDesignCommands.cs`
  (guarded writer), `Api/Endpoints/WorkflowDesignExceptionTranslator.cs` (400 mapping),
  `Reconciliation/Git/Services/GitWorkflowExporter.cs` (export guard), the two feature classes for `DependsOn`.
- `src/essentials/Workflows/Runtime/Api/Services/WorkflowExecutableInspector.cs` and
  `src/essentials/Workflows/ExecutionEvidence/Services/ExecutionEvidenceCheckpointEnricher.cs` (withheld rendering).
- `src/essentials/Secrets/Elsa.Secrets.csproj` (add `Workflows/**/*` to the sibling `Compile Remove` globs, or the
  bridge's sources compile into `Elsa.Secrets` twice) and `src/essentials/Secrets/EXTENSION_POINTS.md`.
- Shells files that enable `Secrets`: `src/apps/Elsa.Workbench/shells.json`, `shells.baseline.json`, and the compose
  shells file (`docker/compose/elsa-workbench.shells.json`) when it enables Secrets.

New projects:

- `src/essentials/Secrets/Workflows/Elsa.Secrets.Workflows.csproj` (bridge, feature, README).
- `tests/essentials/Secrets/Workflows/Tests/Elsa.Secrets.Workflows.Tests.csproj` (bridge tests, integration, canary).

Separate repository (`elsa-foundation-studio`), slice 10 only: see
[the Studio contract](contracts/studio-input-descriptor.md).

**Structure Decision**: One new production project (the bridge) and one new test project. Everything else extends
existing owners. The bridge exists so the runtime stays free of Secrets types (R1); the test project exists because
the canary composes design, publishing, runtime and Secrets together, which no existing test host does (R10).

## Implementation sequence

1. Slice 1 (housekeeping) at any time.
2. Slice 2 introduces the runtime value model, the `SecretRead` compile path and the withheld snapshot. Activation
   refuses withheld inputs loudly until slice 3, so `main` never resolves a secret into the snapshot and never
   hydrates a withheld input as null (R3, R16).
3. Slice 3 adds activation-time resolution and failure semantics behind a fake resolver.
4. Slice 4 adds the bridge, composition and the two-tenant and lifecycle integration. Secrets become usable here.
5. Slice 5 (declaration and policy) is independent of slices 2 to 4 in behavior but edits
   `RuntimeInputBindingCompiler.cs` and `ExecutableNodeCompiler.cs`, so it lands after slice 2 merges or rebases on it.
6. Slice 6 (the rule) needs slice 5's `IsCredential`.
7. Slice 7 (withholding backstop and surfaces) needs slices 2, 3 and 5; slice 8 (masking) needs slice 3.
8. Slice 9 (canary) needs slices 2, 3, 4, 6, 7 and 8.
9. Slice 10 (Studio) needs slice 5's wire fields on `main`; it can run in parallel with slices 6 to 9.

## Validation and delivery

Each slice runs its focused suites and the architecture guard, refreshes maps when it touches project references,
and records its bite-proof in the PR (see [quickstart](quickstart.md#bite-proof-procedure-every-behavioral-claim)).
The canary PR records the per-protection table (A15). The PR that merges the last backend slice sets the spec to
`Implemented`. Status changes and claims are posted on each slice's issue. The specification PR itself changes
documents only: `git diff --check`, cited-path verification and the maps check.

## Complexity Tracking

No constitutional violation needs justification. Two choices add moving parts on purpose and are recorded here so a
reviewer can challenge them:

| Choice | Why needed | Simpler alternative rejected because |
|---|---|---|
| New `Elsa.Secrets.Workflows` project | keeps Secrets types and failure classification out of the runtime contract (R1) | a direct Runtime to `Elsa.Secrets.Core` reference is viable and is the named fallback; it was not chosen because it moves Secrets vocabulary into the runtime |
| A new `ValuePresence.Withheld` | the persisted snapshot needs a value-free shape that every consumer must handle explicitly (R3) | reusing external references conflates runtime payload storage with secrets and has no tenant parameter |
