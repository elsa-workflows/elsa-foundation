# Tasks: Workflow secret safety

**Input**: [plan](plan.md), [spec](spec.md), [research](research.md), [data model](data-model.md),
[contracts](contracts/) and [quickstart](quickstart.md).
**Status**: Planned implementation tasks; all unchecked. Issue #2211 delivers this plan only. The tasks are grouped
by user story below and regrouped into ten independently mergeable PRs in [Delivery slices](#delivery-slices), each
to be filed as its own issue.
**Tests**: Required. The spec defines independent tests for every story and a bite-proofed canary (FR-013,
FR-014), and framework §2.23 requires branch-covered tests per implementation. Each test task is written before the
implementation task that satisfies it and must fail on an assertion (not only on compilation) first. A test task
may depend on a contract-only task in the same slice so that it compiles. Every behavioral claim gets the revert or
mutation named in [the acceptance matrix](contracts/acceptance-proof-matrix.md).

## Format: `[ID] [P?] [Story] Description`

- **[P]**: can run in parallel (different files, no dependency on an incomplete task).
- **[Story]**: US1 to US5 from the spec.
- Paths are repository-relative. Slice 10 paths are in `elsa-workflows/elsa-foundation-studio`.

## Phase 1: Setup

- [ ] T001 At the start of every slice: read the slice issue's comments and the open PRs that reference it, post the claim (worktree and exact scope) per `docs/agents/issue-tracker.md`, branch from current `main`, and re-check for competing work before pushing. Push to `origin`, never to `main`; open the PR ready for review.
- [ ] T002 [P] Correct `ISecretResolver` to `ISecretValueResolver` and `src/Elsa/Secrets` to `src/essentials/Secrets` in `specs/079-secrets-module/plan.md`, `specs/079-secrets-module/research.md` and `specs/079-secrets-module/tasks.md` (FR-018).
- [ ] T003 [P] In `specs/079-secrets-module/contracts/runtime-contract.md`, rename the interface to `ISecretValueResolver` with its real signature `ResolveAsync(string tenantId, SecretReference reference, CancellationToken)`, and add one line to the "Secret Expression" section pointing to `specs/188-workflow-secret-safety/` for the runtime mechanism (FR-018).

## Phase 2: Foundational (runtime value model; blocks US1, US2 and US4)

- [ ] T004 [P] Write failing model tests in `tests/essentials/Workflows/Runtime/Tests/RuntimeSecretBindingModelTests.cs`: a `SecretRead` binding requires exactly the `Secret` payload (any other payload, or none, throws); a withheld envelope rejects inline, external and transient payloads, requires `Secret` for kind `SecretReference` and forbids it for `PolicyRequiresEncryption`; both round-trip through the runtime artifact JSON options with `presence: "Withheld"` and `source: "SecretRead"` as strings.
- [ ] T005 Add `RuntimeSecretReference`, `RuntimeInputBindingSource.SecretRead` and the `Secret` payload (constructor, `ValidateCanonical`) in `src/essentials/Workflows/Runtime/Core/Models/RuntimeInputBinding.cs`.
- [ ] T006 Add `ValuePresence.Withheld`, `WithheldValue`, `WithheldValueKind` and `ValueEnvelope.Withheld(...)` with validation in `src/essentials/Workflows/Runtime/Core/Models/ValueEnvelope.cs`.
- [ ] T007 Audit every branch on `ValuePresence` and `RuntimeInputBindingSource` under `src/` (`grep -rn "ValuePresence\.\|RuntimeInputBindingSource\."`). Make each handle the new member explicitly: pass through where no value is read, throw a fixed error where a value is required. This includes `src/essentials/Workflows/Runtime/Services/Executables/WorkflowExecutableHasher.cs` (format `SecretRead` by reference, never by value), `src/essentials/Workflows/Runtime/Resolvers/RuntimeInputBindingResolver.cs`, and `MaterializeCheckpointInputsAsync` in `src/essentials/Activities/Runtime/Services/WorkflowInvokeActivitySchedulerWorkHandler.cs`, which gets an explicit `Withheld` case throwing a fixed `VF-ACT-010` message instead of falling to "has an invalid value envelope" (research R3a, IP8). List every touched file in the PR.
- [ ] T008 Make `src/essentials/Activities/Runtime/Services/ActivityActivator.cs` refuse any withheld envelope with `VF-ACT-010` ("input was withheld and is not resolved in this host") before hydration, with a test in `tests/essentials/Activities/Runtime/Tests/ClrActivityActivatorTests.cs`, so no slice can hydrate null silently. Add a test in `tests/essentials/Activities/Runtime/Tests/WorkflowInvokeActivitySchedulerWorkHandlerTests.cs` that a checkpoint participant handed a withheld envelope faults with the fixed `VF-ACT-010` message from T007.
- [ ] T009 [P] Add a "Withheld value" entry to `docs/glossary/elsa.md`.

## Phase 3: User Story 1, use a stored secret in a workflow (P1)

**Goal**: A `Secret` binding resolves at CLR activation for the execution's tenant, re-resolves on every activation, never enters persisted state, and is refused at publish on every activity kind that reads inputs outside the activator (research R3a).
**Independent test**: Run a secret-bound test activity, rotate, suspend and resume, and run two tenants; the activity always sees its own tenant's current value, and persisted rows hold only the withheld envelope (A01 to A04).

### Compile and persist the reference (slice 2)

- [ ] T010 [P] [US1] Write failing compiler tests in `tests/essentials/Workflows/Publishing/Api/Tests/SecretBindingCompilationTests.cs`: `Secret` compiles to `SecretRead`, never to `Expression`; `{name}` is required and `typeName` and `scope` are optional; a non-object payload or a blank name is refused with a fixed message that does not echo the payload; a string-to-target plan is compiled; a target with no plan from `string` is refused (research R11); with a fake `IRuntimeSecretTypeDomains`, an `rsa-key` or `x509-certificate` reference on an integer or collection input is refused with `VF-ACT-013`, the same reference on a single string input is accepted, a `text` reference on an integer input is accepted, and a reference without `typeName` or a host without the domains contract is accepted; the effective policy includes `IsSensitive` and `RequiresEncryption`; a `Secret` binding on an intrinsic node, on a graph activity node (consumer `elsa.graph-activity`) and on a CLR activity type implementing `IRuntimeActivityCheckpointParticipant` is refused with `VF-ACT-012` (R12, R3a IP7, IP8, IP11).
- [ ] T011 [P] [US1] Write failing materializer tests in `tests/essentials/Workflows/Runtime/Tests/RuntimeStartActivityStateTests.cs`: a `SecretRead` input materializes as a withheld envelope of kind `SecretReference` carrying the reference and plan, and the serialized snapshot contains no value.
- [ ] T012 [P] [US1] Write a failing inspection test in `tests/essentials/Activities/Runtime/Tests/ActivityExecutionInspectionOutcomeTests.cs`: a withheld input renders as sensitive with the reference name and no value, without throwing.
- [ ] T092 [P] [US1] Add the optional replacement contract `IRuntimeSecretTypeDomains` and `RuntimeSecretValueDomain { Unknown, Text, StructuredText }` in `src/essentials/Workflows/Runtime/Core/Contracts/IRuntimeSecretTypeDomains.cs` (research R11). Contract only; it precedes T010 so the compiler tests compile.
- [ ] T013 [US1] Add `CompileSecretInput` and dispatch it before `CompileExpressionInput` in `src/essentials/Workflows/Publishing/Services/RuntimeInputBindingCompiler.cs` (parse the payload, compile the plan with `ValueConversionPlanResolver`, apply the `VF-ACT-013` domain check through an optional `IRuntimeSecretTypeDomains`, add the policy minimum).
- [ ] T014 [US1] Refuse `Secret` bindings with `VF-ACT-012` in `src/essentials/Workflows/Publishing/Services/ExecutableNodeCompiler.cs`: in the intrinsic compile path, for descriptors whose consumer is not `elsa.clr-activity`, and for CLR types that implement `IRuntimeActivityCheckpointParticipant`.
- [ ] T015 [US1] Emit the withheld envelope for `SecretRead` in `src/essentials/Workflows/Runtime/Services/Values/RuntimeActivityInputMaterializer.cs`.
- [ ] T016 [US1] Render withheld inputs in `src/essentials/Activities/Runtime/Services/ActivityExecutionInspection.cs`.

### Resolve at activation (slice 3)

- [ ] T017 [P] [US1] Add `IRuntimeSecretResolver`, `RuntimeSecretResolutionRequest` and `RuntimeSecretResolution` in `src/essentials/Workflows/Runtime/Core/Contracts/IRuntimeSecretResolver.cs`, declared as a §2.6.2 replacement contract.
- [ ] T018 [US1] Write failing activator tests in `tests/essentials/Activities/Runtime/Tests/ClrActivityActivatorTests.cs`: a withheld secret is resolved through a fake `IRuntimeSecretResolver` with the tenant from `IWorkflowExecutionPartitionAccessor`; converted with the envelope's plan; hydrated; `request.Inputs` is unchanged afterwards (no write-back); a global or across-scope context refuses before the resolver is called; an instance whose `TenantId` is set and differs from the partition refuses with `TenantMismatch` before the resolver is called, and a null `TenantId` resolves under the partition (research R2); a snapshot without withheld envelopes never calls the resolver and never reads the instance; two secret inputs resolve independently; a `PolicyRequiresEncryption` envelope still refuses with `VF-ACT-010`.
- [ ] T019 [US1] Write failing re-resolution tests in `tests/essentials/Activities/Runtime/Tests/PinnedInputRetryResumeTests.cs`: invoke delivers the value; the persisted activity state holds the withheld envelope; changing the fake's value while suspended and then resuming delivers the new value.
- [ ] T020 [US1] Resolve withheld secret envelopes at activation in `src/essentials/Activities/Runtime/Services/ActivityActivator.cs` (optional `IRuntimeSecretResolver`; `IWorkflowExecutionPartitionAccessor`; `IWorkflowExecutionStateStore` for the tenant check, read only when a withheld secret envelope is present; `IRuntimeValueConversionExecutor`), keeping T008's refusal for kind `PolicyRequiresEncryption`. Add the workflow execution id to `ActivityActivationRequest` in `src/essentials/Activities/Runtime/Contracts/IActivityActivator.cs` and set it in every caller: `WorkflowInvokeActivitySchedulerWorkHandler.cs`, `WorkflowResumeBookmarkSchedulerWorkHandler.cs` and both activations in `StructuralParentEvaluationSupport.cs` (all under `src/essentials/Activities/Runtime/Services/`). Update the registration in `src/essentials/Activities/Runtime/ActivitiesRuntimeFeature.cs` if the constructor needs it.
- [ ] T021 [P] [US1] Document the overridable `IRuntimeSecretResolver` in `src/essentials/Workflows/Runtime/EXTENSION_POINTS.md` and activation-time resolution in `src/essentials/Activities/Runtime/README.md`.
- [ ] T091 [US1] Write one failing test per input path of research R3a that slice 3 owns, in `tests/essentials/Activities/Runtime/Tests/SecretInputPathCoverageTests.cs` (support from `tests/essentials/Activities/Runtime/Tests/StructuralExecutionTestSupport.cs`): IP5 structural parent evaluation from the notify-parent and parent-completion handlers resolves through the activator; IP6 a test activity implementing `IRuntimeRematerializeInputsOnChildCompletion` (as `Do` and `While` do) with a secret-bound input re-materializes a withheld envelope after each child completes, resolves again, and never persists the fresh snapshot; IP9 a boundary retry clones only boundary inputs, calls neither the activator nor the resolver, and none of the cloned values is a withheld envelope; IP10 an operator reschedule copies the withheld envelope to the successor and the successor's invoke resolves. IP1 to IP4, IP7, IP8 and IP11 to IP13 are covered by T011, T012, T018, T019, T010, T008 and T067.

### Bridge to the Secrets module (slice 4)

- [ ] T022 [US1] Create `src/essentials/Secrets/Workflows/Elsa.Secrets.Workflows.csproj` (references `Elsa.Secrets.Core` and `Elsa.Workflows.Runtime.Core` only); add `Workflows/**/*` to the `Compile`, `EmbeddedResource` and `None` remove lists in `src/essentials/Secrets/Elsa.Secrets.csproj`; add the project to `Elsa.Server.slnx` and `Elsa.Server.Workbench.slnf`; create its `packages.lock.json` per `docs/reference/nuget-lock-files.md`.
- [ ] T023 [US1] Create `tests/essentials/Secrets/Workflows/Tests/Elsa.Secrets.Workflows.Tests.csproj` with its lock file, and add it to `Elsa.Server.slnx`.
- [ ] T024 [P] [US1] Write the failing feature registration test (§2.23.1) in `tests/essentials/Secrets/Workflows/Tests/SecretsWorkflowsFeatureRegistrationTests.cs`: `SecretsWorkflowsFeature` registers exactly one `IRuntimeSecretResolver`, resolving to `SecretValueRuntimeResolver`.
- [ ] T025 [P] [US1] Write failing bridge tests in `tests/essentials/Secrets/Workflows/Tests/SecretValueRuntimeResolverTests.cs`: tenant and reference (name, type, scope) pass through unchanged; a success maps the value; `IRuntimeSecretTypeDomains` maps `text` to `Text`, `rsa-key` and `x509-certificate` to `StructuredText`, and any other name to `Unknown`.
- [ ] T026 [US1] Write failing integration tests in `tests/essentials/Secrets/Workflows/Tests/SecretResolutionIntegrationTests.cs` (embedded SQLite runtime host, Secrets with the encrypted store, the bridge, a test activity; binding authored with `SecretExpressionTypes.Secret` and compiled through `RuntimeInputBindingCompiler`): the value is delivered and converted; rotation between runs takes effect with no republish (SC-002); rotation while suspended takes effect on resume; two tenants with the same secret name each get their own value (A04); a name-only reference resolves within the tenant; a secret deleted between publish and run faults with `NotFound`.
- [ ] T027 [US1] Implement `SecretValueRuntimeResolver`, the `IRuntimeSecretTypeDomains` implementation, and `SecretsWorkflowsFeature` (public, not sealed, virtual `ConfigureServices`, `DependsOn` `Secrets` and the activation feature, verified by name) in `src/essentials/Secrets/Workflows/`.
- [ ] T028 [US1] Compose the bridge in the Workbench: add a `ProjectReference` in `src/apps/Elsa.Workbench/Elsa.Workbench.csproj` (and update its lock file), and enable `SecretsWorkflows` in `src/apps/Elsa.Workbench/shells.json`, `src/apps/Elsa.Workbench/shells.baseline.json` and `docker/compose/elsa-workbench.shells.json`. Run the architecture suite (feature registration and shell visibility guards) and refresh maps.
- [ ] T029 [P] [US1] Write `src/essentials/Secrets/Workflows/README.md` (with a "Cross-domain contributions" section) and rewrite the "Runtime Integration" section of `src/essentials/Secrets/EXTENSION_POINTS.md` to the shipped mechanism, listing the bridge.
- [ ] T093 [US1] Write the tenant-agreement proof in `tests/essentials/Secrets/Workflows/Tests/TenantAgreementTests.cs` (research R2): in a two-tenant host, on every resolving path of research R3a (invoke, bookmark resume, structural parent evaluation, child-completion re-materialization and operator reschedule), the tenant handed to the resolver equals the instance's `TenantId`; an instance stored with a `TenantId` different from the partition it runs under faults with `TenantMismatch`, names only the reference and code, and never reaches the resolver.

**Checkpoint**: secrets are usable from workflows, and no resolved value is persisted.

## Phase 4: User Story 2, a failing secret fails the run clearly and safely (P1)

**Goal**: Every resolution failure faults with reference and code only, retryable only when transient, and keeps that classification when masked; a host without the bridge parks the activity instead of faulting it.
**Independent test**: One run per failure code, plus a conversion failure and a host without `SecretsWorkflows` (A05, A06).

### Runtime failure semantics (slice 3)

- [ ] T030 [P] [US2] Add `IRuntimeFaultClassification` (`IsRetryable`, `FailureCode`) in `src/essentials/Workflows/Runtime/Core/Contracts/`, and `RuntimeSecretResolutionException` (implementing it) and the missing-resolver exception in `src/essentials/Workflows/Runtime/Core/Exceptions/`.
- [ ] T031 [P] [US2] Write failing activator failure tests in `tests/essentials/Activities/Runtime/Tests/ClrActivityActivatorTests.cs`: a failed resolution throws `RuntimeSecretResolutionException` with name, code and retryable flag, and its message contains no value; a conversion failure reports `TypeMismatch`; no composed resolver throws the missing-resolver exception.
- [ ] T032 [P] [US2] Write failing tests in `tests/essentials/Activities/Runtime/Tests/ActivityFaultIncidentRecorderTests.cs` (the fault's `IsRetryable` and code come from any exception implementing `IRuntimeFaultClassification`, not from a concrete type; every other exception stays non-retryable) and in `tests/essentials/Workflows/Runtime/Tests/ActivityActivationFailureHandlerTests.cs` (the missing-resolver exception classifies as an activation failure with recovery metadata).
- [ ] T033 [US2] Implement the failure paths in `src/essentials/Activities/Runtime/Services/ActivityActivator.cs`.
- [ ] T034 [US2] Read `IsRetryable` and the failure code through `IRuntimeFaultClassification` in `src/essentials/Activities/Runtime/Services/ActivityFaultIncidentRecorder.cs`.
- [ ] T035 [US2] Classify the missing-resolver exception in `src/essentials/Workflows/Runtime/Services/Incidents/ActivityActivationFailureHandler.cs`.

### Secrets failure classification (slice 4)

- [ ] T036 [P] [US2] Write failing mapping tests in `tests/essentials/Secrets/Workflows/Tests/SecretValueRuntimeResolverTests.cs`: a theory over every `SecretResolutionFailureCode` member, including `None` on a failed result; retryable only for `StoreUnavailable`; a sentinel placed in `ResolvedSecret.Error` never appears in the result.
- [ ] T037 [US2] Write failing integration tests in `tests/essentials/Secrets/Workflows/Tests/SecretFailureIntegrationTests.cs`: revoked, expired, deleted, missing, type-mismatched and store-unavailable (failing `ISecretStore` double) secrets each fault naming the reference and code, with no value and the retryable flag per the contract table; a host without `SecretsWorkflows` leaves the activity waiting with an activation-failure incident (A06).
- [ ] T038 [US2] Implement the code mapping in `src/essentials/Secrets/Workflows/SecretValueRuntimeResolver.cs`.

**Checkpoint**: lifecycle operations on secrets have visible, predictable effects on runs.

## Phase 5: User Story 3, credentials cannot be typed into a workflow (P1)

**Goal**: Activities declare sensitive and credential inputs; authors cannot downgrade them; a literal or expression on a credential input is refused at all seven entry points by one application-layer rule; an encryption-required input accepts only a secret reference.
**Independent test**: A07, A08, A18 and the seven-row matrix A09.

### Declaration and effective policy (slice 5)

- [ ] T039 [P] [US3] Add test activities with a sensitive input and a credential input to `tests/essentials/Activities/Design/Tests/ClrFixture/`, and an invalid credential-with-default activity kept out of the default scan.
- [ ] T040 [P] [US3] Write failing scanner tests in `tests/essentials/Activities/Design/Tests/Unit/ClrAssemblyScannerTests.cs`: both flags are read; credential implies sensitive; credential with `DefaultValue` is refused naming the type and input; an undeclared activity's `InputDefinition` serializes without the new members and its `DefaultActivityDefinitionHasher` hash equals the pre-change golden value.
- [ ] T041 [P] [US3] Write failing view tests in `tests/essentials/Activities/Design/Api/Tests/ActivityAuthoringCatalogTests.cs`: `isSensitive` and `isCredential` are on the wire, and both are false for undeclared and intrinsic descriptors.
- [ ] T042 [P] [US3] Write failing policy tests in `tests/essentials/Workflows/Runtime/Tests/ValueDurabilityPolicyTests.cs` (every row of the effective-policy table, including the `VF-ACT-005` downgrade refusals) and in `tests/essentials/Workflows/Publishing/Api/Tests/WorkflowExecutableCompilerTests.cs`: both compile paths agree; `BuildActivityContract` sets `ActivityInputContract.IsCredential` from the declaration, and an undeclared input's contract schema fingerprint equals the pre-change golden value; the pinned-contract path decides "credential" from `ActivityInputContract.IsCredential` only, so a non-credential pinned input whose policy requires encryption is never treated as a credential (research R5).
- [ ] T095 [P] [US3] Write failing tests for the encryption-required publish rule in `tests/essentials/Workflows/Publishing/Api/Tests/EncryptionRequiredBindingTests.cs` (research R8, FR-010): on a non-credential input whose effective policy requires encryption, through both `CompileAll` overloads, a literal, an object, a variable, a JavaScript expression and an unbound input whose pinned contract declares a default are each refused with `VF-ACT-011`, naming node and input and never echoing the value or expression text; a `Secret` reference and an unbound input without a default are accepted; an input whose policy does not require encryption accepts a literal.
- [ ] T043 [US3] Add `IsSensitive` and `IsCredential` to `src/essentials/Activities/Runtime/Core/Attributes/ActivityInputAttribute.cs`.
- [ ] T044 [US3] Add nullable `IsSensitive` and `IsCredential` to `src/essentials/Activities/Design/Core/Models/InputDefinition.cs`.
- [ ] T045 [US3] Read, normalize and validate the flags in `src/essentials/Activities/Design/Reconciliation/Clr/Services/ClrAssemblyScanner.cs`.
- [ ] T046 [US3] Project the flags in `src/essentials/Activities/Design/Api/Models/ActivityAuthoringCatalogView.cs`, `src/essentials/Activities/Design/Api/Services/ActivityAuthoringCatalogReader.cs` and `src/essentials/Activities/Design/Api/Services/IntrinsicAuthoringDescriptorProvider.cs`.
- [ ] T047 [US3] Add the declaration-and-downgrade helper to `src/essentials/Workflows/Runtime/Core/Models/ValuePolicyCombiner.cs` and call it from `ExecutableNodeCompiler.CompileActivityPolicy` and `RuntimeInputBindingCompiler.Compile` (`src/essentials/Workflows/Publishing/Services/`), including the pinned-contract path. Add the explicit `IsCredential` flag to `ActivityInputContract` in `src/essentials/Activities/Runtime/Core/Models/ActivityContract.cs` and set it in `ExecutableNodeCompiler.BuildActivityContract`; nothing infers it from `RequiresEncryption`.
- [ ] T096 [US3] Implement the `VF-ACT-011` refusal in both `CompileAll` overloads of `src/essentials/Workflows/Publishing/Services/RuntimeInputBindingCompiler.cs`, after the effective policy is computed. Slice 6 later places the credential rule ahead of it (T060).
- [ ] T048 [P] [US3] Document declaring sensitive and credential inputs, and the encryption-required binding rule, in `src/essentials/Activities/Runtime/README.md`, and add a "Credential input" entry to `docs/glossary/elsa.md`.

### The credential-literal rule, application layer only (slice 6)

- [ ] T049 [P] [US3] Write failing predicate tests in `tests/essentials/Workflows/Design/Tests/Unit/CredentialInputBindingTests.cs` covering every row of the acceptance table, and keep `tests/essentials/Workflows/Design/Tests/Unit/BaselineValidatorTests/RequiredInputOutputValidatorTests.cs` green across the `IsBound` extraction.
- [ ] T050 [P] [US3] Write failing validator tests in `tests/essentials/Workflows/Design/Tests/Unit/BaselineValidatorTests/CredentialLiteralValidatorTests.cs`: nested activities are found; intrinsic and unresolvable nodes are skipped; path, type and message follow the contract, the message starts with `Inputs/CredentialLiteral`, and it never contains the bound literal; the same findings come back through `IDraftValidator.Validate` and through the throwing `EnsureNoCredentialLiteralsAsync`.
- [ ] T051 [P] [US3] Write a failing promotion-gate test in `tests/essentials/Workflows/Design/Persistence/EntityFrameworkCore/Tests/CredentialLiteralPromoteGateTests.cs`: a draft holding a credential literal, stored directly through `IUpdateDraftCommand` (as a host without admission would, and as a draft saved before its activity was installed would), is refused by `IPromoteDraftToVersionCommand` with `DraftHasValidationErrorsException` carrying `Inputs/CredentialLiteral`, and no version row is written. The EF command code is not changed by this slice; the test proves the existing gate plus the registered validator are enough.
- [ ] T052 [P] [US3] Write failing API tests in `tests/essentials/Workflows/Design/Api/Tests/CredentialLiteralEndpointTests.cs`: Definitions/Add, Drafts/Replace, Definitions/Update, Versions/Add and Definitions/Submit return 400 with errors keyed by `{nodeId}/inputs/{referenceKey}` and messages starting with the rule id, and store nothing (row counts unchanged, no row's `StateSource` contains the literal); Drafts/Promote of a draft holding a literal returns the existing 409 promotion-gate shape with the same key and message; a secret reference, an empty string and a sensitive non-credential literal are accepted everywhere.
- [ ] T053 [P] [US3] Write failing per-item tests (credential-literal contract, "Per-item behavior"). In `tests/essentials/Workflows/Design/Tests/Unit/Reconciliation/WorkflowsVersionReconcilerTests.cs`: a pass with one refused item and one valid item materializes nothing at all for the refused item (no definition record, no metadata update, no version row), materializes the valid item, drops the refused item's claim from `WorkflowVersionsReconciled`, completes without throwing, and logs one warning with the rule id, definition id, version, node id and input name and without the literal. In `tests/essentials/Workflows/Design/Tests/Unit/Reconciliation/Git/GitWorkflowExporterTests.cs`: a refused version gets no directory, file, commit or tag; the other versions and definitions are exported; the pass completes and logs the same value-free warning. A startup-task test asserts that neither `WorkflowsVersionReconcilerStartupTask` nor `GitWorkflowExportStartupTask` throws on a refused item.
- [ ] T054 [P] [US3] Write failing publish tests in `tests/essentials/Workflows/Publishing/Api/Tests/PublishWorkflowRequestHandlerTests.cs` and `tests/essentials/Workflows/Publishing/Api/Tests/WorkflowExecutableCompilerTests.cs`: literal, variable and JavaScript bindings on a credential input are refused with `Inputs/CredentialLiteral` (not `VF-ACT-011`, which the credential rule precedes); a secret reference is accepted.
- [ ] T055 [P] [US3] Write a failing coverage guard in `tests/essentials/Architecture/CredentialLiteralAdmissionCoverageTests.cs` (credential-literal contract, "Coverage guard"): every public interface in `Elsa.Workflows.Design.Persistence.Core.Contracts` that accepts `WorkflowDefinitionState`, `WorkflowDefinitionDraft`, `WorkflowDefinitionVersion` or `UpdateDraftRequest` is classified as admitted or exempt-with-reason; a source scan of `src/` finds every non-persistence constructor that takes an admitted contract and asserts it also takes `ICredentialLiteralValidator`, and fails if it finds fewer than the six known callers; every `IGitWorkflowExporter` implementation takes the validator; `CredentialLiteralValidator` is registered as an `IDraftValidator`. Mutation-test it three ways: drop the validator from one Design API caller, add an admitted-contract caller without it, and add an unclassified state-carrying contract; each must turn it red.
- [ ] T056 [US3] Add the acceptance predicate, the rule id `Inputs/CredentialLiteral` and the shared `IsBound` in `src/essentials/Workflows/Design/Core/Models/CredentialInputBinding.cs`; make `src/essentials/Workflows/Design/Validations/Validators/RequiredInputOutputValidator.cs` use the shared `IsBound`.
- [ ] T057 [US3] Add `ICredentialLiteralValidator`, `CredentialLiteralRefusedException` and the throwing extension `EnsureNoCredentialLiteralsAsync` in `src/essentials/Workflows/Design/Validations/Core/`.
- [ ] T058 [US3] Implement `CredentialLiteralValidator` in `src/essentials/Workflows/Design/Validations/Validators/` and register it in `src/essentials/Workflows/Design/Validations/WorkflowDesignValidationsFeature.cs` both as `ICredentialLiteralValidator` and as an `IDraftValidator` (the latter makes the existing promotion gate refuse).
- [ ] T059 [US3] Call `EnsureNoCredentialLiteralsAsync` on the incoming state, before the command, in the five Design API callers: `src/essentials/Workflows/Design/Api/Endpoints/Definitions/Add/Endpoint.cs`, `src/essentials/Workflows/Design/Api/Endpoints/Drafts/Replace/Endpoint.cs`, `src/essentials/Workflows/Design/Api/Endpoints/Definitions/Update/Handler.cs`, `src/essentials/Workflows/Design/Api/Endpoints/Versions/Add/Endpoint.cs` and `src/essentials/Workflows/Design/Api/Endpoints/Definitions/Submit/Endpoint.cs`. No file under `src/essentials/Workflows/Design/Persistence/` changes.
- [ ] T097 [US3] Admit each item in `src/essentials/Workflows/Design/Reconciliation/Services/WorkflowsVersionReconciler.cs` before any catalog mutation for it (refuse that item only, drop its claim, log the value-free warning, continue). Add a `ProjectReference` to `Elsa.Workflows.Design.Validations.Core` in `src/essentials/Workflows/Design/Reconciliation/Elsa.Workflows.Design.Reconciliation.csproj`, update its lock file, and refresh maps.
- [ ] T060 [US3] Apply the predicate per input in both `CompileAll` overloads of `src/essentials/Workflows/Publishing/Services/RuntimeInputBindingCompiler.cs`, ahead of slice 5's `VF-ACT-011` check, throwing `CredentialLiteralRefusedException`.
- [ ] T061 [US3] Admit each version before writing its file in `src/essentials/Workflows/Design/Reconciliation/Git/Services/GitWorkflowExporter.cs` (skip that version only, log the value-free warning, continue). Add a `ProjectReference` to `Elsa.Workflows.Design.Validations.Core` in `src/essentials/Workflows/Design/Reconciliation/Git/Elsa.Workflows.Design.Reconciliation.Git.csproj`, update its lock file, and refresh maps.
- [ ] T062 [US3] Map `CredentialLiteralRefusedException` to 400 in `src/essentials/Workflows/Design/Api/Endpoints/WorkflowDesignExceptionTranslator.cs`, and make the publish path surface it as 400 with the rule id through `src/essentials/Workflows/Publishing/Api/Endpoints/WorkflowPublishingExceptionTranslator.cs`. Promote keeps the existing `DraftHasValidationErrorsException` mapping.
- [ ] T063 [US3] Declare `DependsOn` `WorkflowDesignValidations` in `src/essentials/Workflows/Design/Api/WorkflowsDesignApiFeature.cs`, `src/essentials/Workflows/Design/Reconciliation/Json/JsonWorkflowReconciliationFeature.cs` and `src/essentials/Workflows/Design/Reconciliation/Git/WorkflowsDesignGitReconciliationFeature.cs`. The persistence features declare nothing new. Compose the validator (or a catalog-backed double) in every test host that constructs a Design API caller, the reconciler or the exporter, starting with `tests/essentials/Workflows/Design/Tests/Infrastructure/WorkflowsDesignTestHost.cs` and finding the rest by grep.
- [ ] T064 [P] [US3] Document the rule and its five integration points in `src/essentials/Workflows/Design/Validations/README.md` and `src/essentials/Workflows/Design/Validations/EXTENSION_POINTS.md`, the per-item reconciliation refusal in `src/essentials/Workflows/Design/Reconciliation/README.md`, and the per-version export refusal in `src/essentials/Workflows/Design/Reconciliation/Git/README.md`.

**Checkpoint**: 7 of 7 entry points refuse with one rule id (SC-003), and no persistence file changed.

## Phase 6: User Story 4, secret values never leak into stored or emitted output (P1)

**Goal**: Encryption-requiring values are withheld and refused at the commit backstop; every inspection surface renders the withheld marker; resolved values are masked in emitted text without changing a fault's classification; a canary proves it, with every protection bite-proofed.
**Independent test**: A10 to A15.

### Withholding backstop and surfaces (slice 7)

- [ ] T065 [P] [US4] Write failing backstop tests in `tests/essentials/Workflows/Runtime/Tests/RuntimeCheckpointCommitValidatorTests.cs`: a present inline or external envelope that requires encryption is refused in an activity state's snapshot and completion, in a durable value and in an inspection projection; a withheld envelope is accepted; the message names the state and key, not the value.
- [ ] T066 [P] [US4] Write failing producer tests in `tests/essentials/Workflows/Runtime/Tests/RuntimeExternalEnvelopeStorageWithholdingTests.cs`: a value whose effective policy requires encryption becomes a withheld envelope of kind `PolicyRequiresEncryption`; other values are unchanged.
- [ ] T067 [P] [US4] Write failing surface tests: `tests/essentials/Workflows/ExecutionEvidence/Tests/ExecutionEvidenceCheckpointEnricherTests.cs` (withheld disposition with the reference name, also with `RedactSensitiveValues = false`); `tests/essentials/Workflows/Runtime/Api/Tests/WorkflowExecutableInspectorTests.cs` (a `SecretRead` binding shows its reference); run-inspector tests in `tests/essentials/Workflows/Runtime/Api/Tests/` for the activity-execution, descendants and value-evidence payload reads (a withheld input shows the marker and reference, no value, no throw).
- [ ] T068 [US4] Add the backstop rule to `src/essentials/Workflows/Runtime/Services/Checkpoints/RuntimeCheckpointCommitValidator.cs`.
- [ ] T069 [US4] Add producer-side withholding to `src/essentials/Workflows/Runtime/Services/Values/RuntimeExternalEnvelopeStorage.cs`.
- [ ] T070 [US4] Render withheld values in `src/essentials/Workflows/ExecutionEvidence/Services/ExecutionEvidenceCheckpointEnricher.cs`, `src/essentials/Workflows/Runtime/Api/Services/WorkflowExecutableInspector.cs` and the run-inspector views under `src/essentials/Workflows/Runtime/Api/`.
- [ ] T071 [P] [US4] Document the commit backstop, `VF-ACT-010` as a backstop behind `VF-ACT-011`, and `VF-ACT-012` and `VF-ACT-013` in `src/essentials/Workflows/Runtime/README.md`.

### Masking (slice 8)

- [ ] T072 [P] [US4] Write failing mask tests in `tests/essentials/Workflows/Runtime/Tests/RuntimeSecretMaskTests.cs`: raw and JSON-escaped values are replaced by `[secret:<name>]`; empty values are ignored; registrations are isolated per activity execution id.
- [ ] T073 [P] [US4] Write failing boundary tests in `tests/essentials/Activities/Runtime/Tests/FaultIncidentExecutionTests.cs` and `tests/essentials/Activities/Runtime/Tests/WorkflowInvokeActivitySchedulerWorkHandlerTests.cs`: an activity that throws with the value yields a persisted fault and incident with the marker and the original exception type name, on the invoke, resume and structural paths; a returned `ActivityFault` with the value is masked; log lines captured with `tests/essentials/Testing/RecordingLogger.cs` show the marker; runtime spans carry no exception message.
- [ ] T094 [P] [US2] Write a failing classification-preservation test in `tests/essentials/Activities/Runtime/Tests/FaultIncidentExecutionTests.cs` (research R9: masking must not change a fault's classification): an activity with two secret inputs, where the first resolves (so its value is registered with the mask) and the second fails with `StoreUnavailable`; the recorded fault is retryable, carries code `StoreUnavailable` and type name `RuntimeSecretResolutionException`, and its text contains neither secret value. Bite-proof: make `SecretMaskedException` stop copying the classification; the retryable assertion goes red.
- [ ] T074 [US4] Add `IRuntimeSecretMask` in `src/essentials/Workflows/Runtime/Core/Contracts/`, `SecretMaskedException` (implementing `IRuntimeFaultClassification` and copying `IsRetryable` and `FailureCode` from the wrapped exception) in `src/essentials/Workflows/Runtime/Core/Exceptions/`, and the scoped implementation registered in `src/essentials/Workflows/Runtime/Extensions/RuntimeCoreServiceCollectionExtensions.cs`.
- [ ] T075 [US4] Register resolved values with the mask in `src/essentials/Activities/Runtime/Services/ActivityActivator.cs`.
- [ ] T076 [US4] Replace exceptions from activation and activity code with `SecretMaskedException` at the fault boundaries of `src/essentials/Activities/Runtime/Services/WorkflowInvokeActivitySchedulerWorkHandler.cs`, `src/essentials/Activities/Runtime/Services/WorkflowResumeBookmarkSchedulerWorkHandler.cs` and `src/essentials/Activities/Runtime/Services/StructuralParentEvaluationSupport.cs`, preserving the classification; mask `ActivityFault.Message` before `ActivityFaultProjection.ToNormalized` at its call sites; report the original type name in `src/essentials/Workflows/Runtime/Services/Incidents/DefaultRuntimeFaultCapturePolicy.cs`.
- [ ] T077 [P] [US4] Document the overridable `IRuntimeSecretMask` and `IRuntimeFaultClassification` in `src/essentials/Workflows/Runtime/EXTENSION_POINTS.md`.

### Canary (slice 9)

- [ ] T078 [US4] Add the canary activities in `tests/essentials/Secrets/Workflows/Tests/Support/`: `CanaryCredentialActivity.cs` (a credential input and a non-secret companion input; modes record-and-complete, throw-with-value, return-fault-with-value, suspend, throw-with-value-on-resume) and `CanaryStructuralActivity.cs` (a structural activity whose child-completion callback throws with its resolved value). Neither may log, return or store the value in any other way (research R9 limits).
- [ ] T079 [US4] Build the canary host in `tests/essentials/Secrets/Workflows/Tests/Support/SecretsCanaryWorkflowHost.cs` (`IAsyncDisposable`, temp directories): a CShells web host on a test server, in a FastEndpoints host collection (pattern: `tests/essentials/Workflows/Runtime/Api/Tests/FastEndpointsHostCollection.cs`); design, validations and design EF on SQLite; publishing; runtime and runtime EF on SQLite; `WorkflowsRuntimeApi` (instance, activity-execution, descendants, value-evidence payload and incident reads, and the executable inspector); Secrets with the encrypted store; `SecretsWorkflows`; execution evidence; git export in the Writer role on a temp repository; a capturing logger provider for all categories; a `System.Diagnostics.ActivityListener` recording runtime spans (pattern: `tests/essentials/Workflows/Runtime/Tests/RuntimeEngineTracingTests.cs`). Save runtime diagnostics settings at level `DiagnosticSnapshot` before the runs, so `DefaultDiagnosticSnapshotFactory` actually runs. Add the project references listed in the plan's inventory, update the lock file and refresh maps.
- [ ] T080 [US4] Write the encoded scanner in `tests/essentials/Secrets/Workflows/Tests/Support/CanaryScanner.cs` with its own tests: it finds a planted value as raw UTF-8, JSON-escaped, and all three Base64 alignments of its UTF-8 and UTF-16LE encodings, in database files including `-wal`, and reports no false positive on a clean file.
- [ ] T098 [US4] Add the canary's test-only injection seams in `tests/essentials/Secrets/Workflows/Tests/Support/` (research R10, A15), each a DI replacement in the canary host only: an `IRuntimeActivityInputMaterializer` decorator that re-plants a value the real materializer withheld, with a chosen policy; an `IRuntimePayloadCapturePolicy` double that captures every payload; a decorator over the invoke scheduler work handler that throws a planted value after the handler returns; and a hand-built runtime artifact, imported without publish (research R13), that binds a literal to an input whose pinned policy requires encryption. No production code gains a switch.
- [ ] T081 [US4] Write `tests/essentials/Secrets/Workflows/Tests/WorkflowSecretCanaryTests.cs`: the four FR-013 run shapes (success, faulting exception, suspend and resume, rotation) plus the structural and throw-on-resume variants, then a scan of all eight surfaces at diagnostics level `DiagnosticSnapshot` and again at `Payload`. Before each surface's scan counts, assert its precondition from research R10 (non-empty, contains the run), so no surface passes vacuously. Assert the value appears zero times (SC-004) and, as a positive control, that the reference name or marker appears where expected. Add the injection scenarios of A15 (S5 to S9), each asserting its own positive control and excluding the surface its injection planted. Run in both discrete and fused start/invoke modes if the host can select them; otherwise record which mode ran.
- [ ] T082 [US4] Pin the duplicated `"Secret"` literal: assert that the compiler's constant and the `Elsa.Workflows.Design.Core` predicate's constant equal `SecretExpressionTypes.Secret`, in `tests/essentials/Secrets/Workflows/Tests/SecretExpressionTypeAgreementTests.cs`.
- [ ] T083 [US4] Run the per-protection bite-proof of A15, one protection at a time, with the restore procedure from the quickstart. Record in the PR body one row per A15 protection id (P1 to P9, M1 to M6): the mutation, the scenario, the surface or assertion that went red, and the restored green run. The PR table must list exactly A15's ids; a protection that stays green is reported as unguarded and blocks the slice (FR-014, SC-005).

**Checkpoint**: the guarantee is enforced by a test that fails when any single protection is removed, and the PR table proves zero protections are unguarded.

## Phase 7: User Story 5, Studio shows sensitivity from the activity (P2, slice 10)

**Goal**: Studio reads the declaration, offers only the secret picker for credentials, and masks sensitive literals.
**Independent test**: A16 (vitest).

- [ ] T084 [P] [US5] Add `isSensitive?` and `isCredential?` to `StudioActivityInputDescriptor` in `src/apps/Elsa.Studio.Web/Client/src/sdk/index.ts` and in `src/extensions/Elsa.Studio.Secrets/Client/src/studio-sdk.d.ts`, `src/extensions/Elsa.Studio.ExpressionEditors.JavaScript/Client/src/studio-sdk.d.ts` and `src/extensions/Elsa.Studio.ExpressionEditors.Liquid/Client/src/studio-sdk.d.ts` (FR-015).
- [ ] T085 [P] [US5] Write failing tests in `src/apps/Elsa.Studio.Web/Client/src/__tests__/registry.test.ts` (the password editor resolves ahead of single-line for `uiHint: "password"` and for a sensitive non-credential text input) and in `src/apps/Elsa.Studio.Web/Client/src/__tests__/property-editors.test.tsx` (masked while typing; a stored value is never prefilled on reload).
- [ ] T086 [P] [US5] Write failing tests in `src/essentials/Elsa.Studio.Workflows/Client/src/__tests__/` for a credential input: default syntax `Secret`; the syntax picker offers only `Secret`; no literal editor renders; the "install Secrets" state appears when the descriptor is absent; a stored literal loads without showing its value.
- [ ] T087 [US5] Add the `studio.property.password` editor to `src/apps/Elsa.Studio.Web/Client/src/app/propertyEditors.tsx` (FR-017).
- [ ] T088 [US5] Implement credential handling in `src/essentials/Elsa.Studio.Workflows/Client/src/activityProperties.ts` and `src/essentials/Elsa.Studio.Workflows/Client/src/ActivityPropertiesPanel.tsx` (FR-016).

## Phase 8: Polish and cross-cutting concerns

- [ ] T089 In the PR that merges the last slice, run the architecture suite and `dotnet run --project tools/maps/Elsa.Maps.Generator -c Release -- check`, and set `specs/188-workflow-secret-safety/spec.md` to `Implemented`. If the Studio slice merges last, open a one-line follow-up in this repository for the status change.
- [ ] T090 File follow-up issues for what this plan deliberately leaves out, linking `specs/188-workflow-secret-safety/research.md`: a runtime artifact import check, which today skips `Inputs/CredentialLiteral`, `VF-ACT-011` and `VF-ACT-012` (R13); activity outputs, private state, bookmarks and activity loggers (phase 1 redacting value type); a retry policy that consumes `IsRetryable` (R4); the stale scanner sentence in constitution §E2.8 (R5); the tenantless `ISecretManager` signatures in spec 079's runtime contract.

## Dependencies and execution order

- Phase 1 has no dependencies; T002 and T003 can run at any time.
- Phase 2 blocks US1, US2 and US4.
- US1: T092 is contract-only and precedes T010. T010 to T012 precede T013 to T016. T017 is contract-only and precedes T018, T019 and T091 so they compile; T018, T019 and T091 precede T020. T022 and T023 scaffold the projects; T024 to T026 and T093 precede T027 and T028.
- US2: T030 precedes T031 and T032; they precede T033 to T035. T036 and T037 precede T038. US2's slice 3 work builds on T020; its slice 4 work builds on T027. T094 belongs to slice 8 and needs T030 and T074.
- US3: the declaration tasks T039 to T048, T095 and T096 precede the rule tasks T049 to T064 and T097, because the rule reads `IsCredential` and is ordered ahead of `VF-ACT-011`.
- US4: T065 to T071 need Phase 2, T020, and T047 (credential policy carries `RequiresEncryption`). T072 to T077 and T094 need T020 and T030. The canary T078 to T083 and T098 needs US1, US2, US3's rule, T096 (`VF-ACT-011`) and the rest of US4.
- US5 needs only T046's wire fields on `main`.
- Within each story: tests before implementation, models before services, services before endpoints.

### Parallel opportunities

- T002 and T003 alongside anything.
- Phase 2: T004 and T009 alongside T005 and T006.
- US1: T010, T011, T012 and T092 together; T017 alongside T013 to T016; T024, T025 and T093 together.
- US2: T030, T031 and T032 together once T017 exists.
- US3: T039 to T042 and T095 together; T049 to T055 together.
- US4: T065 to T067 together; T072, T073 and T094 together.
- US5 (separate repository) in parallel with US3's rule and US4 once T046 has merged.

```text
# Example: launch the US3 rule tests together
T049 predicate tests          tests/essentials/Workflows/Design/Tests/Unit/CredentialInputBindingTests.cs
T050 validator tests          tests/essentials/Workflows/Design/Tests/Unit/BaselineValidatorTests/CredentialLiteralValidatorTests.cs
T051 promotion-gate test      tests/essentials/Workflows/Design/Persistence/EntityFrameworkCore/Tests/CredentialLiteralPromoteGateTests.cs
T052 API admission tests      tests/essentials/Workflows/Design/Api/Tests/CredentialLiteralEndpointTests.cs
T053 per-item tests           tests/essentials/Workflows/Design/Tests/Unit/Reconciliation/
T054 publish tests            tests/essentials/Workflows/Publishing/Api/Tests/
T055 coverage guard           tests/essentials/Architecture/CredentialLiteralAdmissionCoverageTests.cs
```

## Implementation strategy

The smallest useful increment is slices 2 to 4: secrets become usable, and no resolved value is persisted from the
first merge. Slice 2 ships the binding, the withheld format and the publish refusals for activity kinds that read
inputs outside the activator, with activation refusing withheld inputs loudly, so `main` is never in a state where a
secret is resolved into the snapshot or a withheld input is hydrated as null. Slices 5 and 6 close the
paste-a-literal path. Slices 7 and 8 complete the leak guarantees, and slice 9 locks them in with the canary.
Studio (slice 10) follows the wire fields from slice 5.

## Delivery slices

Each slice is one PR and one GitHub issue. Every slice runs T001 first, follows the quickstart's working rules, and
records its bite-proof in the PR. "Maps" means the slice changes project references and must refresh maps, staging
`docs/maps/manifest.json` when it changes.

### Slice 1: Spec 079 housekeeping (FR-018)

- **Tasks**: T002, T003.
- **Depends on**: none. May fold into slice 4.
- **Touches**: `specs/079-secrets-module/` only.
- **Proved by**: `grep -rn "ISecretResolver\b\|src/Elsa/Secrets" specs/079-secrets-module` prints nothing.
- **Bite-proof**: restore one old occurrence; the grep finds it.

### Slice 2: Secret bindings compile and persist as withheld references (FR-001 format, FR-004, FR-011 by construction)

- **Tasks**: T004, T005, T006, T007, T008, T009, T010, T011, T012, T092, T013, T014, T015, T016.
- **Depends on**: none.
- **Touches**: `Elsa.Workflows.Runtime.Core` (binding and envelope models, `IRuntimeSecretTypeDomains`), `Elsa.Workflows.Runtime` (materializer, hasher, resolver pass-through), `Elsa.Workflows.Publishing` (compiler: `SecretRead`, `VF-ACT-012`, `VF-ACT-013`), `Elsa.Activities.Runtime` (inspection, activator refusal, checkpoint-participant refusal), glossary. No project reference changes.
- **Proved by**: `Elsa.Workflows.Runtime.Tests`, `Elsa.Workflows.Publishing.Api.Tests`, `Elsa.Activities.Runtime.Tests`, architecture suite.
- **Bite-proof**: make the compiler emit `Secret` as an `Expression` binding: T010 red. Remove the graph-consumer refusal: T010's `VF-ACT-012` graph row red. Remove the `VF-ACT-013` check: T010's `rsa-key` to integer row red. Make the materializer put a placeholder value inline: T011 red. Remove the activator's withheld refusal: T008's test red. Drop the explicit `Withheld` case in `MaterializeCheckpointInputsAsync`: T008's checkpoint-participant assertion red (generic message instead of `VF-ACT-010`).

### Slice 3: Activation-time resolution and failure semantics (FR-001, FR-002, FR-003, FR-004, FR-005)

- **Tasks**: T017, T018, T019, T091, T020, T021, T030, T031, T032, T033, T034, T035.
- **Depends on**: slice 2.
- **Touches**: `Elsa.Workflows.Runtime.Core` (`IRuntimeSecretResolver`, `IRuntimeFaultClassification`, exceptions), `Elsa.Activities.Runtime` (activator, activation request, its three callers, fault recorder), `Elsa.Workflows.Runtime` (activation-failure classification), Runtime and Activities.Runtime docs. Uses a fake resolver; no Secrets reference. No project reference changes.
- **Proved by**: `Elsa.Activities.Runtime.Tests`, `Elsa.Workflows.Runtime.Tests`.
- **Bite-proof**: replace the partition read with a constant tenant: T018's tenant assertion red. Remove the tenant-mismatch check: T018's `TenantMismatch` row red. Cache the resolved value per process: T019's resume assertion red. Skip resolution on the structural path's second activation: T091's IP6 row red. Hard-code `isRetryable: false` again: T032 red.

### Slice 4: Secrets bridge (FR-001, FR-003 classification, FR-005 end to end)

- **Tasks**: T022, T023, T024, T025, T026, T093, T027, T028, T029, T036, T037, T038.
- **Depends on**: slice 3.
- **Touches**: new `Elsa.Secrets.Workflows` and its test project; `Elsa.Secrets.csproj` remove globs; `Elsa.Server.slnx` and `Elsa.Server.Workbench.slnf`; `Elsa.Workbench.csproj` and its lock file; three shells files; Secrets docs. Maps.
- **Proved by**: `Elsa.Secrets.Workflows.Tests` (registration, mapping, type domains, integration, tenant agreement), `Elsa.Secrets.Tests`, architecture suite.
- **Bite-proof**: map `StoreUnavailable` as non-retryable: T036 red. Pass `ResolvedSecret.Error` into the result: T036's sentinel assertion red. Resolve under `"default"` instead of the partition: T026's two-tenant assertion red. Remove the activator's tenant-mismatch check: T093's mismatch case red. Map `rsa-key` to `Unknown`: T025's domain row red.

### Slice 5: Input sensitivity declaration, effective policy and the encryption-required rule (FR-006, FR-007, FR-010 publish rule)

- **Tasks**: T039, T040, T041, T042, T095, T043, T044, T045, T046, T047, T096, T048.
- **Depends on**: none functionally. It edits `RuntimeInputBindingCompiler.cs` and `ExecutableNodeCompiler.cs`, which slice 2 also edits, so it lands after slice 2 or rebases on it.
- **Touches**: `Elsa.Activities.Runtime.Core` (attribute, `ActivityInputContract.IsCredential`), `Elsa.Activities.Design.Core` (`InputDefinition`), `Elsa.Activities.Design.Reconciliation.Clr` (scanner), `Elsa.Activities.Design.Api` (view), `Elsa.Workflows.Runtime.Core` (`ValuePolicyCombiner`), `Elsa.Workflows.Publishing` (compilers, `VF-ACT-011`); test fixtures; docs and glossary. No project reference changes.
- **Proved by**: `Elsa.Activities.Design.Tests`, `Elsa.Activities.Design.Api.Tests`, `Elsa.Workflows.Runtime.Tests`, `Elsa.Workflows.Publishing.Api.Tests`.
- **Bite-proof**: write `false` instead of null for undeclared inputs: T040's golden-hash assertion red. Remove the downgrade check: T042's `VF-ACT-005` rows red. Drop credential's `RequiresEncryption`: T042's credential row red. Infer the pinned contract's credential flag from `RequiresEncryption`: T042's non-credential pinned-input row red. Remove the `VF-ACT-011` check: T095's literal and expression rows red.

### Slice 6: Credential-literal rule at seven entry points, application layer only (FR-008, FR-009)

- **Tasks**: T049, T050, T051, T052, T053, T054, T055, T056, T057, T058, T059, T097, T060, T061, T062, T063, T064.
- **Depends on**: slice 5.
- **Touches**: `Elsa.Workflows.Design.Core` (predicate), `Elsa.Workflows.Design.Validations.Core` and `Elsa.Workflows.Design.Validations` (contract, validator registered twice, shared `IsBound`), `Elsa.Workflows.Design.Api` (five admission calls, translator, `DependsOn`), `Elsa.Workflows.Design.Reconciliation` (per-item admission; new `ProjectReference` to `Elsa.Workflows.Design.Validations.Core`), `Elsa.Workflows.Design.Reconciliation.Json` (`DependsOn`), `Elsa.Workflows.Design.Reconciliation.Git` (per-version admission, `DependsOn`; new `ProjectReference` to `Elsa.Workflows.Design.Validations.Core`), `Elsa.Workflows.Publishing` (compiler refusal) and `Elsa.Workflows.Publishing.Api` (translator); design test hosts; docs. No file under `src/essentials/Workflows/Design/Persistence/` changes. Maps.
- **Proved by**: `Elsa.Workflows.Design.Tests`, `Elsa.Workflows.Design.Persistence.EntityFrameworkCore.Tests` (T051 only), `Elsa.Workflows.Design.Api.Tests`, `Elsa.Workflows.Publishing.Api.Tests`, architecture suite (T055).
- **Bite-proof**: seven separate reverts, one per entry point: remove the admission call from one Design API caller (draft save, add-version, submit each red in T052), unregister the `IDraftValidator` (promote red in T051 and T052), remove the reconciler's admission (T053 reconciliation red), remove the compiler predicate (T054 red), remove the exporter's admission (T053 export red). Make the reconciler throw instead of skipping: T053's pass-completes assertion red. T055's three mutations each red.

### Slice 7: Withholding backstop and leak surfaces (FR-010, FR-011)

- **Tasks**: T065, T066, T067, T068, T069, T070, T071.
- **Depends on**: slice 2 (withheld model), slice 3 (activator refusal for `PolicyRequiresEncryption`), slice 5 (credential policy carries `RequiresEncryption`).
- **Touches**: `Elsa.Workflows.Runtime` (commit validator, envelope storage, README), `Elsa.Workflows.ExecutionEvidence` (enricher), `Elsa.Workflows.Runtime.Api` (inspectors). No project reference changes.
- **Proved by**: `Elsa.Workflows.Runtime.Tests`, `Elsa.Workflows.ExecutionEvidence.Tests`, `Elsa.Workflows.Runtime.Api.Tests`.
- **Bite-proof**: remove the backstop rule: T065 red. Remove the producer withholding: T066 red. Drop the withheld branch from the enricher: T067's evidence assertion red. Drop it from the activity-execution view: T067's run-inspector assertion red.

### Slice 8: Masking resolved values in emitted text (FR-012, FR-003 under masking)

- **Tasks**: T072, T073, T094, T074, T075, T076, T077.
- **Depends on**: slice 3 (values are resolved in the activator; `IRuntimeFaultClassification`).
- **Touches**: `Elsa.Workflows.Runtime.Core` (mask contract, masked exception), `Elsa.Workflows.Runtime` (mask implementation, fault capture policy), `Elsa.Activities.Runtime` (activator registration, handler fault boundaries), Runtime docs. No project reference changes.
- **Proved by**: `Elsa.Workflows.Runtime.Tests`, `Elsa.Activities.Runtime.Tests`.
- **Bite-proof**: skip mask registration in the activator: T073's fault, incident and log assertions red. Skip `ActivityFault` masking only: T073's returned-fault assertion red. Stop copying the classification into `SecretMaskedException`: T094's retryable assertion red.

### Slice 9: Canary end to end with per-protection bite-proof (FR-013, FR-014)

- **Tasks**: T078, T079, T080, T098, T081, T082, T083.
- **Depends on**: slices 2, 3, 4, 5, 6, 7 and 8.
- **Touches**: `tests/essentials/Secrets/Workflows/Tests` only (support, injection seams, scanner, canary, constant pins), plus that project's references and lock file. Maps.
- **Proved by**: `Elsa.Secrets.Workflows.Tests` with `--filter "FullyQualifiedName~Canary"`.
- **Bite-proof**: the A15 table itself. Each of P1 to P9 and M1 to M6, disabled alone, turns T081 red on the scenario and surface A15 names; M1 to M4 must be caught in the runtime database through the UTF-16LE Base64 search, not only in logs. Each surface's precondition is checked by emptying that surface's source (for example, no evidence feature): the precondition assertion, not the zero-count scan, must go red.

### Slice 10: Studio (FR-015, FR-016, FR-017), in `elsa-foundation-studio`

- **Tasks**: T084, T085, T086, T087, T088.
- **Depends on**: slice 5 merged in this repository (wire fields `isSensitive` and `isCredential`).
- **Touches**: `@elsa-workflows/studio-web` (SDK type, property editors), `@elsa-workflows/studio-workflows` (credential handling), and the three `studio-sdk.d.ts` copies in the Secrets, JavaScript and Liquid extensions.
- **Proved by**: `pnpm --filter @elsa-workflows/studio-web test`, `pnpm --filter @elsa-workflows/studio-workflows test`, `pnpm typecheck`, `pnpm lint`.
- **Bite-proof**: remove the credential filter from the syntax picker: T086 red. Remove the password editor registration: T085 red.

### Not in a slice

- T001 runs at the start of every slice.
- T089 rides on whichever PR merges last; T090 is filed when slice 9 merges.
