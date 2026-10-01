# Research and design decisions: Workflow secret safety

Baseline: `main` at `057adc44f` plus the specification commit `30dfd01ec`. Every "verified" statement below was
read in that tree; everything else is a proposed implementation shape, not existing code. Scope is phase 0 of
[the Connections and Secrets model](../../docs/plans/connections-and-secrets-model.md) (decisions D1 and D10 only).
Connections, authentication schemes, OAuth and external secret stores stay out. Plan review round 1 (2026-10-01)
re-read every code claim it changed (R2, R3, R3a, R7, R8, R10, R11, R12) in the same tree. Plan review round 2
(2026-10-01) did the same for R3a, R5, R7, R11 and R12, on the branch tree that carries `057adc44f`'s source
unchanged. Plan review round 3 and the owner decision of 2026-10-01 (SendHttpRequest consumes a secret) re-read the
code behind R3a, R5, R7, R10, R11, R12 and the new R17 in the same tree.

## R1: The runtime/Secrets seam

**Decision**: The workflow runtime owns a small replacement contract, `IRuntimeSecretResolver`, in
`Elsa.Workflows.Runtime.Core`, with runtime-owned shapes `RuntimeSecretReference(Name, TypeName?, Scope?)` and
`RuntimeSecretResolution` (succeeded value, or failure code plus retryable flag). A new cross-domain contribution
project, `Elsa.Secrets.Workflows` (`src/essentials/Secrets/Workflows/`), implements it over the existing
`ISecretValueResolver` and ships a `SecretsWorkflows` shell feature
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
| IP14 | Activities that copy a hydrated input into their own persisted state: `ForEach` (`ToItemEnvelope` writes each `Collection` item into the persisted iteration frame under the `Collection` binding's policy), `For` (the persisted iteration index starts at `Start` and walks by `Step` to `End`), `DispatchWorkflow` (`Inputs` and `CorrelationId` go into the persisted dispatch start payload), `PublishEvent` (`EventName`, `CorrelationId` and `Payload` are staged as a stimulus intent), `Delay` (the timer registration holds a due time derived from `Duration`) | hydrated by the activator, then copied by activity code | refuse at publish: the activity names the input in `[RefusesSecretBinding(..., PersistedByActivity)]` (R12), and a `Secret` binding on it fails with `VF-ACT-012` | T099 |
| IP15 | Publish-time literal reader `HttpEndpointTriggerStimulusProvider` (`HttpEndpoint` `Path`, `SupportedMethods`, `CanStartWorkflow`, `Authorize`, `Policy`, `RequestTimeout`, `RequestSizeLimit`, `ResponseMode`) | reads literal bindings when the trigger is described; any other source throws a generic `ArgumentException` | refuse at publish: `[RefusesSecretBinding(..., FixedAtPublish)]`, `VF-ACT-012`; backstop: the reader throws a fixed `VF-ACT-012` message for `SecretRead` (T007) | T099 |
| IP16 | Publish-time literal reader `EventTriggerStimulusProvider` (`Event` `EventName`, `CorrelationId`, `CanStartWorkflow`) | a non-literal `EventName` throws a generic error; a non-literal `CorrelationId` or `CanStartWorkflow` is silently treated as unauthored | as IP15 | T099 |
| IP17 | Publish-time literal reader `SchedulingNodeInputs` (`Cron.Expression`, `Timer.Interval`, read by the Cron and Timer trigger and recurring-schedule providers) | a non-literal returns null, and the caller throws a generic "no literal" error | as IP15 | T099 |
| IP18 | Publish-time literal reader `BpmnStartTriggerNodeInputs` (`BpmnProcess.CanStartWorkflow`, read by `BpmnProcessTriggerStimulusProvider` and `BpmnProcessRecurringScheduleProvider`) | a non-literal is silently treated as unauthored | as IP15 | T099 |
| IP19 | Publish-time literal reader `DispatchPinSource` (`DispatchWorkflow` `WorkflowDefinitionId` and `Inputs`) | a non-literal `WorkflowDefinitionId` throws a generic error; a non-literal `Inputs` silently marks the pin incomplete | as IP15 | T099 |
| IP20 | Value-derived outcomes: `ExecutableNodeCompiler.AddValueDerivedOutcomes` reads the input named by `[ActivityValueOutcomes]` (`RunJavaScript`'s possible outcomes, `SendHttpRequest.ExpectedStatusCodes`) | reads a literal array at publish; any other source silently adds no outcome | refuse at publish: the compiler refuses a `Secret` binding on the input the attribute names, `VF-ACT-012`, with no new annotation | T099 |
| IP21 | Variable initial values: `ExecutableNodeCompiler.CompileVariableDeclaration` compiles a variable default through `RuntimeInputBindingCompiler.Compile`, and `RuntimeVariableDeclarationProjector` writes it into the persisted variable frame | a non-literal is refused at publish with a generic "requires a persistable literal initial binding" message | refuse at publish with an explicit `VF-ACT-012` message for a `Secret` default | T099 |
| IP22 | Activities that return a hydrated input in their result, or copy it into a fault they report (round 3): `Inline` (`ExecuteAsync` returns `Expression` as the result), `WriteHttpResponse` (`Body` and `ContentType` are returned in the `HttpResponseInstruction` result), `BpmnDecision` (`Outcome`, trimmed, becomes both the result and the completion outcome), `Fault` (`Code`, `Message`, `Category` and `FaultType`, trimmed, are copied into the returned `ActivityFault`; R9 masks only the fault message and only the untrimmed value) | hydrated by the activator, then returned by activity code; the completion persists it under the output's policy | refuse at publish: `[RefusesSecretBinding(..., EchoedToOutput)]` (R12), `VF-ACT-012`. `Inline.Expression` has type `Object`, which R11's type rule already refuses; the attribute keeps the reason explicit and is checked first, so the author sees `VF-ACT-012` | T110 |

IP22 was added in round 3; the search that found it is recorded after the round 2 search below.

Fused mode (spec 123) commits the same Started stage and dispatches the invoke handler inline, so it takes IP1 then
IP2 and IP3 in one work item. A path added later that reads `ActivityInputSnapshot.Values` must handle
`ValuePresence.Withheld` explicitly (T007's audit rule) or this table is stale.

**How the table was built (round 2)**. Every reader of compiled input bindings, and every activity that builds a
persisted envelope, was searched for in `src/` (essentials and extensions):

```bash
grep -rn --include='*.cs' "\.InputBindings" src | grep -v /Publishing/
grep -rln --include='*.cs' "RuntimeInputBinding\b\|RuntimeInputBindingSource\.\|InputBindings" src
grep -rn --include='*.cs' "\.LiteralValue\b\|\.Literal\b" src
grep -rn --include='*.cs' "\.InputBindings\|\.LiteralValue\b\|Source [!=]= RuntimeInputBindingSource" src/essentials/Workflows/Publishing
grep -rln --include='*.cs' "ValueEnvelope.Inline(\|ValueEnvelope\.Null(\|LoopIterationScopeRequest(\|SerializeToElement" src/essentials/Activities src/extensions
```

Readers of compiled bindings found and where each is handled: `RuntimeActivityInputMaterializer` (IP1),
`WorkflowIntrinsicExecutor` (IP11), `WorkflowExecutableInspector` and `WorkflowExecutableHasher` (IP12),
`RuntimeInputBindingResolver` (T007), `ForEach` (IP14), the five publish-time literal readers (IP15 to IP19),
`ExecutableNodeCompiler` (intrinsic literal keys under IP11, value-derived outcomes IP20, variable defaults IP21),
`RuntimeVariableDeclarationProjector` (IP21), `ActivityResultConversionPlanLinker` (reads only `ActivityResult`
bindings), `ActivityTemplatePlacer` and `ActivityTemplateCompiler` (compile bindings, covered by the compiler rules),
and `GraphActivityProvider` (emits empty bindings for graph boundaries, IP7). The `.Literal` hits in
`Elsa.Workflows.Design.Core` authoring and `Elsa.Expressions.Api` read authored values, not compiled bindings.
Activities that build persisted envelopes from their own hydrated inputs are the IP14 list. The other hits are
structure handlers and state persisters (Flowchart, Sequence, If, Parallel, BPMN), which persist structure and
engine state rather than input values, runtime services covered above, and `RunJavaScript`, `HttpEndpointMiddleware`
and the dispatch runtime services, which write outputs or request data (R9 limits), not input values.

**Round 3: activities that echo an input into an output, result, bookmark payload, fault or log.** The round 2
search looked for envelope builders and missed activities that simply return an input. Every activity input in
`src/` was listed with its CLR type, then every place an activity file hands a value to its completion, a fault, a
bookmark, the console or a logger was listed, and each hit was read:

```bash
grep -rln --include='*.cs' "\[ActivityInput" src
awk '/\[ActivityInput/{f=1} f && /public /{print FILENAME": "$0; f=0}' $(grep -rln --include='*.cs' "\[ActivityInput" src)
grep -rn --include='*.cs' "ActivityTransition\.\(Complete\|Fault\)\|Console\.Write\|ILogger<\|\.Log[A-Z][a-z]*(\|Bookmark(" $(grep -rln --include='*.cs' "\[ActivityInput" src)
grep -rn --include='*.cs' "Suspend(\|ActivityBookmarkRequest(" $(grep -rln --include='*.cs' "\[ActivityInput" src)
```

No file under `src/extensions` declares an `[ActivityInput]`. After R11's type rule only single `String` (and
canonical any-typed) inputs can take a `Secret` binding, so each `String` input was classified:

- Returned or copied into a result or fault: `Inline.Expression`, `WriteHttpResponse` `Body` and `ContentType`,
  `BpmnDecision.Outcome`, the four `Fault` inputs. New row IP22, refused.
- Already refused: `Cron.Expression` and `Timer.Interval` are also returned in their results (`CronResult`,
  `TimerResult`), and `Event`, `PublishEvent`, `DispatchWorkflow` and `HttpEndpoint` string inputs are persisted or read
  at publish (IP14 to IP19).
- Read but not copied, so not refused: `Switch.Value` (the completion outcome is the authored case's match string or
  `Default`, never the value itself, `SwitchNavigator.SelectCase`); `RunJavaScript.Script` (evaluated; the result is
  what the script returns, and an engine error that quotes part of the script is masked only when it holds the whole
  value); `SendHttpRequest` `Method`, `Content`, `ContentType` and the new `Authorization` (sent on the request; the
  result holds only the response's status, body and headers, `CollectHeaders` reads `response.Headers` and
  `response.Content.Headers`; R17).
- Written only to the console: `WriteLine.Text` (`Console.WriteLine`). It stays a documented residual (spec
  assumption). `WriteLines.Lines` also goes to the console, but it is a `List<string>`, which R11 refuses.
- Private state and bookmarks: the activities that suspend with state or a bookmark built from an input
  (`Suspend(...)` in `DispatchWorkflow`, `Delay`, `Event` and `HttpEndpoint`; `ActivityBookmarkRequest` in
  `DispatchWorkflow`) use inputs already refused by IP14 to IP16. No activity writes an input to its own logger (no
  `ILogger<` or `.Log...(` hit in these files).

## R4: Failure semantics

**Decision**:

- Resolution failures fault the activity through a typed `RuntimeSecretResolutionException(referenceName,
  failureCode, isRetryable)` with the fixed message `Secret '<name>' could not be resolved (<code>).` The bridge
  maps `StoreUnavailable` to retryable and every other code to permanent, and discards `ResolvedSecret.Error`.
- A conversion failure at activation (`RuntimeValueConversionException`) is reported with its own code,
  `ConversionFailed`, permanent. `TypeMismatch` keeps one meaning: the stored secret's type differs from the
  reference's type (round 4). The case is a backstop: publish compiles no plan from text that can fail on a string
  (R11).
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
sensitive, and refuses a credential input that declares `DefaultValue`. It also refuses a credential input that could
never be bound: a credential input accepts only a `Secret` binding, so it must not sit where R12 refuses one. The
scanner therefore refuses, naming the type and input, a credential input on a type that implements
`IRuntimeActivityCheckpointParticipant` (matched by full name, as the scanner already matches `IActivity`) and a
credential input that the type names in `[RefusesSecretBinding]` (R12; read for this check only, never written to
the catalog), and a credential input whose CLR type is not `string` (R11: a `Secret` binding converts only to a
single string or any-typed input, so a credential of any other type could never be bound). Graph activities and intrinsics have no declaration surface: the only other producer of catalog
`InputDefinition`s is the Activities Design API (`AddDefinitionCommandHandler`, `AddVersionCommandHandler`, whose
commands accept `InputDefinition`s for any consumer), and it refuses an input with `isCredential: true`, because in
phase 0 a credential is declared only through `[ActivityInput(IsCredential = true)]`. Intrinsic descriptors report
`false` for both flags. `ActivityInputDescriptorView` gains non-null `IsSensitive` and `IsCredential` so Studio and
validators read them (FR-006).

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
- Phase 0 annotates one built-in input, `SendHttpRequest.Authorization`, by owner decision (2026-10-01; R17). That
  input is new, so it changes `SendHttpRequest`'s catalog content and hash; every other built-in keeps its hash. The
  version consequence is handled by the code's existing versioning rule, not by a `[Version]` attribute (R17). The
  agent options stay for phase 1. The `[RefusesSecretBinding]` attribute that R12 puts on built-ins is different: the
  scanner reads it only to refuse a conflicting credential declaration and never writes it to `InputDefinition`, so
  those activities keep their catalog hash (pinned by T040 and T099).
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
  `ActivityTreeWalker` and `CatalogVersionResolver`. The same class is also registered as an `IDraftValidator`, so
  the validation panel (`DraftValidating`) reports the same findings. That registration only reports; no entry point
  relies on it for enforcement.
- One shared admission helper, `WorkflowStateAdmission`, a `public sealed` class in
  `Elsa.Workflows.Design.Validations.Core` next to the existing `DraftValidationGate`, registered by
  `WorkflowDesignValidations`. It is the only way an application-layer caller runs the rule:
  `AdmitAsync(state)` raises `CredentialLiteralRefusedException` carrying `ValidationError`s (path
  `{nodeId}/inputs/{referenceKey}`), and `FindRefusalsAsync(state)` returns the same findings without throwing, for the
  per-item callers. Every caller of a state-writing design command takes it, which is what the coverage guard checks.
- No persistence project gains a reference, a dependency or a rule, and `WorkflowsDesignEntityFrameworkCore` gains no
  `DependsOn`. The only persistence change is promote's content precondition (point 1 below), a storage-integrity
  compare-and-set that carries no rule.
- Four application-layer integration points cover the seven entry points:
  1. **Design API admission, including promote.** The six Design API callers of a state-writing design command call
     `WorkflowStateAdmission.AdmitAsync` before the command runs: the Definitions/Add endpoint
     (`IAddWorkflowDefinitionCommand`), the Drafts/Replace endpoint and the Definitions/Update handler (both
     `IUpdateDraftCommand`), the Versions/Add endpoint (`IAddWorkflowDefinitionVersionCommand`), the
     Definitions/Submit endpoint (`ISubmitWorkflowDefinitionCommand`), and the Drafts/Promote endpoint
     (`IPromoteDraftToVersionCommand`), all under `src/essentials/Workflows/Design/Api/Endpoints/`. The first five
     admit the incoming state. Promote carries only a draft id, so its endpoint reads the stored draft through
     `IWorkflowDefinitionDraftStore` (as the Drafts/Get endpoint already does) and admits the draft's state. This
     covers draft save, promote, add-version and submit. Draft save blocks because the refusal happens before the
     command, so nothing is stored. Promote refuses a draft stored before the rule existed, or while its activity was
     not in the catalog. `Elsa.Workflows.Design.Api` already references `Elsa.Workflows.Design.Validations.Core`
     (verified).

     Promote does not rely on the in-lock gate. `EfPromoteDraftToVersionCommand` derives validation errors only when
     its optional `IInlineEventPublisher? inlineEvents` constructor parameter is composed
     (`if (inlineEvents is not null) ...`, verified), so a host without it would promote a credential literal
     unchecked. The endpoint's admission runs whatever the command's composition.

     Promote promotes exactly what it admitted (round 3). The endpoint admits the draft it reads outside the
     promotion lock, and the command re-reads the draft in-lock and promotes that row's `State` (verified: the
     atomic-write delegate reads `Db.Drafts` after `beforeAttempt` takes the draft and definition locks). A concurrent
     `Drafts/Replace` landing between the two reads would be promoted without having been admitted by promote: a
     stateful rule split across the application and persistence layers. The draft row has no revision or concurrency
     token to compare (verified: the `elsa_workflow_definition_drafts` mapping declares no `IsConcurrencyToken`
     property, and `LastModifiedAt` is a timestamp that two writes in one clock tick, or a frozen test clock, leave
     unchanged). So the endpoint computes the SHA-256 of the admitted draft's stored `StateSource` (empty when null)
     and passes it to the command as an expected-state precondition. The hash is required (round 4).
     `IPromoteDraftToVersionCommand` keeps a single `Execute` method, `Execute(key, draftId, requestedVersion,
     expectedStateHash, ct)`, whose `expectedStateHash` is a non-nullable `string`; the two current overloads
     without it are removed, because their only `src/` caller is the Drafts/Promote endpoint (verified), which uses
     the `requestedVersion` overload, and Elsa 4 is unreleased, so no compatibility overload is kept. The command
     throws `ArgumentException` for a null or blank hash before it reads or writes anything, so no caller can
     promote without a precondition. One helper in `Elsa.Workflows.Design.Persistence.Core`,
     `WorkflowDraftStateHash.Compute(stateSource)`, computes the hash for the endpoint, the command and the test
     callers. `EfPromoteDraftToVersionCommand` hashes the `StateSource` of the row it reads in-lock and throws a new
     `WorkflowDraftChangedException` (`Elsa.Workflows.Design.Persistence.Core`, mapped to 409) before it writes
     anything when the two differ. That comparison is storage integrity, a compare-and-set on the row's content; it
     knows nothing about credentials, and the rule stays in the endpoint. Where the publisher is composed, the
     in-lock gate also re-runs the validators. T113 proves the conflict.

     The hash is not part of `PromoteDraftRequestMaterial` (round 4). The atomic writer resolves an existing
     operation marker before `beforeAttempt` takes the locks and before the delegate runs (verified in
     `EfDesignAtomicWriter`), so a replay of an already-succeeded promote with the same operation key returns the
     original version id and writes nothing, even when the draft changed afterwards and the replay therefore carries
     a different hash. Leaving the hash out loses no safety: a replay writes no row, and the in-lock comparison
     guards every first execution. Through the endpoint, a replay is still admitted first, so a replay after an
     edit that put a credential literal into the draft is refused with 400 before the command runs; a replay after
     an admissible edit returns the original version id.
  2. **`WorkflowsVersionReconciler.ReconcileVersion`**, per item, before any catalog mutation for that item (see
     the per-item contract in [the rule contract](contracts/credential-literal-rule.md)). This covers file
     reconciliation and git import, which both contribute through `WorkflowVersionsReconciling` sources (verified:
     `WorkflowVersionsReconcilingHandler`). It calls `WorkflowStateAdmission.FindRefusalsAsync`.
  3. **`RuntimeInputBindingCompiler.CompileAll`** (both overloads), which sees each input and its binding and
     applies the same predicate. This covers publish, publish-on-reconcile and draft test runs, with no catalog
     lookup. `Elsa.Workflows.Publishing` already references `Elsa.Workflows.Design.Core` and
     `Elsa.Workflows.Design.Validations` (verified).
  4. **`GitWorkflowExporter`**, per version, before writing its file, through `WorkflowStateAdmission.FindRefusalsAsync`.
- Coverage guard (T055): an architecture test classifies every `*Command` contract in
  `Elsa.Workflows.Design.Persistence.Core.Contracts` as state-writing or not, and asserts one thing about the
  state-writing ones: every non-persistence `src/` type whose constructor takes one also takes
  `WorkflowStateAdmission`. Promote is state-writing (it writes a version from a stored draft) and is not exempt. A
  new state-writing command, or a new caller of an existing one, fails the guard until it is classified or admitted.
  The guard also asserts, by reflection, that every `Execute` method of `IPromoteDraftToVersionCommand` takes a
  non-nullable `expectedStateHash`, so a promote overload without the admitted hash cannot be added back.

**Caller inventory (verified at `057adc44f`, re-run in round 2)**: outside the persistence projects, the only `src/`
callers of state-writing design commands are the six Design.Api callers above and `WorkflowsVersionReconciler`
(`IMaterializeWorkflowDefinitionVersionCommand`); `IPromoteDraftToVersionCommand` has no other `src/` caller.
`ICreateDraftCommand` has no `src/` caller today and is classified admitted. `ICloneDraftFromVersionCommand` takes
only a source version id and copies a stored version, so it adds no new content; it is exempt with that reason, and a
version that already holds a literal is the residual case below, caught at promote and publish.
`ISaveWorkflowDefinitionCommand` and `IMaterializeWorkflowDefinitionCommand` carry definition metadata only (name,
description, deleted flag), and `IDiscardDraftCommand` and `IDeleteWorkflowDefinitionPermanentlyCommand` remove state;
none writes workflow state.

**Rationale**: business rules live in the application layer; stores keep only storage integrity. At the entry
points that admit incoming state (draft save, add-version, submit, reconciliation, git export, publish) the rule is a
pure function of that state and immutable catalog versions, so nothing is read from the store between the check and
the write. Promote is the exception: it checks stored state and writes it later, in-lock, so the check and the write
are split across layers. The content precondition (point 1) closes that window without moving the rule: the
command refuses to promote anything but the content the endpoint admitted. Draft save blocks because the admission throws before the command; that is the deliberate exception to the
non-blocking draft convention (spec clarification), and `DraftValidating` keeps its non-blocking contract for every
other validator. Promote is admitted like every other entry point, so it does not depend on how the promotion
command was composed.

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
- An `IDraftValidator` only: records without blocking at draft save, which the spec rules out. For promote it blocks
  only when the command's optional `IInlineEventPublisher` is composed, so it cannot be the enforcement there either.
  It is registered here for reporting only.

**Residual, stated plainly**: like `RequiredInputOutputValidator`, the validator skips nodes whose activity version
the catalog cannot resolve, so it cannot tell whether their inputs are credentials. In the uninstalled-activity
case, draft save, promote, add-version, submit, file reconciliation and git export all accept such a node. A draft
or a version holding a literal on that input can therefore be stored, and exported to git. Storage does not stop it,
and the rule cannot: what keeps it from running is publish. Publish must compile every node and cannot compile an
activity version the catalog does not hold (`WorkflowExecutableCompiler` reads it through
`IActivityDefinitionVersionStore.GetWithDefinitionAsync`, which in the EF store throws `EntityNotFoundException`,
verified), and once
the activity is installed, publish and every other entry point apply the rule. The existing
`UnknownActivityVersionValidator` also blocks promote of such a draft, but only where the promotion command's
optional in-lock gate runs. T112 (A22) proves both halves (spec FR-008 and its edge case state this).

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
the console or its own logger, values it returns as outputs, and values it puts in private state or bookmark payloads
are not covered by masking in phase 0. Phase 1's redacting value type addresses them. For the built-ins, R12 refuses a
`Secret` binding on every input the R3a search found returned, copied or persisted (IP14 to IP22), so what remains is
`WriteLine.Text` on the console and a server that reflects `SendHttpRequest`'s `Authorization` value in its response
(R17). The canary activity must not do any of these, and the PR must say so.

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
OpenTelemetry package is centrally managed); and a test activity with three string inputs and these modes: record
and complete, throw with the value, return a fault with the value, suspend then resume, and throw with the value on
resume. Its inputs are `Primary` (declared credential), `Companion` (undeclared, bound to a non-secret literal) and
`Plain` (undeclared, so its pinned contract policy is not sensitive; S10 binds the canary secret here). A minimal
structural test activity whose child-completion callback throws with a resolved value covers the structural fault
boundary.

**Canary input names avoid every name-based redactor (round 3).** `DefaultDiagnosticSnapshotFactory` redacts any
value whose name, with `-` removed, contains `password`, `secret`, `token`, `apikey`, `api_key`, `authorization` or
`credential`, case-insensitively (`SensitiveNameFragments`, verified). An input named, say, `ApiKey` would be redacted
by name, so a disabled P6 or P7 would stay green for the wrong reason. `Primary`, `Companion` and `Plain` contain
none of them, nor any of the OpenTelemetry redactor's `SensitiveNames` (`authorization`, `token`, `password`,
`secret`, `api-key`, `apikey`, `cookie`, `connection-string`, `connectionstring`); the canary activity's type name and
the secret's reference name avoid them too, and T078 asserts it.

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
| Diagnostic snapshots | the companion input's inspection projection holds a non-empty diagnostic snapshot for the canary run (the factory ran), and the secret-bound input's own inspection projection exists for that run and shows the withheld marker with the reference name; in S10 that projection also reports `isSensitive: false` with a capture decision at the configured level, so it was eligible for capture |
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
withheld, the same decorator in a second mode that lowers the policy of the `SecretRead` binding on `Plain` to
`{IsSensitive: false, RequiresEncryption: false}` before delegating (an input-only lowering is not enough:
`ActivityExecutionInspection.BuildInputValueSnapshots` computes `isSensitive` as
`value.Policy.IsSensitive || input.Policy.IsSensitive`, reading the pinned contract's policy too, verified. The
contract policy is the input's owner storage policy combined with the authored `ArgumentState.IsSensitive`
(`ExecutableNodeCompiler.BuildActivityContract` through `CompileActivityPolicy`, verified), so `Plain`, undeclared and
authored without `isSensitive`, has a contract policy that is not sensitive, and S10's positive control asserts it.
With the lowered binding producer withholding,
the commit backstop and every sensitive-value rule stand aside and the protection under test is the only one left;
this is the shape a hand-built imported artifact can already have, R13, so the runtime protections must hold under
it), an `IRuntimePayloadCapturePolicy` double that captures every payload, a decorator over the invoke scheduler work
handler that throws a planted value after the handler returns (the only way an exception reaches a runtime span,
since handlers record activity faults themselves), and a hand-built runtime artifact imported without publish
(R13), which is how an encryption-required literal reaches the runtime. Each injection scenario asserts its
own positive control (the planted value is present where the injection put it) and excludes that surface from its
scan. The full list is [A15](contracts/acceptance-proof-matrix.md#per-protection-bite-proof-a15).

**Alternatives considered**: the Workbench process harness (`tests/essentials/Workbench/Tests/WorkbenchProcess.cs`)
composes the real host, but has no way to load a test activity; the existing embedded runtime host has no design,
publishing, git export or Runtime.Api.

## R11: Which input types can hold a secret (edge case)

**Decision (revised in plan review round 3)**: a resolved secret is text. `CompileSecretInput` resolves the binding's
conversion plan with the existing `ValueConversionPlanResolver`
(`src/essentials/Workflows/Publishing/Services/ValueConversionPlanResolver.cs`) from source type `String` and
source representation `TextValue` to the input's declared type, passing the authored conversion request through as
the literal paths do. Publish refuses the binding exactly when the resolver has no plan, with the resolver's existing
`VF-COER-001` diagnostic (`ValueConversionPublicationException`, carrying node and input). That is the one rule: it
does not depend on the secret's type or on whether the host composes the Secrets bridge.

What the resolver does with that source (verified in `ValueConversionPlanResolver.Resolve` and
`ValueConversionCompatibility`, `src/essentials/Workflows/Runtime/Core/Models/ValueConversionCompatibility.cs`), under
the default mode `Auto`:

- **Accepted**: a single `String` input (`Identity`), its nullable alias (`NullableCompatibility`), and a single
  canonical any-typed input, alias `Elsa.Any`, `Any` or `JsonNode` (`CanonicalAny`, which accepts a `TextValue`
  source).
- **Refused at publish** (`AutomaticUnsupported`, or `AutomaticCollectionShapeAmbiguous` for collections): every other
  type. That includes the numeric types, `Boolean`, `Char`, `Guid`, the date and time types, `TimeSpan`, enums and
  `Uri`; `Object`, the alias CLR `object` maps to (`TypeAliasConvention`), which is not a canonical any alias;
  `JsonElement` and `JsonObject`, because the `elsa.json` profile needs a `FormattedContent` or `StructuredValue`
  source; and every collection, including a collection of `String`, because `Auto` has no single-to-collection plan.
- **Explicit modes**: `Json`, `Xml` and `Profile` require a `FormattedContent` source and are refused
  (`ExplicitProfileRequiresFormattedContent`); `None` requires an exact `String` target.

So an `rsa-key` bound to an integer input is refused at publish, as the spec's edge case asks, and so is a `text`
secret bound to an integer, `TimeSpan`, `Object` or `JsonElement` input. At run time, `TypeMismatch` is the Secrets
resolver's code for a stored secret whose type differs from the reference's `typeName` (US2.3). A conversion failure
at activation is reported with its own code, `ConversionFailed` (R4), as a backstop: none of the three plans above fails on a string
value, so it is reachable only through an artifact that skipped publish (R13).

A credential input accepts only a `Secret` binding, so a credential whose type has no plan from text could never be
bound. The scanner therefore refuses a credential declaration on a property whose CLR type is not `string` (R5,
FR-006).

**Why the round 2 design was withdrawn**: round 2 added a second rule, `VF-ACT-013`, refusing a structured-text
secret (`rsa-key`, `x509-certificate`) on numeric, boolean, date/time and enum inputs and collections of them, backed
by an optional `IRuntimeSecretTypeDomains` contract the bridge implemented from `SecretTypeNames`. It assumed a text
secret could reach a numeric input and convert at run time, and that `Object`, `JsonElement` and string collections
accept text. The resolver has no plan from text to any of those types, so `VF-ACT-013` could never fire (rule 1
refuses first), its bite-proof (remove the check, the `rsa-key` to integer row goes red) could not bite, and the
accepted rows for `Object`, `JsonElement` and string collections would have failed. `VF-ACT-013`, the contract, its
enum and the bridge implementation are withdrawn. The code `VF-ACT-013` is not reused.

**Alternatives considered**: coerce the text into the target at activation the way a literal is coerced at publish
(number parsing, JSON parsing). FR-004 asks for the existing input conversion rules, and for a value that only exists
at run time those are the resolver's pinned plans; a second, host-side coercion path would be a conversion system
the published plan does not pin. Hard-coding secret type names in publishing: no longer needed, since no rule
depends on the secret's type.

## R12: Secret bindings on activity kinds that cannot resolve at the point of use

**Decision**: Publish refuses a `Secret` binding, with `VF-ACT-012` and a fixed message naming the node and input
(and, for the attribute, value-outcome and variable cases, the reason), in these cases (spec FR-001):

- intrinsic nodes (Set Variable, Set Output and the other `WorkflowIntrinsicKind` values);
- nodes whose activity descriptor consumer is not `elsa.clr-activity`, which today means graph activities
  (`WellKnownRuntimeActivityConsumers.GraphActivity`);
- CLR activity types that implement `IRuntimeActivityCheckpointParticipant` (today only `GraphActivity` implements
  it, so this case guards future CLR participants);
- an input the CLR activity type names in a new class-level attribute,
  `[RefusesSecretBinding(inputKey, reason)]` (`Elsa.Activities.Runtime.Core.Attributes`, `AllowMultiple`), with reason
  `PersistedByActivity` (the activity copies the hydrated value into its own persisted state, R3a IP14),
  `FixedAtPublish` (a publish-time reader needs a literal, R3a IP15 to IP19) or `EchoedToOutput` (the activity returns
  the value in its result or copies it into a fault it reports, R3a IP22, round 3);
- the input named by the type's existing `[ActivityValueOutcomes]` attribute (R3a IP20), which needs a literal at
  publish;
- a variable's initial value (R3a IP21), which is persisted in the variable frame.

**The general mechanism (round 2)**: nothing in the code tells publish today that an activity persists a hydrated
input or reads it at publish; the five literal readers and `ForEach` each decide it privately. The attribute makes it
a declaration on the activity type, read at publish through `ExecutableNodeCompiler.ResolveClrActivityType`, the same
reflection path that already reads `[ResumeTarget]` and `[ActivityValueOutcomes]` (verified). It is the way any
activity author opts an input out of secret binding; it is not special-casing `ForEach`. Phase 0 applies it to the
built-ins found by the R3a search: `ForEach.Collection`; `For.Start`, `End` and `Step`; `DispatchWorkflow`
`WorkflowDefinitionId`, `Inputs` and `CorrelationId`; `PublishEvent` `EventName`, `CorrelationId` and `Payload`;
`Delay.Duration`; the eight `HttpEndpoint` inputs of IP15; `Event` `EventName`, `CorrelationId` and
`CanStartWorkflow`; `Cron.Expression`; `Timer.Interval`; `BpmnProcess.CanStartWorkflow`; and, with
`EchoedToOutput`, `Inline.Expression`, `WriteHttpResponse` `Body` and `ContentType`, `BpmnDecision.Outcome`, and
`Fault` `code`, `message`, `category` and `faultType`. `ExecutableNodeCompiler` checks the attribute before
`RuntimeInputBindingCompiler` resolves the binding's conversion plan, so an attributed input reports `VF-ACT-012`
even where R11's type rule would also refuse it (`Inline.Expression` is `Object`). The scanner never writes the
attribute into the catalog, so these activities keep their catalog hash (R5). Each literal reader also handles a
`SecretRead` binding explicitly, throwing the fixed `VF-ACT-012` message instead of treating it as unauthored or
throwing its generic "no literal" error (T007's audit rule), so a missing attribute is still loud. Text an activity
writes from other inputs into private state, bookmarks or outputs remains the activity author's responsibility
(R9 limits).

A credential input accepts only a `Secret` binding, so it must not sit in any of these places: the catalog refuses
such a declaration (R5).

**Rationale**: each of them hands its inputs to code that persists them or needs them fixed at publish. Intrinsics write their value into a
durable variable or workflow output (`WorkflowIntrinsicExecutor`). Graph activities skip input hydration and capture
their inputs as boundary input durable values (`GraphActivityScope.CaptureInputsAsync`), which the boundary retry
later clones. Checkpoint participants receive their inputs through `MaterializeCheckpointInputsAsync` and may write
them into checkpoint state. Inputs named by `[RefusesSecretBinding]` are copied into persisted state, returned in
a persisted result or fault, or read at publish, and variable defaults are persisted in variable frames. Resolving a secret into any of these would persist
it, and a publish-time reader has no value to read, so the binding is refused where the author can act on it, the
same loud-over-silent choice as for intrinsics. The spec scopes resolution to activity
inputs at the point of use. R3a lists every path and which ones this rule closes.

## R13: Entry points the spec does not list

The runtime artifact import paths (`src/essentials/Workflows/Runtime/Reconciliation`, artifact import through
publishing) carry already-compiled bindings and are not among FR-008's seven entry points, and they skip the
publish rules of R8 (`VF-ACT-011`) and R12 (`VF-ACT-012`). An imported artifact with a literal on an
encryption-required input reaches the runtime with that policy. R8's producer withholding then withholds it at
materialization, and activation refuses it with `VF-ACT-010`, which is loud. The literal still sits in the imported
file, and an artifact whose own policy does not require encryption on that input is not caught at all. The spec
records runtime artifact import as out of scope for phase 0 (Assumptions). Recommendation: a follow-up issue for an
import-time check (T090). Not planned here. The canary uses this path on
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

## R16: Why eleven slices instead of six

The expected shape had six slices. The code changed it in four places, and the owner decision added one:

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

- `SendHttpRequest` consuming a secret (owner decision 2026-10-01, R17) is slice 11. It changes a shipped activity's
  contract and needs resolution, the bridge, the credential declaration and the canary's encoded scanner, so it lands
  after slice 9 rather than inside slice 5.

Slice 1 (housekeeping), slice 9 (canary) and slice 10 (Studio) keep their expected roles.

## R17: SendHttpRequest consumes a secret (owner decision 2026-10-01)

**Decision**: phase 0 makes one built-in activity consume a secret end to end. `SendHttpRequest`
(`src/essentials/Activities/Http/Activities/SendHttpRequest.cs`) gains an optional input:

```csharp
[ActivityInput(Key = nameof(Authorization), DisplayName = "Authorization", IsCredential = true)]
public string? Authorization { get; set; }
```

It is a credential (R5), so it accepts only a `Secret` binding or no binding: a literal or expression is refused at
the seven entry points (`Inputs/CredentialLiteral`), and its effective policy is `{IsSensitive, RequiresEncryption}`.
Its type is `string`, so a `Secret` binding gets an `Identity` plan (R11). After `AddHeaders` copies `RequestHeaders`,
a non-empty `Authorization` removes any existing `Authorization` header from the request (`HttpRequestHeaders` names
are case-insensitive, so a `RequestHeaders` key spelled `authorization` is removed too) and adds the value verbatim
with `TryAddWithoutValidation`, which never throws and so never puts the value into exception text. The value is the
whole header value (for example `Bearer <token>`); the activity adds no scheme. A null or empty `Authorization` leaves
`RequestHeaders` untouched. Precedence, stated once: the `Authorization` input wins over a `RequestHeaders` entry.
Phase 1's http Connection replaces this input; Elsa 4 is unreleased, so no compatibility path is kept.

**What the activity records (verified)**: the result is `SendHttpRequestResult(StatusCode, ResponseBody,
ResponseHeaders)`. `ResponseHeaders` is built by `CollectHeaders` from `response.Headers` and
`response.Content.Headers`, never from the request, and no output echoes a request header. The activity has no logger.
A transport failure (`HttpRequestException`) and a timeout complete with `SendHttpRequestResult.NoResponse` and an
outcome, recording no exception text. Two things remain outside the activity's control and are spec assumptions,
not protections: a server that reflects the `Authorization` value in its response body or headers puts it into the
persisted result, and an `Authorization` entry typed into `RequestHeaders` is an ordinary literal, because
`RequestHeaders` is not a credential input. The tests use a local endpoint that asserts the header and answers
without repeating it, for exactly that reason.

**Logs**: the outbound request goes through the named `IHttpClientFactory` client `Elsa.Activities.Http`
(`ActivitiesHttpFeature`). The factory's logging handlers can write request headers at `Trace` level. Whether
`Microsoft.Extensions.Http` 10.0.10 (the pinned version) redacts header values by default is not asserted here; the
slice proves it with a capture test at `Trace` level (T106). If that test finds the value, `ActivitiesHttpFeature`
adds `RedactLoggedHeaders` for the `Authorization` header on that client, without narrowing whatever the default
already redacts.

**The other inputs are not regressed**: `Method`, `Content` and `ContentType` (string) keep their behavior and may
take a `Secret` binding like any string input; `Url` (`Uri`), `RequestHeaders` (a dictionary) and `Timeout`
(`TimeSpan`) cannot take one (R11); `ExpectedStatusCodes` refuses one (IP20). `SendHttpRequestExecutionTests` and the
behavioral `SendHttpRequestDrive` keep passing unchanged.

**Catalog and version (Model X)**: the new input changes `SendHttpRequest`'s catalog content and its
`DefaultActivityDefinitionHasher` hash. Built-ins carry no `[Version]` attribute (verified: no `[Version(` use in
`src/`), so `ActivityTypeVersionResolver` takes the version from the assembly's informational version. Published
packages are versioned per ADR 0067: a package whose files changed gets a new computed patch, so a host upgrading
`Elsa.Activities.Http` reconciles a new activity version and `ActivityVersionReconciler` appends it; no
`ActivityVersionHashMismatchException`. A dev build keeps the SDK's default informational version
(`PackageVersioning.props`), so a design catalog reconciled by a dev build from before this change holds the same
version with a different hash, and reconciliation throws `ActivityVersionHashMismatchException`, exactly as it does
for any other change to a built-in's inputs. Elsa 4 is unreleased: no `[Version]` attribute is added (it would pin
this one activity off its package's version line), no migration is written, and a dev catalog from an earlier build
is recreated. The committed contract-surface baseline
(`tests/essentials/Activities/Design/Tests/Contracts/activity-contract-surface.baseline.json`) is regenerated in the
same PR, and published executables pinned to the old contract keep it.

**Proof**: T105 (unit: header applied, precedence, empty value, result and catalog flags), T106 (logs), T108 (end to
end through the canary host: the local endpoint received the header, rotation took effect, and the encoded scanner
finds the value in no persisted or emitted surface). A23 lists the bite-proofs.

## Authority and follow-ups

- The design document lists an ADR and glossary entries as follow-ups of the whole model. This plan adds glossary
  entries for the two new terms ("credential input", "withheld value") in the slices that introduce them, and leaves
  the ADR to the design document's own follow-up.
- Performance measurement is retired (#1668, ADR 0073): no benchmark, perf test or perf gate is planned.
- Elsa 4 is unreleased: no migration or compatibility shim. Definitions that already hold literals on newly guarded
  inputs are refused on their next save or publish.
