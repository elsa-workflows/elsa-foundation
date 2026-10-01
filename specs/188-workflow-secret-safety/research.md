# Research and design decisions: Workflow secret safety

Baseline: `main` at `057adc44f` plus the specification commit `30dfd01ec`. Every "verified" statement below was
read in that tree; everything else is a proposed implementation shape, not existing code. Scope is phase 0 of
[the Connections and Secrets model](../../docs/plans/connections-and-secrets-model.md) (decisions D1 and D10 only).
Connections, authentication schemes, OAuth and external secret stores stay out.

## R1: The runtime/Secrets seam

**Decision**: The workflow runtime owns a small replacement contract, `IRuntimeSecretResolver`, in
`Elsa.Workflows.Runtime.Core`, with runtime-owned shapes `RuntimeSecretReference(Name, TypeName?, Scope?)` and
`RuntimeSecretResolution` (succeeded value, or failure code plus retryable flag). A new cross-domain contribution
project, `Elsa.Secrets.Workflows` (`src/essentials/Secrets/Workflows/`), implements it over the existing
`ISecretValueResolver` and ships a `SecretsWorkflows` shell feature that depends on `Secrets`. The runtime never
references a Secrets project; the bridge references only `Elsa.Secrets.Core` and `Elsa.Workflows.Runtime.Core`.

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

**Rationale (verified)**:

- `ScopedWorkflowExecutionCommandExecutor` opens one DI scope per execution command bound to
  `new PersistenceScope(envelope.Partition.Value)`
  (`src/essentials/Workflows/Runtime/Services/Executions/ScopedWorkflowExecutionCommandExecutor.cs`), and
  `PersistenceWorkflowExecutionPartitionAccessor` returns `RequireScope().Value`, which throws for global and
  across-scope contexts.
- The instance's own state rows are keyed by that scope (`EfWorkflowExecutionStateStore.CopyToEntity` writes
  `ScopeKey`), so it is the boundary that already isolates the instance's state.
- Single-tenant hosts: `PersistenceScope.DefaultValue`, `WorkflowExecutionPartition.DefaultValue` and
  `AspNetCoreIdentityDefaults.DefaultTenantId` are all `"default"`, and the Secrets API reads its tenant from the
  same identity claim. So a secret created through the Secrets API in a single-tenant host resolves under the
  partition the runtime binds. No new tenant concept is introduced (spec assumption).

**Alternatives considered**:

- `WorkflowExecutionState.TenantId`: nullable, and the run inspector lets any trusted caller see an execution
  whose tenant is null (`HttpContextActivityExecutionInspectionAuthorizationContext`). Using it would need a fallback for null,
  which is exactly the cross-tenant default the constraints forbid. It would also add a state read per activation.
- A tenant field on `RuntimeInputBindingResolutionContext` filled by the materializer: secrets are not resolved at
  materialization in this design (R3), so it would be a second, unused tenant source that could drift.
- The HTTP principal's tenant claim: absent on background drains, timers and recovery sweeps.

**Assumption to prove**: a multi-tenant host binds the persistence scope to the same tenant identifier its
identity claim carries. The bridge slice proves the two-tenant case (same secret name, different values) end to end.

## R3: Resolution point and the withheld snapshot

**Decision**: A `Secret` binding compiles to a new `RuntimeInputBindingSource.SecretRead` carrying a
`RuntimeSecretReference` and a compiled string-to-target `ValueConversionPlan`. At Scheduled to Running, the
materializer emits a `ValueEnvelope` with a new presence, `Withheld`, holding the reference and the plan but no
value, and does not resolve anything. The secret is resolved in `ActivityActivator.ActivateAsync`
(`src/essentials/Activities/Runtime/Services/ActivityActivator.cs`), next to the existing just-in-time
`DereferenceInputsAsync`, converted, hydrated into the activity, registered with the per-activation mask (R9), and
never written back.

**Rationale (verified)**:

- The input snapshot is committed with the Running transition before the activity body runs
  (`WorkflowStartActivitySchedulerWorkHandler.StartActivity`), and the invoke handler reads the committed
  `state.InputSnapshot` in a later work item (`WorkflowInvokeActivitySchedulerWorkHandler`). Anything resolved
  before that commit is persisted: this is the leak FR-011 closes.
- Every activation passes the activator: invoke, bookmark resume (`WorkflowResumeBookmarkSchedulerWorkHandler`),
  structural parent evaluation (`StructuralParentEvaluationSupport`, two sites), and the retry boundary, which
  re-activates from the immutable snapshot (`WorkflowRetryActivityBoundarySchedulerWorkHandler`). Resolving there
  re-resolves on every execution and resumption (FR-002) without per-handler code.
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
- Resolve in each handler before activation: four call sites instead of one.

## R4: Failure semantics

**Decision**:

- Resolution failures fault the activity through a typed `RuntimeSecretResolutionException(referenceName,
  failureCode, isRetryable)` with the fixed message `Secret '<name>' could not be resolved (<code>).` The bridge
  maps `StoreUnavailable` to retryable and every other code to permanent, and discards `ResolvedSecret.Error`.
- A conversion failure at activation (`RuntimeValueConversionException`) is reported with code `TypeMismatch`.
- `ActivityFaultIncidentRecorder` hard-codes `isRetryable: false` for exception faults (verified); it reads
  `IsRetryable` from this exception type instead.
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
`IsRetryable = true` on the fault for operators and incident strategies. An explicit retry re-activates from the
snapshot and re-resolves. No automatic retry is added.

## R5: Declaring sensitive and credential inputs

**Decision**: `[ActivityInput]` gains two bool properties, `IsSensitive` and `IsCredential`; credential implies
sensitive. `InputDefinition` gains `bool? IsSensitive = null` and `bool? IsCredential = null`, set only when true.
`ClrAssemblyScanner` reads both by name (reflection-only, like `UIHint`), normalizes credential to also be
sensitive, and refuses a credential input that declares `DefaultValue`. `ActivityInputDescriptorView` gains
non-null `IsSensitive` and `IsCredential` so Studio and validators read them (FR-006).

Effective policy: sensitive sets `IsSensitive`; credential sets `IsSensitive` and `RequiresEncryption`. A
`SecretRead` binding always adds the minimum `{IsSensitive, RequiresEncryption}`, whatever the declaration.

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

## R7: One rule, and where it is enforced for all seven entry points

**Decision**:

- One acceptance predicate in `Elsa.Workflows.Design.Core`, which owns `ArgumentState` and already references
  `Elsa.Activities.Design.Core` for `InputDefinition`. A binding is accepted when the input is not a credential, or
  the binding is unbound, or its expression type is `Secret`. Unbound means null value, null or empty string, or JSON
  null or undefined. That is the definition `RequiredInputOutputValidator.IsBound` already uses, extracted so both
  rules share it.
- One stable rule identifier: `Inputs/CredentialLiteral`.
- One tree-walking validator behind a §2.6.2 replacement contract, `ICredentialLiteralValidator`. The contract
  lives in `Elsa.Workflows.Design.Validations.Core`; the implementation lives in
  `Elsa.Workflows.Design.Validations` and reuses `ActivityTreeWalker` and `CatalogVersionResolver`. A gate extension
  throws `CredentialLiteralRefusedException` carrying `ValidationError`s (path `{nodeId}/inputs/{referenceKey}`).
- Three integration points cover the seven entry points:
  1. The EF design commands, the only layer every design writer shares (API endpoints, the version reconciler,
     git import, test kits, future agent writers). A guarded state writer on `EfDesignCommand` replaces the direct
     `EfDesignSupport.WriteState` call in the eight state-writing commands: add-definition, create-draft, clone,
     update-draft, promote, add-version, submit and materialize-version. A guard test asserts no command serializes
     `WorkflowDefinitionState` any other way. This covers draft save, promote, add-version, submit and file
     reconciliation.
  2. `RuntimeInputBindingCompiler.CompileAll`, which sees each `(InputDefinition, ArgumentState)` pair directly and
     applies the same predicate. This covers publish, publish-on-reconcile and draft test runs, with no catalog
     lookup.
  3. `GitWorkflowExporter`, which guards each version before writing its file.

**Rationale**: No single existing choke point covers all seven without moving the rule into persistence or
serialization (alternatives below). The rule lives in the application layer (Design.Validations). The commands
only invoke a gate, as they already publish the in-lock `DraftValidating` gate (constitution §E2.9.7). The rule is
a pure function of the incoming state and immutable catalog versions, so there is no read-then-write race to split.
Draft save blocks because the guard throws before the atomic write stage. That is the deliberate exception to the
non-blocking draft convention (spec clarification); `DraftValidating` keeps its non-blocking contract for every
other validator.

**Alternatives considered**:

- A `JsonConverter` on `WorkflowDefinitionState`: synchronous, cannot reach the async catalog, and puts the rule in
  serialization.
- Calls in each API endpoint and the reconciler: nine call sites, and any non-HTTP writer bypasses them.
- Making `DraftValidating` blocking for this category: add-version, submit and materialize have no draft, and it
  would change the documented contract of the shared gate.
- An `IDraftValidator` only: records without blocking at draft save, which the spec rules out.

**Residual**: like `RequiredInputOutputValidator`, the validator skips nodes whose activity version the catalog
cannot resolve, so it cannot tell whether their inputs are credentials. Publish must resolve every node, so it
remains the backstop. A literal on a credential input of an activity that is not installed in that environment can
therefore be stored until the activity is installed and the definition is next saved or published.

## R8: Enforcing RequiresEncryption (FR-010)

**Decision**: three layers.

1. Secret-bound values are never materialized (R3).
2. Producer-side withholding: `RuntimeExternalEnvelopeStorage.RewriteAsync`, the destination-storage decision used
   by input materialization and intrinsic writes (verified callers: `RuntimeActivityInputMaterializer`,
   `WorkflowIntrinsicExecutor`), replaces any present value whose effective policy requires encryption with a
   `Withheld` envelope of kind `PolicyRequiresEncryption`.
3. Backstop: `RuntimeCheckpointCommitValidator.Validate`, the application-layer check every commit passes before a
   store sees it (two call sites, verified), refuses a commit that carries a present inline or external envelope
   whose policy requires encryption, with `RuntimeCheckpointCommitValidationException` (`VF-ACT-005`).

The activator refuses to hydrate a withheld value that is not a secret reference, with a new code `VF-ACT-010`, and
does not hydrate null silently.

**Ambiguity, recorded**: FR-010 says other withheld values "are not recoverable after the instance is persisted",
which assumes the activity runs before persistence. Verified: the snapshot is persisted at Scheduled to Running,
before the body runs in discrete mode, so such an input would not reach the activity even on its first run. After
R5, no phase 0 producer creates one: credential inputs accept only secret references, `FromAuthoredStorage` sets
`RequiresEncryption` false, and outputs have no encryption declaration. Layers 2 and 3 are a structural guarantee for
future producers, and the hydration failure is loud. This is the conservative reading. The alternative, silently
hydrating null, fails silently.

## R9: Masking resolved values in emitted text (FR-012)

**Decision**: An in-memory, scoped `IRuntimeSecretMask`. The activator registers each resolved value under the
activity execution id and reference name, ignoring empty values. It is applied where text about an activation's
failure originates:

1. At the activity-code boundary, the invoke, resume and structural handlers replace an exception thrown from
   activation or activity code with `SecretMaskedException` before `RecordFaultAsync`, logging or telemetry see it.
   The masked exception carries the masked message, masked stack text, the original type name, and inner exceptions
   flattened and masked. `DefaultRuntimeFaultCapturePolicy` reports the original type name for it.
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

**Decision**: An in-process CShells generic host, following `EmbeddedFixtureHostEvidenceTests`
(`tests/essentials/Workflows/Runtime/Persistence/EntityFrameworkCore/Tests/EmbeddedFixtureHostEvidenceTests.cs`),
in the new bridge test project `tests/essentials/Secrets/Workflows/Tests`. It composes design, validations and
design EF (SQLite); publishing; runtime and runtime EF (SQLite); Secrets with the encrypted store; `SecretsWorkflows`;
execution evidence; git export in the Writer role against a temporary repository (reusing the
`GitWorkflowExporterTests` fixtures); a capturing logger provider (pattern:
`tests/essentials/Secrets/Nuplane/Tests/Support/CapturingLoggerProvider.cs`); a `System.Diagnostics.ActivityListener`
recording runtime spans, as `RuntimeEngineTracingTests` already does (no OpenTelemetry package is centrally managed);
and a test activity with a credential input and four modes: record and complete, throw with the value, return a
fault with the value, and suspend then resume.

**Encoding trap (verified, and the reason the scan cannot be a raw grep)**: runtime EF rows store `ContentJson`
through `RuntimeArtifactJson`, whose `LosslessUtf16StringConverter` writes every CLR `string` property as Base64 of
its UTF-16LE code units (`EfRelationalIdentity.Encode`). `JsonElement` payloads such as
`ValueEnvelope.InlineValue` are written raw. A raw scan of the database finds a leaked input value but misses the
same value inside a persisted fault message or metadata string. So the scanner searches each surface for the raw
UTF-8 value, its JSON-escaped form, and the three Base64 alignments of both its UTF-8 and UTF-16LE encodings, over
every file in the database directory including `-wal` and `-shm`. The canary value is ASCII alphanumeric so JSON
escaping cannot vary it. The bite-proof (FR-014) is also the scanner's own control: with a protection disabled, the
scanner must find the value on that surface.

**Alternatives considered**: the Workbench process harness (`tests/essentials/Workbench/Tests/WorkbenchProcess.cs`)
composes the real host, but has no way to load a test activity; the existing embedded runtime host has no design,
publishing or git export.

## R11: Publish-time type certainty (edge case)

**Decision**: Publish refuses a `Secret` binding only when the existing conversion rules
(`ValueConversionPlanResolver`, `src/essentials/Workflows/Publishing/Services/ValueConversionPlanResolver.cs`) have
no plan from `string` to the input's declared type at all. Otherwise conversion runs at activation and a failure
faults with `TypeMismatch`.

**Rationale and ambiguity**: secret values are strings, and only the Secrets domain knows what an `rsa-key` value
looks like. The spec's example (an `rsa-key` into an integer input) has a string-to-integer plan, so it faults at
run time with `TypeMismatch` instead of being refused at publish. Refusing it at publish would need Secrets' type
knowledge in publishing. Both outcomes are loud; the run-time one is later. Recorded as an interpretation.

## R12: Secret bindings on engine intrinsics

**Decision**: Publish refuses a `Secret` binding on an intrinsic node (Set Variable, Set Output) with a fixed
message.

**Rationale**: intrinsics write their value into a durable variable or workflow output
(`WorkflowIntrinsicExecutor`), which would persist the secret. The spec scopes resolution to activity inputs. Loud
refusal over a silent write.

## R13: Entry points the spec does not list

The runtime artifact import paths (`src/essentials/Workflows/Runtime/Reconciliation`, artifact import through
publishing) carry already-compiled bindings and are not among FR-008's seven entry points. An imported artifact with
a literal on a credential input would compile its literal with the credential's `RequiresEncryption` policy. The R8
backstop then refuses to persist it at run time, which is loud. The literal still sits in the imported file.
Recommendation: a follow-up issue for an import-time check. Not planned here.

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
- "Declaration and effective policy" (FR-006, FR-007) and "the rule at seven entry points" (FR-008, FR-009) touch
  different layers (catalog and compiler policy, versus design commands, compiler refusal and git export). Together
  they are too large to review as one diff.
- Withholding (FR-010, FR-011) and masking (FR-012) are independent mechanisms with independent bite-proofs.

Slice 1 (housekeeping), slice 9 (canary) and slice 10 (Studio) keep their expected roles.

## Authority and follow-ups

- The design document lists an ADR and glossary entries as follow-ups of the whole model. This plan adds glossary
  entries for the two new terms ("credential input", "withheld value") in the slices that introduce them, and leaves
  the ADR to the design document's own follow-up.
- Performance measurement is retired (#1668, ADR 0073): no benchmark, perf test or perf gate is planned.
- Elsa 4 is unreleased: no migration or compatibility shim. Definitions that already hold literals on newly guarded
  inputs are refused on their next save or publish.
