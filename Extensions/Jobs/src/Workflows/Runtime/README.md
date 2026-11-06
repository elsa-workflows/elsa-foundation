# Jobs native workflow adapter

This optional package adds an awaited `RunJob` saved boundary and an independent `SubmitJob` checkpoint boundary. Standalone Jobs Core, client, worker and
EF provider keep their standalone package closure. This adapter references public native runtime
contracts and adds no runtime-to-Design dependency or handler implementation selection.

Source implementation and source tests are prepared for M3. The native/Design/BPMN target builds
passed at the recorded Generation E source; runtime cases remain unexecuted. M2 remote qualification
is a prerequisite for M3 qualification. Native/BPMN packaged
journeys, nested/independent/detached cases, interruption/recovery and UX acceptance remain
unqualified. See [the feature tasks](../../../../../specs/1218-standalone-jobs/tasks.md).

`RunJob` requires the published `jobs.handler.v1` descriptor. The optional Design contributor pins
literal handler/version, input/result schemas, queue, execution policy and association mode. Runtime
compares the same literal selections with the descriptor without resolving an installed handler.
Materialized `Input` and `NotBefore` are captured once. Inputs copied into this durable request refuse
direct Secret binding; fixed selections refuse it at publication. `RunJob` supports only Awaited. `SubmitJob` requires an explicit Independent or Detached pin.

The activity suspends first. `BookmarkCreated` enrichment captures the actual bookmark, registration,
opaque resume target, full native executable and invocation contract. The same checkpoint freezes
`jobs.native.admission.v1` and a durable start intent. The Jobs association's continuation holds the
address, without recursively embedding its request. A protected origin and immutable request digest
recognize sender replay even after ordinary producer-key expiry. Private native identities reuse the
Core canonical payload digest with a fixed native schema, so equivalent JSON object property
reordering preserves continuation/delivery IDs while raw numeric lexemes retain their identity. Cancellation settles that exact
origin/tombstone before or after acceptance and never waits on physical handler exit or result ACK.
The start sender first reads the exact protected origin, so a known acceptance or cancellation can
confirm a lost reply after its native wait retires; unknown state still requires the actual wait.

The read-only preclaim handler and a fresh resume activation independently compare the canonical
selected result and exact existing continuation permission. A permission-shaped payload has no
authority on its own. Before trigger claim, missing/corrupt retained admission or unavailable
persistence access defers the original queued resume with its original diagnostic cause. Deliberate
malformed/conflicting incoming payloads retain their separate refusal policy. Existing grants remain readable after ACK and reusable after a later Jobs
workflow hold. Queue/type holds govern attempts; Jobs workflow-instance/definition holds govern new
continuation grants. No handler attempt is created by result redelivery.

An undefined, oversized or unsupported-depth materialized Job input can fail before any saved wait
or JobId exists. That path returns an explicit `jobs.native.admission-invalid` native fault with a
versioned digest over its exact material, contract and initial attempt. The inspector recognizes it
only from the matching final native fault/attempt chain. Missing metadata is never this proof.
Material/header/owner/store unavailability remains retryable scheduler responsibility and is not
converted into that native-only business failure. Native-owner cancellation similarly records an
explicit cancellation phase before its origin source; a consumed JobResult still needs its grant.

At the consumed resume-claim checkpoint the adapter freezes `jobs.native.delivery.v1`, including the
actual typed delivery and claimed native attempt, before closed-attempt payload retirement. Final
proof additionally checks the consumed receipt/attempt chain and successful typed completion or
normalized fault marker. Native cancellation alone does not prove a still-live parent consumed a
JobResult. A missing header, activity or model root never becomes terminal proof.

Direct native cancellation and consumed-result terminal checkpoints add a separate required
`Elsa.Jobs.Settlement.v1` source. Cancellation itself confirms immediately; settlement remains owed
until the eventual canonical selected result and exact Jobs acknowledgement are retained. A
cancelled, non-retired protected origin without acceptance is positive pre-admission proof; an
unknown or retired origin is not. The source reads exact acknowledged state after an uncertain ACK
reply, without using the proposed JobId hint or a transport response as proof. Pending required
sources must retain native history through the runtime deletion fence; that provider/runtime
retention change and its race controls are coordinated separately.

`AddJobsWorkflowsRuntime` registers hooks by default. Enable `EnableResultDelivery` and supply explicit
trusted `DeliveryScopes` to run one bounded, paced result loop. Configure one Jobs command/query
authority for the same store. The optional shell `JobsWorkflowsRuntimeFeature` depends on Tasks and
workflow resumption and starts this delivery loop; it does not enable a Jobs handler worker.

A coordinator host can use `AddElsaJobs(options => options.EnableWorker = false)` alongside the Jobs
provider, native workflow host and `AddJobsWorkflowsRuntime` with result delivery enabled. If the
shell also includes `JobsFeature`, set its `EnableWorker=false` on the coordinator. The actual Jobs
worker lives in another host with registered handlers. No handler registry or CLR handler is needed
to publish or coordinate this native boundary.

The loop reads one finite page per trusted scope, rotates scopes and always delays. `DeliveryPageSize`
defaults to 32 (1–100; configure it within the selected provider's page bound), `PollInterval` to 250ms
(10ms–1min), and cooperative `OperationTimeout` to 30s (100ms–10min), separately for each page query and each
delivery item. Each item uses a fresh scope; its timeout keeps that item owed and allows the next
item on the same page to progress. It holds no physical handler
slots and owns no retries, occurrence cursor or result store. Tasks and ordinary hosting share its
one lifecycle instance. Stop cancels reads/dispatch cooperatively and waits for that loop to exit.
Unknown replies re-read Jobs-owned responsibility; dispatch/duplicate/queued statuses never ACK.

For a native worker with `ModelWorkOrigin`, exactly one `IJobWorkflowAssociationSource` must resolve
the actual model root. The adapter never falls back to helper-native ownership. Model address JSON
is opaque here. A model source proves exact root input consumption or positive root terminality;
helper completion is insufficient. No native-worker ACK intent waits for root consumption, avoiding
a cancellation settlement cycle. The optional BPMN package uses the public native inspector and its
own typed owner reader, with no parsing of private BPMN state by this package. Its existing excluded
result source calls `IJobNativeResultSettlement.SettleWorkerAsync` after its positive root/input
decision and before source delivery completes. A rejected input while the model root remains live
cannot acknowledge Jobs. No second native settlement source is emitted for `ModelWorkOrigin`.

## Cross-domain contributions

`JobsCheckpointEnricher` contributes deterministic `IRuntimeCheckpointCommitEnricher` changes.
`JobsNativeIntentHandler` handles `Elsa.Jobs.Start.v1` and `Elsa.Jobs.Cancel.v1` postcommit intents with
UntilAcknowledged retry and explicit durable-source delivery. Before sender effects, the adapter
re-reads the physically committed source, scope, intent fingerprint and live claim; a bare intent or
working-set lookup cannot authorize delivery. `JobsNativeSettlementIntentHandler` handles direct-native
`Elsa.Jobs.Settlement.v1` with the same retry policy. `JobsNativeResumeValidator` handles `ActivityResumeValidating` sequentially
and read-only before native claim/activation. A shared startup validator contributes `IStartupTask`
and `IHostedService`; the optional delivery worker contributes `IBackgroundTask` and `IHostedService`
through one instance. See the [native runtime catalog](../../../../../Essentials/src/Workflows/Runtime/Elsa.Workflows.Runtime/EXTENSION_POINTS.md)
and [Tasks catalog](../../../../../Essentials/src/Tasks/Elsa.Tasks/EXTENSION_POINTS.md).


`SubmitJob` completes without a bookmark or result-delivery registration. It returns a typed
`SubmitJobResult` whose stable `JobId` is a submission address and whose `AdmissionState` explicitly
remains `PendingAdmission`; it is not `JobAcceptance`. The bounded opaque `Submission` field retains
the once-materialized input, logical handler/policy pin, full executable/contract and actual nested
invocation owner. The same native completion checkpoint freezes `jobs.native.submission.v1` and a
required `Elsa.Jobs.Submit.v1` outbox source. A handler result is never needed to continue this parent.

The sender rechecks its actual physical source and retained completion, then admits the origin-protected
request even after normal parent completion. Actual acceptance must name that deterministic JobId;
a different canonical winner is an owed conflict rather than a substituted success. The public
`IJobNativeSubmissionInspector` proves canonical Jobs acceptance from the exact protected origin,
retained request and native final completion chain. Missing state or a pending origin never becomes
acceptance. A model bridge must prove that acceptance before consuming the helper's completion.

Independent submission inherits explicit parent cancellation and survives normal completion/fault.
Its additional required `Elsa.Jobs.SubmissionOwnership.v1` source remains owed while the exact owning
parent is active, preserving native history through the existing required-source deletion fence even
when admission is already Delivered. Explicit cancellation creates or replays the exact CancelOrigin
responsibility; normal terminality releases that lifetime responsibility after admission is confirmed.
Detached submission keeps the same exact model/definition/version/invocation pause association but
has no inherited cancellation or owner-lifetime source. Both modes use null continuation and create no
Jobs result-delivery/ACK obligation. A model-owned helper's completion is not its root's lifetime:
its selected association source must additionally implement `IJobWorkflowSubmissionLifetimeSource`.

A positive native owner-cancellation phase also covers cancellation before the first invocation.
It records the exact full pinned cancellation marker; a missing submission alone never proves zero
admission. Source controls for these paths are unexecuted until the immutable native/BPMN qualification
runs. The public inspector's phase classification and cancellation receipt do not grant model authority.

Optional Jobs-store payload expiry does not erase model responsibility. Live awaited delivery still
requires retained input/result bytes and cannot be purged before exact Jobs ACK. Already-settled
source/ACK replay and independent origin reconciliation use validated compact canonical identities,
association and original digests. If a native source still retains its original completion bytes,
the adapter can authenticate those bytes against compact settled metadata after store expiry; it
does not reconstruct a successful null result or persist expired bytes again. Missing expiry or
consumption proof remains unavailable. Native source/history retention follows its existing required
responsibility fence and is not shortened by the Jobs payload policy.

Ordinary whole-workflow cancellation also covers a created root `RunJob` helper whose initial
activation was durably claimed before its Jobs wait/admission checkpoint. The cancellation enricher
records a bounded positive receipt tied to that existing activation claim, the exact cancellation
commit/checkpoint/work item/command, full native/model pins, tenant, contract and invocation. It
requires the single initial attempt still open and no saved wait, private state, completed activation,
admission or result effects; it does not invent an ended attempt. Inspection and settlement accept
only that exact receipt. Missing admission metadata alone remains unavailable. This receipt proves
no committed Jobs handoff, not the timing of preparatory CLR instructions. The separate actual BPMN
pre-wait witness must establish its stronger checkpoint-before-handler cut and resulting cleanup.
