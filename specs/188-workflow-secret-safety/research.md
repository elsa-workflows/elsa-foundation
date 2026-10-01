# Research and design decisions: Workflow secret safety

Baseline: `main` at `057adc44f` plus the specification commit `30dfd01ec`. Every "verified" statement below was
read in that tree; everything else is a proposed implementation shape, not existing code. Scope is phase 0 of
[the Connections and Secrets model](../../docs/plans/connections-and-secrets-model.md) (decisions D1 and D10 only).
Connections, authentication schemes, OAuth and external secret stores stay out. Plan review round 1 (2026-10-01)
re-read every code claim it changed (R2, R3, R3a, R7, R8, R10, R11, R12) in the same tree.

## R1: The runtime/Secrets seam

**Decision**: The workflow runtime owns a small replacement contract, `IRuntimeSecretResolver`, in
`Elsa.Workflows.Runtime.Core`, with runtime-owned shapes `RuntimeSecretReference(Name, TypeName?, Scope?)` and
`RuntimeSecretResolution` (succeeded value, or failure code plus retryable flag). A new cross-domain contribution
project, `Elsa.Secrets.Workflows` (`src/essentials/Secrets/Workflows/`), implements it over the existing
`ISecretValueResolver` (and the optional type-domain contract of R11) and ships a `SecretsWorkflows` shell feature
that depends on `Secrets`. The runtime never references a Secrets project; the bridge references only
`Elsa.Secrets.Core` and `Elsa.Workflows.Runtime.Core`.

**Rationale**:

- Verified: portable expressions run under capability profile `BindingPureV1`, which forbids service location
  (`src/essentials/Expressions/Core/Contracts/ExpressionEvaluationCapabilities.cs`), so a secret cannot be an
  expression handler.
- Verified: `Elsa.Workflows.Runtime.Core` references only Activities.Runtime.Core, Expressions.Core, Primitives
  and Serialization.Core. Keeping Secrets out keeps runtime-only hosts able to run without composing Secrets
  (constitution §E2.2.3 deployment shapes).
- The failure codes (`SecretResolutionFailureCode`, `src/essentials/Secrets/Core/Models/SecretModels.cs`) belong to
  Secrets. Their transient/permanent meaning (FR-003) is decided in the bridge that owns that vocabulary, not in
  the runtime.
- The shape has a precedent: `Elsa.Secrets.Nuplane` adapts `ISecretValueResolver` to Nuplane's
  `ISecretReferenceProvider` (`src/essentials/Secrets/Nuplane/SecretsFeedCredentialProvider.cs`). Framework §2.2's
  secondary-domain rule puts the model-owning domain first, so the name is `Elsa.Secrets.Workflows`.
- Runtime tests can use a fake resolver; phase 1 Connections can add a second implementer without a runtime change.

**Alternatives considered**:

- `Elsa.Workflows.Runtime` references `Elsa.Secrets.Core` directly. Framework §2.1 allows a cross-`.Core`
  reference, and it saves one project. Rejected as the default because the runtime's public binding contract would
  carry Secrets' reference type and enum, every runtime host would carry Secrets.Core, and the code classification
  would move into the runtime. It stays the fallback if reviewers reject a new project; slices 3 and 4 would then
  merge.
- A portable expression handler for `Secret`: forbidden by `BindingPureV1` (above).
- `Elsa.Secrets` references `Elsa.Workflows.Runtime.Core` and registers the implementation itself: the Secrets
  feature, which Nuplane feed credentials also use, would grow a workflow dependency and one branch per consumer
  (the reverse naming §2.2 calls an anti-pattern).

## R2: Where the tenant comes from

**Decision**: The tenant used to resolve a secret is the execution's partition,
`IWorkflowExecutionPartitionAccessor.Current` (`src/essentials/Workflows/Runtime/Core/Contracts/IWorkflowExecutionPartitionAccessor.cs`),
read once per activation in the activator and passed explicitly to `IRuntimeSecretResolver`. No binding, request
payload, setting or default can supply it.

Resolution fails closed when the two tenant sources disagree. When the input snapshot holds at least one withheld
secret envelope, the activator also reads the executing instance's `WorkflowExecutionState.TenantId`. When that
tenant is set and differs ordinally from the partition, the activator throws
`RuntimeSecretResolutionException(referenceName, "TenantMismatch", isRetryable: false)` before any resolver call.
The fault names the reference and the code only; it carries no value and no store detail. When `TenantId` is null,
the partition is used: it is the scope the instance's own rows are stored under, not a cross-tenant default. To read
the instance, `ActivityActivationRequest` (`src/essentials/Activities/Runtime/Contracts/IActivityActivator.cs`)
gains the workflow execution id, set by every caller (invoke, bookmark resume, and both activations in
`StructuralParentEvaluationSupport.ConstructActivityAsync`). The read happens only when a withheld secret envelope
is present, so activations without secrets pay nothing.

**Rationale (verified)**:

- `ScopedWorkflowExecutionCommandExecutor` opens one DI scope per execution command bound to
  `new PersistenceScope(envelope.Partition.Value)`
  (`src/essentials/Workflows/Runtime/Services/Executions/ScopedWorkflowExecutionCommandExecutor.cs`), and
  `PersistenceWorkflowExecutionPartitionAccessor` returns `RequireScope().Value`, which throws for global and
  across-scope contexts.
- The instance's own state rows are keyed by that scope (`EfWorkflowExecutionStateStore.CopyToEntity` writes
  `ScopeKey`), so it is the boundary that already isolates the instance's state.
- The runtime already treats the two identifiers as one: `InMemoryWorkflowExecutionStateStore` filters
  `state.TenantId` by `query.TenantPartition`. The mismatch check turns that assumption into a loud refusal instead
  of a silent cross-tenant read.
- Single-tenant hosts: `PersistenceScope.DefaultValue`, `WorkflowExecutionPartition.DefaultValue` and
  `AspNetCoreIdentityDefaults.DefaultTenantId` are all `"default"`, and the Secrets API reads its tenant from the
  same identity claim. So a secret created through the Secrets API in a single-tenant host resolves under the
  partition the runtime binds. No new tenant concept is introduced (spec assumption).

**Alternatives considered**:

- `WorkflowExecutionState.TenantId` alone: nullable, and the run inspector lets any trusted caller see an execution
  whose tenant is null (`HttpContextActivityExecutionInspectionAuthorizationContext`). Using it alone would need a
  fallback for null, which is exactly the cross-tenant default the constraints forbid. It is used here only as a
  cross-check that can refuse, never as the source.
- A tenant field on `RuntimeInputBindingResolutionContext` filled by the materializer: secrets are not resolved at
  materialization in this design (R3), so it would be a second, unused tenant source that could drift.
- The HTTP principal's tenant claim: absent on background drains, timers and recovery sweeps.

**Proof (replaces the earlier "assumption to prove")**: T093 runs a two-tenant host and, on every path in R3a that
resolves a secret (invoke, bookmark resume, structural parent evaluation, child-completion re-materialization and
operator reschedule), asserts that the tenant passed to the
resolver equals the instance's `TenantId`. A second case stores an instance whose `TenantId` differs from the
partition it runs under and asserts the `TenantMismatch` fault, with the resolver never called. Bite-proof: remove
the mismatch check; the second case goes red.

## R3: Resolution point and the withheld snapshot

**Decision**: A `Secret` binding compiles to a new `RuntimeInputBindingSource.SecretRead` carrying a
`RuntimeSecretReference` and a compiled string-to-target `ValueConversionPlan`. At Scheduled to Running, the
materializer emits a `ValueEnvelope` with a new presence, `Withheld`, holding the reference and the plan but no
value, and does not resolve anything. The secret is resolved in `ActivityActivator.ActivateAsync`
(`src/essentials/Activities/Runtime/Services/ActivityActivator.cs`), next to the existing just-in-time
`DereferenceInputsAsync`, converted, hydrated into the activity, registered with the per-activation mask (R9), and
never written back.

That branch runs only for activation strategies with `RequiresInputHydration = true`. Today that is the CLR
strategy (`ClrActivityActivator`); `GraphActivityActivationStrategy` returns `false` and the activator returns the
lease before hydration (verified). So the activator is the single resolution point for CLR activations only. It is
not the only place inputs are read: R3a enumerates every path and how each is handled.

**Rationale (verified)**:

- The input snapshot is committed with the Running transition before the activity body runs
  (`WorkflowStartActivitySchedulerWorkHandler.StartActivity`), and the invoke handler reads the committed
  `state.InputSnapshot` in a later work item (`WorkflowInvokeActivitySchedulerWorkHandler`). Anything resolved
  before that commit is persisted: this is the leak FR-011 closes.
- Every CLR activation passes the activator: invoke, bookmark resume (`WorkflowResumeBookmarkSchedulerWorkHandler`),
  and structural parent evaluation (`StructuralParentEvaluationSupport.ConstructActivityAsync`, called by the
  notify-parent and parent-completion handlers), including its second activation on a transient, re-materialized
  snapshot for `IRuntimeRematerializeInputsOnChildCompletion` activities. Resolving there re-resolves on every
  execution and resumption (FR-002) without per-handler code.
- The boundary retry does not pass the activator. `WorkflowRetryActivityBoundarySchedulerWorkHandler` clones the
  prior execution's boundary input durable values (which only `GraphActivityScope` writes) and enqueues a fresh
  `ScheduleActivity` work item; it never activates anything and never reads an input snapshot. The retried
  execution re-enters Scheduled to Running and materializes a new snapshot like any other; because only graph
  activities write boundary inputs, it is a graph activity, which cannot carry a secret binding (R12). An earlier
  draft of this plan said the retry boundary "re-activates from the immutable snapshot"; that was wrong.
- The persisted snapshot then holds the reference plus a withheld marker by construction, which is FR-011's shape
  for activity state, evidence and the inspector, independent of any redaction option (for example
  `ExecutionEvidenceOptions.RedactSensitiveValues`).

**Alternatives considered**:

- Resolve in the materializer and strip at the store: a business rule in persistence, and in discrete mode the
  invoke handler would read the stripped marker, so the activity would never see the value.
- Resolve in the materializer and strip in the commit validator: same discrete-mode loss, and fused and discrete
  modes would diverge.
- Encode the reference as a `DurableValueExternalReference` with a "secret" storage profile and reuse external
  dereferencing: `IExternalPayloadStore.ReadAsync` has no tenant parameter, and external references are also
  produced by `RuntimeExternalEnvelopeStorage` rewrites, so the two meanings would collide.
- Resolve in each handler before activation: four call sites instead of one, and still no answer for graph
  activities, whose inputs are persisted as boundary values (R3a, IP7).

## R3a: Every path that materializes or reads activity inputs

Read at `057adc44f`. Each path is either resolved at the point of use without persisting, or the binding is refused
at publish for that activity kind (as R12 already does for intrinsics). A test per path is task T091 unless another
task is named. Paths that only copy or render the withheld envelope never see a value.

| ID | Path (verified code) | How it reads inputs | Phase 0 handling | Test |
|---|---|---|---|---|
| IP1 | Scheduled to Running, discrete and fused: `WorkflowStartActivitySchedulerWorkHandler` through `RuntimeActivityInputSnapshotMaterializer` and `IRuntimeActivityInputMaterializer` | materializes and commits the snapshot | emits `Withheld` for `SecretRead`; never resolves | T011 |
| IP2 | Invoke and resume inspection: `ActivityExecutionInspection.BuildInputValueSnapshots`, called before activation | renders the committed snapshot into inspection projections | renders the withheld marker with the reference name; reads no value; must not throw | T012 |
| IP3 | Invoke activation: `WorkflowInvokeActivitySchedulerWorkHandler` to `IActivityActivator` (CLR, `RequiresInputHydration = true`) | hydrates the CLR activity from the snapshot | resolve at point of use in the activator; nothing written back | T018 |
| IP4 | Bookmark resume activation: `WorkflowResumeBookmarkSchedulerWorkHandler` to the activator | hydrates from the committed snapshot | resolve at point of use | T019 |
| IP5 | Structural parent evaluation: `StructuralParentEvaluationSupport.ConstructActivityAsync` from the notify-parent and parent-completion handlers | hydrates from the committed snapshot | resolve at point of use | T091 |
| IP6 | Child-completion re-materialization (issue #977; `Do`, `While` implement `IRuntimeRematerializeInputsOnChildCompletion`) | re-materializes a transient snapshot and activates again | the materializer emits `Withheld` again; the activator resolves; the fresh snapshot is never persisted (ADR 0045) | T091 |
| IP7 | Graph activities (consumer `elsa.graph-activity`; `GraphActivityActivationStrategy.RequiresInputHydration = false`) | `GraphActivity.PrepareEntryCheckpointAsync` receives inputs from the invoke handler and `GraphActivityScope.CaptureInputsAsync` persists them as boundary input durable values | refuse at publish: a `Secret` binding on a node whose descriptor consumer is not `elsa.clr-activity` fails with `VF-ACT-012` (R12) | T010 |
| IP8 | Checkpoint participants: `WorkflowInvokeActivitySchedulerWorkHandler.MaterializeCheckpointInputsAsync` for any activity implementing `IRuntimeActivityCheckpointParticipant` | copies inline values into a dictionary; a `Withheld` envelope falls to the default branch and throws "has an invalid value envelope" | refuse at publish: a `Secret` binding on a CLR activity type that implements `IRuntimeActivityCheckpointParticipant` fails with `VF-ACT-012`; backstop: an explicit `Withheld` case throws a fixed `VF-ACT-010` message instead of the generic one | T010, T091 |
| IP9 | Boundary retry: `WorkflowRetryActivityBoundarySchedulerWorkHandler` | clones boundary input durable values; enqueues a fresh `ScheduleActivity`; never calls the activator | nothing to resolve: boundary input durable values are written only by `GraphActivityScope`, so the retried boundary is a graph activity, which cannot carry a secret binding (IP7); the scheduled execution re-materializes through IP1 like any other | T091 |
| IP10 | Operator reschedule: `RescheduleActivityAlterationHandler` | copies the source's `InputSnapshot` into a Running successor | copies the withheld envelope (a reference, no value); the successor's invoke resolves through IP3 | T091 |
| IP11 | Engine intrinsics: `WorkflowIntrinsicExecutor` | evaluates bindings and writes durable variables and outputs | refuse at publish (R12, `VF-ACT-012`) | T010 |
| IP12 | Executable inspection and hashing: `WorkflowExecutableInspector`, `WorkflowExecutableHasher` | read bindings, not values | format `SecretRead` by reference | T007, T067 |
| IP13 | Evidence, run inspector and diagnostic snapshots: `ExecutionEvidenceCheckpointEnricher`, Runtime.Api activity-execution readers, `DefaultDiagnosticSnapshotFactory` | read committed state | render the withheld marker; read no value | T067 |

Fused mode (spec 123) commits the same Started stage and dispatches the invoke handler inline, so it takes IP1 then
IP2 and IP3 in one work item. A path added later that reads `ActivityInputSnapshot.Values` must handle
`ValuePresence.Withheld` explicitly (T007's audit rule) or this table is stale.

## R4: Failure semantics

**Decision**:

- Resolution failures fault the activity through a typed `RuntimeSecretResolutionException(referenceName,
  failureCode, isRetryable)` with the fixed message `Secret '<name>' could not be resolved (<code>).` The bridge
  maps `StoreUnavailable` to retryable and every other code to permanent, and discards `ResolvedSecret.Error`.
- A conversion failure at activation (`RuntimeValueConversionException`) is reported with code `TypeMismatch`.
- `ActivityFaultIncidentRecorder` hard-codes `isRetryable: false` for exception faults (verified). It reads the
  classification through a new runtime-owned contract, `IRuntimeFaultClassification` (`IsRetryable`,
  `FailureCode`), instead of through a concrete exception type. `RuntimeSecretResolutionException` implements it, and
  the masking wrapper of R9 copies it from the exception it wraps, so masking can never turn a transient
  `StoreUnavailable` into a permanent fault.
- A tenant disagreement (R2) is reported with code `TenantMismatch`, permanent.
- When no `IRuntimeSecretResolver` is composed, the activator raises an activation failure, not a fault: a new
  `ActivityActivationFailureKind` handled by `ActivityActivationFailureHandler`
  (`src/essentials/Workflows/Runtime/Services/Incidents/ActivityActivationFailureHandler.cs`), so the activity
  parks with an incident whose recovery is "compose `SecretsWorkflows`", like the existing missing storage driver
  case.

**Rationale**: Constitution §E2.6.1 separates domain gates, which may deny execution, from missing modules, which
must not destroy executability. A revoked secret is a domain gate. A host that forgot the bridge is a missing
module. `DefaultSecretValueResolver` copies a store exception's raw message into `ResolvedSecret.Error` for
`StoreUnavailable` (verified), which can carry store-private detail such as a connection target, so the error text
must not reach the fault.

**Finding to keep visible**: no runtime retry policy reads `IsRetryable` today (the only reader is
`ActivityFaultProjection`). Spec scenario US2.2 ("existing retry policies may retry") is therefore met by recording
`IsRetryable = true` on the fault for operators and incident strategies. An operator reschedule (R3a, IP10) copies the
withheld snapshot and re-resolves at the successor's activation. No automatic retry is added.

## R5: Declaring sensitive and credential inputs

**Decision**: `[ActivityInput]` gains two bool properties, `IsSensitive` and `IsCredential`; credential implies
sensitive. `InputDefinition` gains `bool? IsSensitive = null` and `bool? IsCredential = null`, set only when true.
`ClrAssemblyScanner` reads both by name (reflection-only, like `UIHint`), normalizes credential to also be
sensitive, and refuses a credential input that declares `DefaultValue`. `ActivityInputDescriptorView` gains
non-null `IsSensitive` and `IsCredential` so Studio and validators read them (FR-006).

Effective policy: sensitive sets `IsSensitive`; credential sets `IsSensitive` and `RequiresEncryption`. A
`SecretRead` binding always adds the minimum `{IsSensitive, RequiresEncryption}`, whatever the declaration.

Every place the credential rule decides reads the explicit credential flag; none infers it. The design-side
predicate reads `InputDefinition.IsCredential`. The pinned-contract compile path
(`RuntimeInputBindingCompiler.CompileAll(nodeId, IEnumerable<ActivityInputContract>, ...)`, used by
`ActivityTemplatePlacer`) has no `InputDefinition`, so `ActivityInputContract`
(`src/essentials/Activities/Runtime/Core/Models/ActivityContract.cs`) gains an explicit `IsCredential` flag, set by
`ExecutableNodeCompiler.BuildActivityContract` from the declaration. It must leave the schema fingerprint of every
undeclared input unchanged (a golden fingerprint test pins it). `RequiresEncryption` is never used as a proxy for "credential": it is also true
for non-credential inputs (any `SecretRead` binding, and any pinned policy that asks for it), and treating those as
credentials would refuse literals the spec allows. Inputs whose policy requires encryption without being
credentials are governed by R8's publish rule (`VF-ACT-011`), not by the credential rule.

**Rationale (verified)**:

- `DefaultActivityDefinitionHasher` serializes `Inputs` with `WhenWritingNull`. Nullable flags left null keep every
  existing CLR activity's catalog hash unchanged; non-null bools would change all of them and Model X reconciliation
  would throw `ActivityVersionHashMismatchException` on existing catalogs.
- For the same reason phase 0 annotates no built-in activity. Annotating one changes its content under the same
  version. Only test activities are annotated; `SendHttpRequest` and the agent options move in phase 1.
- A credential default would be a literal credential in the activity catalog.

**Alternatives considered**: an `InputSensitivity` enum in `Elsa.Primitives` (precedent: `ValueRepresentation`).
Rejected because `Elsa.Primitives` is on Line A and the bool pair needs no shared type. Deriving sensitivity from
`UIHint = "password"`: conflates presentation with policy.

**Constitution note**: §E2.8 (provisional) says the CLR scanner honors only `[Version]` and `[Required]` as author
intent. The scanner already reads `[ActivityInput]` display name, description, category, UI hint, defaults and
options (verified), so that sentence is already behind the code. This work extends the existing `[ActivityInput]`
reading and does not edit §E2.8.

## R6: What counts as a downgrade

**Decision**: `ArgumentState.IsSensitive` is `bool?`. `null` means the author made no choice, so the declaration
applies. `true` strengthens. An explicit `false` on an input declared sensitive or credential is a downgrade
attempt and is refused at compile time with `VF-ACT-005`, through one helper on `ValuePolicyCombiner` used by both
`ExecutableNodeCompiler.CompileActivityPolicy` and `RuntimeInputBindingCompiler.Compile`.

**Rationale**: `ValuePolicyCombiner.Combine` ORs the flags, so without an explicit check a downgrade would be
silently ignored. The spec asks for a refusal, which is the loud direction. Verified: Studio carries `isSensitive`
untouched in `argumentExtras` and does not write `false` by default (`activityInputWire.ts`), so ordinary saves do
not trip it.

## R7: One rule, enforced in the application layer above the design commands

**Decision**:

- One acceptance predicate in `Elsa.Workflows.Design.Core`, which owns `ArgumentState` and already references
  `Elsa.Activities.Design.Core` for `InputDefinition`. A binding is accepted when the input is not a credential
  (`InputDefinition.IsCredential`, never inferred; R5), or the binding is unbound, or its expression type is
  `Secret`. Unbound means null value, null or empty string, or JSON null or undefined. That is the definition
  `RequiredInputOutputValidator.IsBound` already uses, extracted so both rules share it.
- One stable rule identifier: `Inputs/CredentialLiteral`. Every finding's message starts with it, so it survives
  every translator unchanged.
- One tree-walking validator, `CredentialLiteralValidator` in `Elsa.Workflows.Design.Validations`, behind a §2.6.2
  replacement contract, `ICredentialLiteralValidator`, in `Elsa.Workflows.Design.Validations.Core`. It reuses
  `ActivityTreeWalker` and `CatalogVersionResolver`. A throwing extension, `EnsureNoCredentialLiteralsAsync(state)`,
  raises `CredentialLiteralRefusedException` carrying `ValidationError`s (path `{nodeId}/inputs/{referenceKey}`). The
  same class is also registered as an `IDraftValidator`, so the existing validation gate reports the same findings.
- No persistence project gains a reference, a dependency or a rule. The EF design commands are unchanged, and
  `WorkflowsDesignEntityFrameworkCore` gains no `DependsOn`.
- Five application-layer integration points cover the seven entry points:
  1. **Design API admission.** The five application-layer callers of a state-carrying design command call
     `EnsureNoCredentialLiteralsAsync` on the incoming state before the command runs: the Definitions/Add endpoint
     (`IAddWorkflowDefinitionCommand`), the Drafts/Replace endpoint and the Definitions/Update handler (both
     `IUpdateDraftCommand`), the Versions/Add endpoint (`IAddWorkflowDefinitionVersionCommand`), and the
     Definitions/Submit endpoint (`ISubmitWorkflowDefinitionCommand`), all under
     `src/essentials/Workflows/Design/Api/Endpoints/`. This covers draft save, add-version and submit. Draft save
     blocks because the refusal happens before the command, so nothing is stored. `Elsa.Workflows.Design.Api`
     already references `Elsa.Workflows.Design.Validations.Core` (verified).
  2. **The existing promotion gate.** `EfPromoteDraftToVersionCommand` already derives the validation error set
     in-lock through `DraftValidationGate.DeriveValidationErrorsAsync` and throws
     `DraftHasValidationErrorsException` on any error (verified). Registering the validator as an
     `IDraftValidator` makes promote refuse with no change to persistence. This also refuses a draft stored before
     the rule existed, or while its activity was not in the catalog.
  3. **`WorkflowsVersionReconciler.ReconcileVersion`**, per item, before any catalog mutation for that item (see
     the per-item contract in [the rule contract](contracts/credential-literal-rule.md)). This covers file
     reconciliation and git import, which both contribute through `WorkflowVersionsReconciling` sources (verified:
     `WorkflowVersionsReconcilingHandler`).
  4. **`RuntimeInputBindingCompiler.CompileAll`** (both overloads), which sees each input and its binding and
     applies the same predicate. This covers publish, publish-on-reconcile and draft test runs, with no catalog
     lookup. `Elsa.Workflows.Publishing` already references `Elsa.Workflows.Design.Core` and
     `Elsa.Workflows.Design.Validations` (verified).
  5. **`GitWorkflowExporter`**, per version, before writing its file.
- Coverage guard (T055): an architecture test classifies every design-persistence contract that carries workflow
  state, and asserts that every `src/` type whose constructor takes a state-carrying contract also takes
  `ICredentialLiteralValidator`, except the persistence implementations themselves. A new state-carrying command,
  or a new caller of an existing one, fails the guard until it is classified or admitted.

**Caller inventory (verified at `057adc44f`)**: outside the persistence projects, the only `src/` callers of
state-carrying design commands are the five Design.Api callers above and `WorkflowsVersionReconciler`
(`IMaterializeWorkflowDefinitionVersionCommand`). `ICreateDraftCommand` has no `src/` caller today.
`ICloneDraftFromVersionCommand` takes only a source version id and copies a stored version, so it adds no new
content; it is exempt with that reason, and a version that already holds a literal is the residual case below,
caught at promote and publish. `ISaveWorkflowDefinitionCommand` and `IMaterializeWorkflowDefinitionCommand` carry
definition metadata only (name, description, deleted flag), not state.

**Rationale**: business rules live in the application layer; stores keep only storage integrity. The rule is a pure
function of the incoming state and immutable catalog versions, so there is no read-then-write race to split across
layers. Draft save blocks because the admission throws before the command; that is the deliberate exception to the
non-blocking draft convention (spec clarification), and `DraftValidating` keeps its non-blocking contract for every
other validator. Promote reuses the gate that already exists for exactly this purpose.

**Alternatives considered**:

- **A guarded state writer inside the EF design commands** (the first draft of this plan): every one of the eight
  state-writing commands would call the rule before `EfDesignSupport.WriteState`. Rejected: it puts a business rule
  in persistence, every future backend would have to re-implement it, and the EF feature would have to depend on
  the validations feature.
- **Decorators over the command contracts, registered by Design.Validations.** Rejected after reading the
  composition. The design commands are replacement contracts owned by the selected backend
  (`DesignPersistenceBackend.ReplacementContractTypes`). `DesignPersistenceBackend.EnsureOwnsRegisteredContracts`
  throws "no longer exclusively owns" when a contract's descriptor is not the backend's own, and
  `WorkflowPortfolioEntityFrameworkCoreRegistration` calls it; `RemoveOwnedDescriptors` throws the same way when a
  backend is switched. A decorator would also have to be registered after the backend's `ConfigureServices`, which
  makes Validations depend on a persistence feature. Making decorators work would mean changing persistence
  composition, which is what this redesign avoids.
- A `JsonConverter` on `WorkflowDefinitionState`: synchronous, cannot reach the async catalog, and puts the rule in
  serialization.
- Making `DraftValidating` blocking for this category: add-version, submit and materialize have no draft, and it
  would change the documented contract of the shared gate.
- An `IDraftValidator` only: records without blocking at draft save, which the spec rules out. It is used here, but
  only for promote, where the gate already blocks.

**Residual**: like `RequiredInputOutputValidator`, the validator skips nodes whose activity version the catalog
cannot resolve, so it cannot tell whether their inputs are credentials. Draft save, add-version, submit, file
reconciliation and git export therefore cannot judge such nodes. Publish must resolve every node, so it remains the
backstop, and promote re-checks once the activity is installed. A literal on a credential input of an activity that
is not installed in that environment can therefore be stored until the activity is installed and the definition is
next promoted or published.

## R8: Enforcing RequiresEncryption (FR-010)

**Decision**: one publish rule and three runtime layers.

0. Publish rule (`VF-ACT-011`): an activity input whose effective policy requires encryption may be bound only to a
   `Secret` reference or left unbound. `RuntimeInputBindingCompiler` refuses a literal, object, default or
   expression binding on such an input, in both `CompileAll` overloads, after the effective policy is computed and
   after the credential rule (so a credential input still reports `Inputs/CredentialLiteral`). An unbound input
   whose pinned contract declares a default counts as a literal binding. The message names the node and input and
   never echoes the value or expression text.
1. Secret-bound values are never materialized (R3).
2. Producer-side withholding: `RuntimeExternalEnvelopeStorage.RewriteAsync`, the destination-storage decision used
   by input materialization and intrinsic writes (verified callers: `RuntimeActivityInputMaterializer`,
   `WorkflowIntrinsicExecutor`), replaces any present value whose effective policy requires encryption with a
   `Withheld` envelope of kind `PolicyRequiresEncryption`.
3. Backstop: `RuntimeCheckpointCommitValidator.Validate`, the application-layer check every commit passes before a
   store sees it (two call sites, verified), refuses a commit that carries a present inline or external envelope
   whose policy requires encryption, with `RuntimeCheckpointCommitValidationException` (`VF-ACT-005`).

The activator refuses to hydrate a withheld value that is not a secret reference, with a new code `VF-ACT-010`, and
does not hydrate null silently. This is a backstop, not the user-facing behavior.

**Why the publish rule (spec clarification, plan review 2026-10-01)**: the snapshot is persisted at Scheduled to
Running, before the body runs in discrete mode (verified). So a withheld non-secret input would never reach the
activity, not even on its first run: an expression-bound, encryption-required input would fault with `VF-ACT-010`
every time. No phase 0 path can deliver such a value without persisting it, so the binding is refused at publish,
where the author can act on it. Secret references are the only binding for encryption-required inputs. Phase 1's
redacting value type revisits this. After the rule, layers 2 and 3 and `VF-ACT-010` are reachable only through paths
that skip publish, such as runtime artifact import (R13), and through future producers. They stay as a structural
guarantee, and the canary bite-proofs them by injecting exactly that failure (A15).

## R9: Masking resolved values in emitted text (FR-012)

**Decision**: An in-memory, scoped `IRuntimeSecretMask`. The activator registers each resolved value under the
activity execution id and reference name, ignoring empty values. It is applied where text about an activation's
failure originates:

1. At the activity-code boundary, the invoke, resume and structural handlers replace an exception thrown from
   activation or activity code with `SecretMaskedException` before `RecordFaultAsync`, logging or telemetry see it.
   The masked exception carries the masked message, masked stack text, the original type name, and inner exceptions
   flattened and masked. `DefaultRuntimeFaultCapturePolicy` reports the original type name for it.
   Masking must not change how the fault is classified. `SecretMaskedException` implements
   `IRuntimeFaultClassification` (R4) and copies `IsRetryable` and `FailureCode` from the exception it wraps, so a
   `RuntimeSecretResolutionException` for `StoreUnavailable` that is masked (because an earlier secret in the same
   activation was registered with the mask) is still recorded as transient, with its code and its original type
   name. Only the text changes.
2. An activity-authored `ActivityFault.Message` is masked before `ActivityFaultProjection.ToNormalized`.

The replacement marker is `[secret:<reference name>]`. Matching replaces the raw value and its JSON-escaped form,
ordinally.

**Alternatives considered**: logger and OpenTelemetry processors reading an `AsyncLocal` mask (batch exporters run
on other threads after the async flow has ended); regex or name-fragment redactors (`OpenTelemetryRedactor`,
`DefaultDiagnosticSnapshotFactory`), which cannot know a value; masking only in the fault capture policy, which
misses log lines that carry the exception object.

**Limits** (spec assumption, restated so the canary is not mistaken for proving more): text an activity writes to
its own logger, values it returns as outputs, and values it puts in private state or bookmark payloads are not
covered in phase 0. Phase 1's redacting value type addresses them. The canary activity must not do any of these,
and the PR must say so.

## R10: The canary harness (FR-013, FR-014)

**Decision**: An in-process CShells web host on a test server, in the new bridge test project
`tests/essentials/Secrets/Workflows/Tests`. The host follows `EmbeddedFixtureHostEvidenceTests`
(`tests/essentials/Workflows/Runtime/Persistence/EntityFrameworkCore/Tests/EmbeddedFixtureHostEvidenceTests.cs`) for
composition, and the Runtime.Api endpoint tests (`tests/essentials/Workflows/Runtime/Api/Tests/RuntimeApiEndpointTestFactory.cs`)
for the HTTP surface. It must be a web host because `WorkflowsRuntimeApiFeature` is an `IWebShellFeature`; the canary
test class joins a FastEndpoints host collection, because FastEndpoints keeps its serializer configuration in
process-global statics (`tests/essentials/Workflows/Runtime/Api/Tests/FastEndpointsHostCollection.cs`).

It composes: design, validations and design EF (SQLite); publishing; runtime and runtime EF (SQLite);
`WorkflowsRuntimeApi` (the run inspector: instance, activity-execution, descendants, value-evidence payload and
incident reads; and the executable inspector); Secrets with the encrypted store; `SecretsWorkflows`; execution
evidence; git export in the Writer role against a temporary repository (reusing the `GitWorkflowExporterTests`
fixtures); a capturing logger provider (pattern: `tests/essentials/Secrets/Nuplane/Tests/Support/CapturingLoggerProvider.cs`);
a `System.Diagnostics.ActivityListener` recording runtime spans, as `RuntimeEngineTracingTests` already does (no
OpenTelemetry package is centrally managed); and a test activity with a credential input, one non-secret companion
input, and these modes: record and complete, throw with the value, return a fault with the value, suspend then
resume, and throw with the value on resume. A minimal structural test activity whose child-completion callback
throws with a resolved value covers the structural fault boundary.

Diagnostic snapshots are produced only when payload capture runs in `DiagnosticSnapshot` mode
(`RuntimeDiagnosticsSettingsResolver.ToCaptureMode`; `DefaultDiagnosticSnapshotFactory` is static and is reached through
`ActivityExecutionInspection`, `RuntimeContainerVariableEvidence` and `RuntimeWorkflowOutputStateProjection`). The canary saves runtime diagnostics settings at
level `DiagnosticSnapshot` before the runs, and repeats the scan at level `Payload`, the most permissive level, so
the capture policy's sensitive-payload rule is what keeps the value out.

**No surface may pass vacuously.** For each of the eight surfaces the canary first asserts a precondition that
proves the surface was exercised and contains the run, then scans it:

| Surface | Precondition (must hold before the scan counts) |
|---|---|
| Stored definitions and versions | the design database holds the canary definition's version row, and its state contains the secret reference name |
| Git export output | the export tree holds the version file of the canary definition, and `git log` of the export branch has its commit |
| Persisted instance and activity state | the runtime database holds the canary instance and an activity row whose snapshot carries the withheld marker (found through the encoded search for the reference name) |
| Execution evidence | at least one evidence record for the canary instance, including the secret-bound input's withheld disposition |
| Inspector responses | the instance, activity-execution and executable responses are 200, name the canary instance or artifact, and show the reference for the secret-bound input |
| Diagnostic snapshots | the inspection projection of the companion input holds a non-empty diagnostic snapshot for the canary run |
| Runtime telemetry export | at least one recorded span belongs to the canary instance |
| Runtime-captured logs | at least one captured line names the canary instance id |

**Encoding trap (verified, and the reason the scan cannot be a raw grep)**: runtime EF rows store `ContentJson`
through `RuntimeArtifactJson`, whose `LosslessUtf16StringConverter` writes every CLR `string` property as Base64 of
its UTF-16LE code units (`EfRelationalIdentity.Encode`). `JsonElement` payloads such as
`ValueEnvelope.InlineValue` are written raw. A raw scan of the database finds a leaked input value but misses the
same value inside a persisted fault message or metadata string. So the scanner searches each surface for the raw
UTF-8 value, its JSON-escaped form, and the three Base64 alignments of both its UTF-8 and UTF-16LE encodings, over
every file in the database directory including `-wal` and `-shm`. The canary value is ASCII alphanumeric so JSON
escaping cannot vary it. The bite-proof (FR-014) is also the scanner's own control: with a protection disabled, the
scanner must find the value on that surface.

**Defense in depth and injection**: several protections sit behind another one and cannot be reached while the
first holds (for example the commit backstop behind producer withholding). Disabling such a protection alone would
leave the canary green and prove nothing. For each of them the canary has a dedicated scenario that injects the
upstream failure with a test-only seam, so the protection under test is the only thing between a planted value and
a surface. The test-only seams are DI replacements of existing contracts in the canary host only, never switches in
production code: an `IRuntimeActivityInputMaterializer` decorator that re-plants a value the real materializer
withheld, an `IRuntimePayloadCapturePolicy` double that captures every payload, a decorator over the invoke scheduler work
handler that throws a planted value after the handler returns (the only way an exception reaches a runtime span,
since handlers record activity faults themselves), and a hand-built runtime artifact imported without publish
(R13), which is how an encryption-required literal reaches the runtime. Each injection scenario asserts its
own positive control (the planted value is present where the injection put it) and excludes that surface from its
scan. The full list is [A15](contracts/acceptance-proof-matrix.md#per-protection-bite-proof-a15).

**Alternatives considered**: the Workbench process harness (`tests/essentials/Workbench/Tests/WorkbenchProcess.cs`)
composes the real host, but has no way to load a test activity; the existing embedded runtime host has no design,
publishing, git export or Runtime.Api.

## R11: Publish-time type certainty (edge case)

**Decision**: Publish refuses a `Secret` binding in two cases, and otherwise lets conversion run at activation,
where a failure faults with `TypeMismatch`:

1. The existing conversion rules (`ValueConversionPlanResolver`,
   `src/essentials/Workflows/Publishing/Services/ValueConversionPlanResolver.cs`) have no plan from `string` to the
   input's declared type at all.
2. The reference declares a `typeName` whose value domain is known, and the input's declared type cannot hold that
   domain (`VF-ACT-013`). Phase 0 knows two domains. `Text` (built-in `text`) is free text: no publish-time refusal,
   since a text secret may legitimately hold a number. `StructuredText` (built-ins `rsa-key` and
   `x509-certificate`, PEM-style multi-line text) can be held only by a single string-typed input; any other
   declared type, including a collection, is refused. An `rsa-key` bound to an integer input is therefore refused at
   publish, as the spec's edge case asks.

A reference without a `typeName`, or whose type has no known domain (a custom secret type), keeps the run-time
`TypeMismatch` fault.

**Where the domain knowledge comes from, without a runtime-to-Secrets dependency**: the runtime contract that
resolution already uses (R1) gains a second, optional replacement contract in `Elsa.Workflows.Runtime.Core`,
`IRuntimeSecretTypeDomains`, with `RuntimeSecretValueDomain GetDomain(string typeName)` returning `Unknown`, `Text`
or `StructuredText`. The bridge `Elsa.Secrets.Workflows` implements it from `SecretTypeNames`
(`src/essentials/Secrets/Core/Models/SecretModels.cs`): `text` to `Text`, `rsa-key` and `x509-certificate` to
`StructuredText`, anything else to `Unknown`. `RuntimeInputBindingCompiler` takes it as an optional dependency. In
a host that does not compose `SecretsWorkflows`, every domain is `Unknown` and only the run-time fault applies;
both outcomes are loud, and the publish-time one needs the bridge. Publishing gains no reference: it already
references `Elsa.Workflows.Runtime.Core` (verified).

**Alternatives considered**: hard-coding the three type names in publishing (a second copy of Secrets vocabulary,
which §2.17 already accepts once for the `"Secret"` expression type, but this one would grow with every new secret
type); descriptor metadata on `ActivityInputDescriptorView` (the domain belongs to the secret, not to the input).

## R12: Secret bindings on activity kinds that cannot resolve at the point of use

**Decision**: Publish refuses a `Secret` binding, with `VF-ACT-012` and a fixed message naming the node and input,
on three kinds of node:

- intrinsic nodes (Set Variable, Set Output and the other `WorkflowIntrinsicKind` values);
- nodes whose activity descriptor consumer is not `elsa.clr-activity`, which today means graph activities
  (`WellKnownRuntimeActivityConsumers.GraphActivity`);
- CLR activity types that implement `IRuntimeActivityCheckpointParticipant`.

**Rationale**: each of them hands its inputs to code that persists them. Intrinsics write their value into a
durable variable or workflow output (`WorkflowIntrinsicExecutor`). Graph activities skip input hydration and capture
their inputs as boundary input durable values (`GraphActivityScope.CaptureInputsAsync`), which the boundary retry
later clones. Checkpoint participants receive their inputs through `MaterializeCheckpointInputsAsync` and may write
them into checkpoint state. Resolving a secret into any of these would persist it, so the binding is refused where
the author can act on it, the same loud-over-silent choice as for intrinsics. The spec scopes resolution to activity
inputs at the point of use. R3a lists every path and which ones this rule closes.

## R13: Entry points the spec does not list

The runtime artifact import paths (`src/essentials/Workflows/Runtime/Reconciliation`, artifact import through
publishing) carry already-compiled bindings and are not among FR-008's seven entry points, and they skip the
publish rules of R8 (`VF-ACT-011`) and R12 (`VF-ACT-012`). An imported artifact with a literal on an
encryption-required input reaches the runtime with that policy. R8's producer withholding then withholds it at
materialization, and activation refuses it with `VF-ACT-010`, which is loud. The literal still sits in the imported
file. Recommendation: a follow-up issue for an import-time check. Not planned here. The canary uses this path on
purpose, to reach the producer-withholding protection with an injected value (A15).

## R14: Studio (separate repository)

**Decision**: one slice in `elsa-foundation-studio`, after the descriptor fields ship:

- Add `isSensitive?` and `isCredential?` to the canonical `StudioActivityInputDescriptor`
  (`src/apps/Elsa.Studio.Web/Client/src/sdk/index.ts`) and to the hand-maintained copies that declare the input
  descriptor (`src/extensions/Elsa.Studio.Secrets`, `Elsa.Studio.ExpressionEditors.JavaScript` and
  `Elsa.Studio.ExpressionEditors.Liquid`, `Client/src/studio-sdk.d.ts`). Verified on Studio `origin/main`: the
  other `studio-sdk.d.ts` copies do not declare it.
- Credential inputs: default syntax `Secret`, and the syntax picker in
  `src/essentials/Elsa.Studio.Workflows/Client/src/ActivityPropertiesPanel.tsx` offers only `Secret`.
- A masked editor for UI hint `password`, and for sensitive non-credential text literals per FR-009, registered
  ahead of the single-line editor in `src/apps/Elsa.Studio.Web/Client/src/app/propertyEditors.tsx`. It never
  prefills a stored value.

Verified: the catalog-to-descriptor mapping passes `inputs` through unchanged
(`useWorkflowEditorData.ts`, `toActivityDescriptor`), so the new backend fields reach editors once the type declares
them. The local Studio checkout was on a feature branch; these facts were read from its `origin/main` without
fetching.

## R15: Housekeeping scope (FR-018)

Spec 079 names `ISecretResolver` and `src/Elsa/Secrets` (verified in its `plan.md`, `research.md`, `tasks.md` and
`contracts/runtime-contract.md`). Correct the names and paths. The 079 runtime contract also describes an
"expression handler", a mechanism spec 188 replaces; add a one-line pointer to spec 188 there rather than rewrite the
section. The Secrets `EXTENSION_POINTS.md` "Runtime Integration" section claims "the expression handler resolves the
latest active version", which is false today. The bridge slice rewrites that section to describe the shipped
mechanism.

## R16: Why ten slices instead of six

The expected shape had six slices. The code changed it in four places:

- Resolution cannot ship without the withheld snapshot: resolving before the Running commit persists the value in
  plain text (R3). So the withheld format ships first (slice 2), with activation refusing withheld inputs loudly,
  and activation-time resolution follows (slice 3). Neither intermediate state on `main` leaks a value or hydrates
  null.
- The bridge is its own slice (slice 4). That keeps the runtime diffs free of Secrets types and the bridge diff free
  of runtime internals.
- "Declaration and effective policy" (FR-006, FR-007, and FR-010's publish rule) and "the rule at seven entry
  points" (FR-008, FR-009) touch different layers (catalog and compiler policy, versus design API admission,
  reconciliation, compiler refusal and git export). Together they are too large to review as one diff.
- Withholding (FR-010, FR-011) and masking (FR-012) are independent mechanisms with independent bite-proofs.

Slice 1 (housekeeping), slice 9 (canary) and slice 10 (Studio) keep their expected roles.

## Authority and follow-ups

- The design document lists an ADR and glossary entries as follow-ups of the whole model. This plan adds glossary
  entries for the two new terms ("credential input", "withheld value") in the slices that introduce them, and leaves
  the ADR to the design document's own follow-up.
- Performance measurement is retired (#1668, ADR 0073): no benchmark, perf test or perf gate is planned.
- Elsa 4 is unreleased: no migration or compatibility shim. Definitions that already hold literals on newly guarded
  inputs are refused on their next save or publish.
