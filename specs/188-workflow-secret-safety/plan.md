# Implementation Plan: Workflow secret safety

**Branch**: `188-workflow-secret-safety` | **Date**: 2026-10-01 | **Spec**: [spec.md](spec.md)

**Input**: Specification for issue [#2211](https://github.com/elsa-workflows/elsa-foundation/issues/2211), phase 0
of [the Connections and Secrets model](../../docs/plans/connections-and-secrets-model.md) (decisions D1 and D10).
Source evidence read at `main` `057adc44f`. This plan is a contract for implementation, delivered as eleven slices
([tasks.md](tasks.md#delivery-slices)); it changes no production code.

## Summary

Make a `Secret` binding resolve at the point of use, and keep secret material out of definitions and out of
persisted or emitted runtime output. A `Secret` binding compiles to a new `SecretRead` binding role. The persisted
input snapshot records a withheld envelope (reference plus marker, no value), and `ActivityActivator`, the
activation point for every CLR activation, resolves it through a runtime-owned `IRuntimeSecretResolver`. Activity
kinds that read inputs outside the activator (intrinsics, graph activities, checkpoint participants) cannot carry a
`Secret` binding: publish refuses it (research R3a). Resolution happens for the execution's own partition on every
activation, so resumption re-resolves, and it fails closed when the instance's recorded tenant disagrees with the
partition. Publish also refuses a `Secret` binding on inputs an activity copies into its own persisted state or
reads at publish, or returns in its result, which activity types declare with a new `[RefusesSecretBinding]`
attribute (research R12), and on any input the existing conversion rules cannot convert text into (research R11). A
new bridge project, `Elsa.Secrets.Workflows`, implements that contract over the existing `ISecretValueResolver` and
owns the transient/permanent classification of its failure codes. Activities declare sensitive and credential inputs on `[ActivityInput]`; the
declaration reaches the catalog, the pinned contract, the authoring view and the compiled policy, and an author
cannot downgrade it, and an input that refuses secret bindings cannot be declared a credential. One predicate decides
whether a binding on a credential input is acceptable. It is enforced in the application layer only, at the seven
definition entry points, through four integration points: Design API admission before the design commands
(promote included), the version reconciler (per item), the input-binding compiler, and the git exporter (per
version). Every caller of a state-writing design command, and the exporter, takes the rule's contract,
`ICredentialLiteralValidator`, and a caller that refuses a whole request admits through one shared helper,
`WorkflowStateAdmission`. As built in slice 6's review, the rule also runs at an eighth entry point, the Elsa 3
collection import, through a fifth integration point, `ReusableActivityCollectionImporter`, which judges every activity
node it maps (spec FR-008 note). No persistence project gains a rule or a reference.
An input whose policy requires encryption accepts only a secret reference; anything else that requires encryption
is withheld at the producer and refused at the checkpoint-commit backstop. Values resolved during an activation are
masked in fault, incident and log text without changing the fault's classification. A canary test proves all of it
end to end, with a per-protection bite-proof that injects upstream failures where a protection would otherwise be
unreachable. By owner decision, `SendHttpRequest` gains a credential input `Authorization`, sent as the request's
`Authorization` header, so one built-in consumes a secret end to end (research R17). Studio gets the descriptor flags, a secret-only editor for credential inputs and a masked editor.

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
**Scale/Scope**: Activity inputs only; workflow-level inputs, variables and outputs gain no declaration. One built-in
input is annotated in phase 0, `SendHttpRequest.Authorization` (owner decision, R17). Connections, auth schemes, OAuth, external stores and caching are out.

All unknowns are resolved in [research.md](research.md); none remain marked NEEDS CLARIFICATION.

## Constitution Check

*GATE: checked before research and again after design. Result: pass, with the provisional sections named.*

Framework constitution v4.0.0:

- **§2.1 three-layer separation**: the new contracts sit in `.Core` projects (`IRuntimeSecretResolver`,
  `IRuntimeFaultClassification`, `IRuntimeSecretMask` and models in
  `Elsa.Workflows.Runtime.Core`; `ICredentialLiteralValidator` in `Elsa.Workflows.Design.Validations.Core`; the
  acceptance predicate in `Elsa.Workflows.Design.Core`). Two new implementations also sit in Layer 1 projects, under §2.1's
  thin-utility allowance: `WorkflowDraftStateHash` (a BCL-only SHA-256 helper in
  `Elsa.Workflows.Design.Persistence.Core`) and `WorkflowStateAdmission` (in `Elsa.Workflows.Design.Validations.Core`).
  `WorkflowStateAdmission` stays mechanical: it throws the findings of `ICredentialLiteralValidator` from `AdmitAsync`,
  with no rule logic of its own (the rule lives in the predicate and the validator). As built in slice 6 both are static
  classes, `WorkflowStateAdmission` an extension over the validator contract like `DraftValidationGate`: the
  architecture suite's `.Core` shape ratchet admits no new class with injected dependencies in a `.Core` project. The bridge is Layer 3 and references only
  `Elsa.Secrets.Core` and `Elsa.Workflows.Runtime.Core`. Every new `ProjectReference` from a production library
  targets a `.Core` project. Two kinds of reference are the allowed exceptions: the Workbench app host's reference to
  the bridge (`Elsa.Secrets.Workflows`, host composition), and test-project references, which target implementation
  projects as the tests need. The full list is in [Project reference changes](#project-reference-changes), so the claim "no
  implementation-to-implementation reference across unrelated domains" can be checked line by line. No `.Core`
  gains a heavy package.
- **§2.2 naming**: `Elsa.Secrets.Workflows` follows the secondary-domain sub-rule (the model-owning domain, Secrets,
  is the prefix), with `Elsa.Secrets.Nuplane` as precedent.
- **§2.5 and §2.5.1**: the new feature class is public, not sealed, with a virtual `ConfigureServices`; collaborators
  are registered against contracts. The resolver and the mask are scoped (they execute or hold per-execution state);
  no new singleton.
- **§2.6.2 replacement contracts**: `IRuntimeSecretResolver`, `ICredentialLiteralValidator` and
  `IRuntimeSecretMask` are declared as replacement contracts in their XML
  documentation and registered once; each feature registration test asserts a single implementation.
  `CredentialLiteralValidator` is additionally registered as an `IDraftValidator` contribution (§2.6.1), for
  reporting only. `WorkflowStateAdmission` is a static helper, not a replacement contract, so a host cannot
  swap the admission out; it sits in `Elsa.Workflows.Design.Validations.Core` next to the existing
  `DraftValidationGate`. A second `ICredentialLiteralValidator` fails shell activation (slice 6).
- **§2.6.4 design/runtime split**: the design-time rule (credential literal) and the runtime contract (secret
  resolution) are separate contracts with no shared runtime concern.
- **§2.7 adapter**: the bridge adapts Secrets to the runtime contract. No sync-contributor exception (§2.6.5) is used.
- **§2.10 CQS**: no rule enters persistence. One persistence contract changes: `IPromoteDraftToVersionCommand` today declares two
  `Execute` overloads; the plan removes both and reduces the contract to one method with a new required
  `expectedStateHash` parameter (a breaking change, acceptable because Elsa 4 is unreleased), which
  `EfPromoteDraftToVersionCommand` compares in-lock and refuses with a conflict, a storage-integrity compare-and-set so that promote writes only what its endpoint admitted (research R7).
  The other design commands are untouched.
- **§2.11 DependsOn**: `SecretsWorkflows` depends on `Secrets` and the activation feature; `WorkflowsDesignApi`,
  `JsonWorkflowReconciliation` and `WorkflowsDesignGitReconciliation` (the application-layer features that admit
  state) depend on `WorkflowDesignValidations`, so a missing rule fails at composition, not silently at write time.
  No persistence feature gains a `DependsOn`.
- **§2.12 and Elsa §E4 (deferred)**: no configuration or settings classification is introduced. The bridge has no
  settings; the tenant comes from the execution (R2), unlike `SecretsNuplane`, which must name a tenant.
- **§2.16.1**: `Elsa.Secrets.Workflows` will likely be under 100 lines and is exempt as class 6 (cross-domain
  contribution seam; folding it into either domain creates a forbidden dependency).
- **§2.17**: the `"Secret"` expression-type literal is duplicated in the compiler and the predicate rather than
  creating a Publishing or Design dependency on Secrets; a test pins equality.
- **§2.17, deliberately not applied (slice 6 review)**: `CredentialInputBinding.IsAccepted` is a domain decision in a
  `.Core` project (`Elsa.Workflows.Design.Core`) with two production consumers, the design-time validator and the
  publish compiler, which §2.17 would normally leave duplicated in each. It stays as one shared definition because its
  whole point is that save and publish never disagree about a binding: two copies could drift, and a binding one
  accepts and the other refuses is exactly the defect the rule exists to prevent. The same holds, for the same reason,
  for `SecretReferencePayload` (slice 6 review round 3), the one definition of a well-formed secret reference payload,
  which the predicate applies at save and the publish compiler reads every secret reference through.
- **§2.21.1 golden rule**: no test objective is removed. Extracting `IsBound` from `RequiredInputOutputValidator` is a
  refactor under its existing tests. Test hosts that construct the Design API callers, the reconciler or the
  exporter are rewired to compose the validator; their objectives are unchanged.
- **§2.22 and §2.22.1**: update `src/essentials/Workflows/Runtime/EXTENSION_POINTS.md` (new overridable contracts),
  `src/essentials/Secrets/EXTENSION_POINTS.md` (Runtime Integration section rewritten to the shipped mechanism, bridge
  listed), `src/essentials/Workflows/Design/Validations/EXTENSION_POINTS.md` and `README.md` (new contract and rule),
  `src/essentials/Activities/Runtime/README.md` (attribute flags, activation resolution), and a README for the bridge
  with a "Cross-domain contributions" section.
- **§2.23.1 and §2.23.2**: a registration test for `SecretsWorkflowsFeature`; branch-covered unit tests with stubbed
  dependencies for every new logic-bearing class (bridge mapping per code, predicate per row,
  activator branches including the tenant check, mask and classification copy, commit rule, compiler branches).
  Tests are written first and must fail before implementation.
- **§2.23.3**: logic-bearing implementations are `public sealed`.
- **§2.23.5 exception boundaries**: domain exceptions (`RuntimeSecretResolutionException`,
  `CredentialLiteralRefusedException`, `SecretMaskedException`) live in `.Core` projects; the bridge never lets a
  store exception or its text escape. Masking replaces text, never classification (`IRuntimeFaultClassification`).
- **§2.24 (draft, pending ratification)**: only catalogued patterns are used (three-layer separation, replacement
  contracts, adapter). No new pattern is introduced, so the provisional status does not block this plan.
- **§4.2 SemVer**: adding `ValuePresence.Withheld`, `RuntimeInputBindingSource.SecretRead` and a member on
  `ActivityActivationRequest` changes shapes that consumers depend on. Elsa 4 is pre-release; the computed-version magnitude gate (ADR 0067) classifies
  the bump. No hand-edited version.

Elsa constitution v4.2.0:

- **§E2.2 Design/Runtime split**: no Runtime project gains a Design reference. The bridge references Runtime.Core
  only. Publishing already references both sides and gains no new reference. The two design reconciliation projects
  gain a reference to `Elsa.Workflows.Design.Validations.Core`, a design-side contract.
- **§E2.2.3 deployment shapes**: runtime-only hosts without Secrets still run; a secret-bound activity waits with an
  activation-failure incident (R4). Design-only hosts enforce the rule without the runtime.
- **§E2.6.1 executable-always-runs**: a missing bridge is a missing module, so it parks the activity instead of
  faulting it; secret lifecycle failures are domain gates and fault.
- **§E2.6.2 artifact-only runtime**: the artifact carries the `SecretRead` binding; the runtime needs nothing from
  Design to resolve it.
- **§E2.8 activity catalog (Model X provisional)**: `InputDefinition` gains nullable flags that keep existing
  catalog hashes stable. `SendHttpRequest` changes content (a new input), and takes a new version through the
  existing computed package version, with no `[Version]` attribute (research R17). The sentence saying the CLR scanner honors only `[Version]` and `[Required]` is already
  behind the code and is not edited here (research R5).
- **§E2.9 and §E2.9.7 (provisional)**: `WorkflowDefinitionState` gains no member; a secret reference is authored
  content, a withheld marker is runtime state. The rule lives in Design.Validations and runs in the application
  layer, above the design commands; promote is admitted by its endpoint like every other entry point, because the
  in-lock validation gate §E2.9.7 places in the promotion command runs only when the command's optional
  `IInlineEventPublisher` is composed. The validator still contributes an `IDraftValidator`, so that gate re-checks
  in-lock where it runs. The only persistence change is the promote command's content precondition, which carries no
  rule (research R7).
- **§E6 type names**: `IRuntimeSecretResolver`, `RuntimeSecretReference` and `RuntimeSecretResolution` use the
  `Runtime` qualifier to disambiguate from Secrets' `ISecretValueResolver` and `SecretReference` (R2 allows it);
  `ICredentialLiteralValidator` uses the `Validator` suffix for a findings-returning contract (R4);
  `IRuntimeFaultClassification` carries the same `Runtime` qualifier;
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
  `Contracts/IRuntimeFaultClassification.cs`, `Contracts/IRuntimeSecretMask.cs`,
  new exceptions under `Exceptions/`, `Models/ValuePolicyCombiner.cs` (declaration and downgrade helper).
- `src/essentials/Workflows/Runtime/`: `Services/Values/RuntimeActivityInputMaterializer.cs` (withheld emission),
  `Services/Values/RuntimeExternalEnvelopeStorage.cs` (producer-side withholding),
  `Services/Checkpoints/RuntimeCheckpointCommitValidator.cs` (backstop),
  `Services/Executables/WorkflowExecutableHasher.cs` (binding format),
  `Services/Incidents/ActivityActivationFailureHandler.cs` (missing resolver),
  `Services/Incidents/DefaultRuntimeFaultCapturePolicy.cs` (masked exception type), the scoped mask implementation,
  and `Extensions/RuntimeCoreServiceCollectionExtensions.cs` (registrations).
- `src/essentials/Activities/Runtime/`: `Contracts/IActivityActivator.cs` (workflow execution id on the activation
  request), `Services/ActivityActivator.cs` (resolution and tenant check at activation),
  `Services/ActivityFaultIncidentRecorder.cs` (classification through `IRuntimeFaultClassification`),
  `Services/WorkflowInvokeActivitySchedulerWorkHandler.cs` (including the explicit `Withheld` case in
  `MaterializeCheckpointInputsAsync`), `Services/WorkflowResumeBookmarkSchedulerWorkHandler.cs`,
  `Services/StructuralParentEvaluationSupport.cs` (activation request, masking boundary),
  `Services/ActivityFaultProjection.cs` callers, `Services/ActivityExecutionInspection.cs` (withheld tolerance).
- `src/essentials/Activities/Runtime/Core/`: `Attributes/ActivityInputAttribute.cs` (flags), new
  `Attributes/RefusesSecretBindingAttribute.cs`, and `Models/ActivityContract.cs` (explicit
  `ActivityInputContract.IsCredential`).
- Built-in activities that gain `[RefusesSecretBinding]` (research R12) and literal readers that handle `SecretRead`
  explicitly (research R3a IP14 to IP19, IP22): `ForEach`, `For`, `DispatchWorkflow` and `DispatchPinSource`,
  `PublishEvent`, `Event` and `EventTriggerStimulusProvider`, `Delay`, `Cron`, `Timer` and `SchedulingNodeInputs`,
  `HttpEndpoint` and `HttpEndpointTriggerStimulusProvider`, `BpmnProcess` and `BpmnStartTriggerNodeInputs`, and
  `Inline`, `WriteHttpResponse`, `BpmnDecision` and `Fault`.
- `src/essentials/Activities/Http/Activities/SendHttpRequest.cs` (credential input `Authorization`, research R17) and,
  only if T106 finds the header value in logs, `src/essentials/Activities/Http/ActivitiesHttpFeature.cs` (as built, T106 found nothing in the rendered log text, so it is unchanged).
- `src/essentials/Activities/Design/Core/Models/InputDefinition.cs`,
  `src/essentials/Activities/Design/Reconciliation/Clr/Services/ClrAssemblyScanner.cs` (flags; refuses an unbindable
  credential declaration), `src/essentials/Activities/Design/Api/Handlers/AddDefinitionCommandHandler.cs` and
  `AddVersionCommandHandler.cs` (refuse `isCredential`),
  `src/essentials/Activities/Design/Api/Models/ActivityAuthoringCatalogView.cs`,
  `src/essentials/Activities/Design/Api/Services/ActivityAuthoringCatalogReader.cs`,
  `src/essentials/Activities/Design/Api/Services/IntrinsicAuthoringDescriptorProvider.cs`.
- `src/essentials/Workflows/Publishing/Services/RuntimeInputBindingCompiler.cs` (`SecretRead`, credential rule,
  `VF-ACT-011`, conversion plan from text) and `Services/ExecutableNodeCompiler.cs` (policy from declaration, pinned
  `IsCredential`, `VF-ACT-012` including `[RefusesSecretBinding]`, `[ActivityValueOutcomes]` inputs and variable
  defaults).
- `src/essentials/Workflows/Design/Core/` (predicate and rule id), `Validations/Core/` (contract, exception,
  `WorkflowStateAdmission`), `Validations/` (validator, registration as contract and `IDraftValidator`, shared
  `IsBound`).
- `src/essentials/Workflows/Design/Api/`: the six admission callers under `Endpoints/` (Definitions/Add,
  Drafts/Replace, Definitions/Update handler, Versions/Add, Definitions/Submit, Drafts/Promote),
  `Endpoints/WorkflowDesignExceptionTranslator.cs` (400 mapping for the credential-literal refusal, 409 mapping for
  `WorkflowDraftChangedException`), `WorkflowsDesignApiFeature.cs` (`DependsOn`).
- `src/essentials/Workflows/Design/Reconciliation/Services/WorkflowsVersionReconciler.cs` (per-item admission),
  `Reconciliation/Json/JsonWorkflowReconciliationFeature.cs` and
  `Reconciliation/Git/WorkflowsDesignGitReconciliationFeature.cs` (`DependsOn`),
  `Reconciliation/Git/Services/GitWorkflowExporter.cs` (per-version admission).
- Under `src/essentials/Workflows/Design/Persistence/` only promote's content precondition changes:
  `Core/Contracts/IPromoteDraftToVersionCommand.cs` (one `Execute` method with a required expected-state hash), a new
  `Core/Exceptions/WorkflowDraftChangedException.cs`, a new `Core/Models/WorkflowDraftStateHash.cs`, and `EfPromoteDraftToVersionCommand` in
  `EntityFrameworkCore/Commands/EfWorkflowDesignCommands.cs` (in-lock comparison). No rule and no reference
  (research R7).
- `src/essentials/Workflows/Runtime/Api/Services/WorkflowExecutableInspector.cs`, the run-inspector views under
  `src/essentials/Workflows/Runtime/Api/`, and
  `src/essentials/Workflows/ExecutionEvidence/Services/ExecutionEvidenceCheckpointEnricher.cs` (withheld rendering).
- `src/essentials/Secrets/Elsa.Secrets.csproj` (add `Workflows/**/*` to the sibling `Compile Remove` globs, or the
  bridge's sources compile into `Elsa.Secrets` twice) and `src/essentials/Secrets/EXTENSION_POINTS.md`.
- Shells files that enable `Secrets`: `src/apps/Elsa.Workbench/shells.json`, `shells.baseline.json`, and the compose
  shells file (`docker/compose/elsa-workbench.shells.json`) when it enables Secrets.

New projects:

- `src/essentials/Secrets/Workflows/Elsa.Secrets.Workflows.csproj` (bridge, feature, README).
- `tests/essentials/Secrets/Workflows/Tests/Elsa.Secrets.Workflows.Tests.csproj` (bridge tests, integration, tenant
  agreement, canary).

#### Project reference changes

Every `ProjectReference` this plan adds or removes, so the §2.1 claim is checkable. A slice that touches any of them
refreshes maps (`dotnet run --project tools/maps/Elsa.Maps.Generator -c Release -- all`), updates the affected
`packages.lock.json` files, and stages `docs/maps/manifest.json` when it changes.

| Project | Change | Target layer | Slice |
|---|---|---|---|
| `src/essentials/Secrets/Workflows/Elsa.Secrets.Workflows.csproj` (new) | references `Elsa.Secrets.Core` and `Elsa.Workflows.Runtime.Core`, nothing else | both `.Core` | 4 |
| `src/apps/Elsa.Workbench/Elsa.Workbench.csproj` | adds `Elsa.Secrets.Workflows` (host composition) | app host | 4 |
| `tests/essentials/Secrets/Workflows/Tests/Elsa.Secrets.Workflows.Tests.csproj` (new) | slice 4: the bridge, `Elsa.Secrets`, `Elsa.Secrets.Core`, `Elsa.Workflows.Runtime`, `Elsa.Activities.Runtime`, `Elsa.Workflows.Publishing`, the runtime EF persistence project, test support. Slice 9 adds `Elsa.Workflows.Design.Api`, `Elsa.Workflows.Design.Validations`, the design EF persistence project, `Elsa.Workflows.Design.Reconciliation.Git`, `Elsa.Workflows.Runtime.Api` and `Elsa.Workflows.ExecutionEvidence`. Slice 11 adds `Elsa.Activities.Http`, and `Elsa.Http` and `Elsa.Workflows.Runtime.Http`, because a shell composes the `Http` and `WorkflowsRuntimeHttp` features `ActivitiesHttp` depends on only from their assemblies. The exact set is confirmed against the composed features when each slice is built | test project | 4, 9, 11 |
| `tests/essentials/Activities/Design/Tests/ClrFixture/Elsa.Activities.Design.Tests.ClrFixture.csproj` | adds `Elsa.Workflows.Runtime.Core`, for the fixture with a credential input on an `IRuntimeActivityCheckpointParticipant` (found in slice 5) | test fixture | 5 |
| `src/essentials/Workflows/Design/Reconciliation/Elsa.Workflows.Design.Reconciliation.csproj` | adds `Elsa.Workflows.Design.Validations.Core` (the reconciler admits each item) | `.Core` contract | 6 |
| `src/essentials/Workflows/Design/Reconciliation/Git/Elsa.Workflows.Design.Reconciliation.Git.csproj` | adds `Elsa.Workflows.Design.Validations.Core` explicitly (the exporter admits each version), although it would also arrive transitively through the reconciliation project | `.Core` contract | 6 |
| `src/essentials/Workflows/Design/Api/Elsa.Workflows.Design.Api.csproj` | none: already references `Elsa.Workflows.Design.Validations.Core` (verified) | | 6 |
| `src/essentials/Workflows/Design/Persistence/EntityFrameworkCore/Elsa.Workflows.Design.Persistence.EntityFrameworkCore.csproj` | none (the first draft of this plan added a rule here; the redesign removes it) | | |
| `src/essentials/Workflows/Publishing/Elsa.Workflows.Publishing.csproj` | none: already references `Elsa.Workflows.Design.Core`, `Elsa.Workflows.Design.Validations` and `Elsa.Workflows.Runtime.Core` (verified) | | |
| `src/essentials/Activities/Runtime/Elsa.Activities.Runtime.csproj` | none: already references `Elsa.Workflows.Runtime.Core` (verified) | | |
| `tests/essentials/Architecture/Elsa.Architecture.Tests.csproj` | none expected: the coverage guard (T055) scans `src/` as source and reflects over `Elsa.Workflows.Design.Persistence.Core`, which arrives through the existing `Elsa.Workflows.Design.Api` reference; if the build shows otherwise, the slice adds the reference and refreshes maps | | 6 |

Separate repository (`elsa-foundation-studio`), slice 10 only: see
[the Studio contract](contracts/studio-input-descriptor.md).

**Structure Decision**: One new production project (the bridge) and one new test project. Everything else extends
existing owners. The bridge exists so the runtime stays free of Secrets types (R1); the test project exists because
the canary composes design, publishing, runtime and Secrets together, which no existing test host does (R10).

## Implementation sequence

1. Slice 1 (housekeeping) at any time.
2. Slice 2 introduces the runtime value model, the `SecretRead` compile path, the withheld snapshot and the publish
   refusals for activity kinds and inputs that cannot resolve at the point of use (`VF-ACT-012`, including the
   `[RefusesSecretBinding]` attribute on built-ins) or whose type has no conversion plan from text (`VF-COER-001`). Activation refuses withheld inputs loudly until slice 3, so `main` never resolves a secret
   into the snapshot and never hydrates a withheld input as null (R3, R3a, R16).
3. Slice 3 adds activation-time resolution and failure semantics behind a fake resolver.
4. Slice 4 adds the bridge, composition and the two-tenant and lifecycle integration. Secrets become usable here.
5. Slice 5 (declaration, policy and the `VF-ACT-011` encryption-required binding rule) needs slice 2: its scanner
   refuses a credential declaration on an input named by slice 2's `[RefusesSecretBinding]`, and it edits
   `RuntimeInputBindingCompiler.cs` and `ExecutableNodeCompiler.cs`, which slice 2 also edits. It is independent of
   slices 3 and 4.
6. Slice 6 (the rule, application layer only) needs slice 5's `IsCredential`.
7. Slice 7 (withholding backstop and surfaces) needs slices 2, 3 and 5; slice 8 (masking) needs slice 3.
8. Slice 9 (canary) needs slices 2 to 8.
9. Slice 10 (Studio) needs slice 5's wire fields on `main`; it can run in parallel with slices 6 to 9.
10. Slice 11 (`SendHttpRequest` consumes a secret, owner decision) needs slice 9: it reuses the canary host and the
   encoded scanner, and through slice 9 it has resolution, the bridge and the credential declaration.

## Validation and delivery

Each slice runs its focused suites and the architecture guard, refreshes maps when it touches project references,
and records its bite-proof in the PR (see [quickstart](quickstart.md#bite-proof-procedure-every-behavioral-claim)).
The canary PR records the per-protection table (A15). The PR that merges the last backend slice sets the spec to
`Implemented`. Status changes and claims are posted on each slice's issue. The specification PR itself changes
documents only: `git diff --check`, cited-path verification and the maps check.

## Complexity Tracking

No constitutional violation needs justification. Three choices add moving parts on purpose and are recorded here so a
reviewer can challenge them:

| Choice | Why needed | Simpler alternative rejected because |
|---|---|---|
| New `Elsa.Secrets.Workflows` project | keeps Secrets types and failure classification out of the runtime contract (R1) | a direct Runtime to `Elsa.Secrets.Core` reference is viable and is the named fallback; it was not chosen because it moves Secrets vocabulary into the runtime |
| A new `ValuePresence.Withheld` | the persisted snapshot needs a value-free shape that every consumer must handle explicitly (R3) | reusing external references conflates runtime payload storage with secrets and has no tenant parameter |
| Four application-layer integration points for one rule (five as built in slice 6's review, the Elsa 3 collection importer being the fifth), one shared admission helper, and a coverage guard that asserts every state-writing caller takes the helper | business rules stay out of persistence (R7), and promote must not depend on the promotion command's optional publisher | a guarded writer inside the EF commands is a single choke point but puts the rule in persistence; decorators over the command contracts conflict with the design backend's exclusive ownership of those contracts; the in-lock promotion gate runs only when `IInlineEventPublisher` is composed |
