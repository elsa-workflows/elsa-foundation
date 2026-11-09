# Agent Foundation extension points

Owner project: `Elsa.Weaver`. Contract project: `Elsa.Weaver.Core`. Domain: `Elsa.Weaver`.

This is the domain catalog for framework §2.22.1. The [root index](../../../../EXTENSION_POINTS.md) links here. Signatures and models live in [Core contracts](../Core/Contracts) and [Core models](../Core/Models); the [M1 contribution contract](../../../../specs/1018-weaver-module-contributions/contracts/contributions.md) records the accepted minimum supported by the [76ec independent package witness](../../../../specs/1174-weaver-package-witness/evidence/qualification-76ec58d00.json). [M1 final integration](https://github.com/elsa-workflows/elsa-foundation/issues/3438#issuecomment-6539679143) is verified Done on main `ab46bc22a`; the broader Program remains open.

## Composition and lifetimes

`WeaverFeature` in `Elsa.Weaver` retains the configured feature name `Weaver`. Its virtual registration calls `services.AddWeaver()` idempotently. API, workflow and provider features inherit this feature and declare Type dependencies. The base declares the string dependency `Events`; consumers must explicitly discover ordinary Events infrastructure. Missing Events fails composition.

`Elsa.Weaver.Core` contains contracts, models, named collecting events and thin provider bridges. Active defaults and registration are in `Elsa.Weaver`; modules contribute through Core without referencing these defaults. Context collectors, catalogs, registries, invoker, proposal facade/executor, policy, session policy service, authorizer, streaming and provider registry are scoped. The scoped tool registry caches only its own collected tools, never a cross-request tool instance.

`InMemoryAgentSessionService` (which also owns policy revisions and work claims), `InMemoryAgentFeedbackService`, `InMemoryAgentAuditStore` (sink, reader and audit ownership), `AgentTurnRegistry`, `InMemoryAgentTurnStateStore` and `InMemoryAgentProposalStore` are process-local singleton state. The proposal store holds data, synchronization and retained results; scoped facades resolve current tools and authority. It captures no tool instances, callbacks or `HttpContext`. `TimeProvider.System` is registered with `TryAddSingleton` for execution expiry. These defaults provide no durable restart or distributed coordination guarantee. Since #3441 the session owner writes through `IAgentSessionRecordStore`; the opt-in [EF store](../Persistence/EntityFrameworkCore/EXTENSION_POINTS.md) makes that record durable ([below](#durable-recovery-3441-preview)).

## Overridable contracts

All contracts below have layer badge **Core — `Elsa.Weaver.Core`**. Defaults are internal to `Elsa.Weaver` unless another project is named. Override one contract with `services.Replace(...)`; additive module contribution belongs in the next section. The linked contract files are the signature authority.

| Contract | Default and lifetime | Signature surface | When to override; dependants |
|---|---|---|---|
| [`IAgentSessionService`](../Core/Contracts/AgentContracts.cs) | `InMemoryAgentSessionService`, singleton | Create/find sessions; add/find/list messages and context; set message status | Supply persistent session/message storage. API, context sources, turn orchestration and proposal authorization consume it. The default also owns the session policy revision and work claims; a replacement makes autonomy updates Unsupported ([below](#session-policy-and-owner-scoped-audit-3440-preview)). |
| [`IAgentSessionPolicyService`](../Core/Contracts/AgentContracts.cs) *(preview, #3440)* | `DefaultAgentSessionPolicyService`, scoped | `GetAsync(sessionId, actorId, tenantId)` and `ChangeAutonomyAsync(AgentSessionAutonomyChangeRequest)` return `Task<AgentSessionPolicyResult>` | Normally not replaced: a replacement cannot join the default claims, so it must own the same ordering against admitted work or return `Unsupported`. API create/detail readback and the autonomy route consume it. |
| [`IAgentPolicyEvaluator`](../Core/Contracts/AgentContracts.cs) | `DefaultAgentPolicyEvaluator`, scoped | `EvaluateAvailabilityAsync`, `EvaluateContextAsync`, `EvaluateCapabilityAsync` return `ValueTask<AgentPolicyDecision>` | Extend host policy. Context admission, API, invoker and executor consume it; policy does not grant resource permissions. |
| [`IAgentContextSanitizer`](../Core/Contracts/AgentContracts.cs) | `DefaultAgentContextSanitizer`, scoped | `SanitizeAsync(attachments)` returns `ValueTask<IReadOnlyCollection<AgentContextAttachment>>` | Add generic minimization/redaction. The collector sanitizes installed contributions before final host policy and provider use. |
| [`IAgentContextCollector`](../Core/Contracts/AgentContracts.cs) | `DefaultAgentContextCollector`, scoped | `CollectAsync(policy, AgentContextRequest)` returns `Task<AgentResult<AgentContextContribution>>` | Replace admission orchestration while preserving installed-source admission, diagnostics, minimization and policy. API/session input uses it. |
| [`IAgentCapabilityCatalog`](../Core/Contracts/AgentContracts.cs) | `DefaultAgentCapabilityCatalog`, scoped | `ListAsync()` returns `Task<IReadOnlyCollection<AgentCapability>>` | Replace catalog assembly. API capability discovery consumes it; modules normally add sources. |
| [`IAgentToolRegistry`](../Core/Contracts/AgentToolContracts.cs) | `DefaultAgentToolRegistry`, scoped | Async `ListAsync()` and `FindAsync(toolName)` return installed `IAgentTool` instances | Replace discovery while retaining the named fan-in and identity validation. Orchestrator, invoker and executor share this registry; there is no second execution registry. |
| [`IAgentToolAuthorizer`](../Core/Contracts/AgentContributionContracts.cs) | `UnavailableAgentToolAuthorizer`, scoped, denies every request | `AuthorizeAsync(descriptor, actorId, tenantId, target)` returns `Task<AgentPolicyDecision>` | Supply current server identity, tenant and resource permission integration. Invoker and executor recheck it; modules can also use it before authorized reads. Caller metadata and admitted scopes never grant permission. |
| [`IAgentToolInvoker`](../Core/Contracts/AgentToolContracts.cs) | `DefaultAgentToolInvoker`, scoped | `DispatchAsync(AgentToolInvocation, policy)` returns `Task<AgentToolDispatch>` | Replace generic preparation/policy dispatch while preserving exact binding and the proposal effect pipeline. Both orchestrator and harness tool bridges consume it. |
| [`IAgentProposalService`](../Core/Contracts/AgentContracts.cs) | `InMemoryAgentProposalService`, scoped over singleton `InMemoryAgentProposalStore` | Add/find/approve/deny/execute proposals; `AcknowledgeAsync(sessionId, invocationId, actorId, acknowledgement)`; `FindResultAsync(proposalId)` | Supply durable review/result coordination. API and invoker use the facade; orchestrator resumes from its retained actual result. Preserve idempotency and pending-versus-applied state. |
| [`IAgentActionProposalExecutor`](../Core/Contracts/AgentContracts.cs) | `DefaultAgentActionProposalExecutor`, scoped | `ExecuteAsync(proposal)` and `CompleteClientAsync(proposal, acknowledgement)` return `Task<AgentResult<AgentProposalExecutionResult>>` | Replace effect routing while preserving installed tool/version, expiry, current authorization and receipt checks. Proposal service consumes it. The default executes the owning tool or returns explicit unavailable; no success-reporting no-op remains. |
| [`IAgentStreamingService`](../Core/Contracts/AgentContracts.cs) | `DefaultAgentTurnOrchestrator`, scoped | `StreamAsync(sessionId)` returns `IAsyncEnumerable<AgentStreamEvent>` | Replace generic turn coordination. Streaming API consumes it; providers retain their existing host-step or harness bridge. |
| [`IAgentTurnRegistry`](../Core/Contracts/AgentTurnContracts.cs) | `AgentTurnRegistry`, singleton | `Register`, `Cancel`, `Unregister` take session/turn identity; `IsActive(sessionId)` | Replace active-turn lease/cancellation tracking. API and orchestrator use it to reject a concurrent foreground turn. |
| [`IAgentTurnStateStore`](../Core/Contracts/AgentTurnContracts.cs) *(changed, #3441)* | `InMemoryAgentTurnStateStore`, singleton, bound to the default session owner | `SaveAsync(AgentPausedTurn)`, `FindAsync(sessionId)`, `RemoveAsync(sessionId)` | Replace paused-turn retention. Orchestrator consumes the stored proposal/tool/history correlation after review. The default keeps the pause on the owner's session record, so saving it takes the Pause claim; a replacement keeps autonomy support through the orchestrator's explicit pause claim, but cannot recover paused turns: with the in-memory default that composition reports `unsupported`, and with a durable store selected it fails shell activation (`AgentDurableRecoveryValidator`). |
| [`IAgentSessionRecordStore`](../Core/Contracts/AgentContracts.cs) *(preview, #3441)* | `InMemoryAgentSessionRecordStore`, singleton, round-trips every record as JSON | `CreateAsync`, `LoadAsync` (hydrate), owner/fence-guarded atomic `WriteAsync` that extends the lease, `AcquireOwnershipAsync`/`ReleaseOwnershipAsync`, all-or-nothing unique keys, per-record proposal `Version` compare-and-set (`VersionConflict`), `FindProposalSessionAsync`, `ListOwnedAsync`, `FindOwnerAsync` (plain owner columns; default reads `LoadAsync`); `SupportsFencing` | Supply durable backing for the default session owner, which writes through it and is not replaced, so autonomy stays supported. Select a store with `services.SelectAgentSessionRecordStore(...)` (`AgentPersistenceSelection`), which replaces only the process-local default and throws on a second selection; a second registration fails `AgentRegistrationValidator` at shell activation and before the default lifecycle services resolve. A store that cannot fence always fails shell activation (`AgentDurableRecoveryValidator`). Known implementation: `EfAgentSessionRecordStore` (`Elsa.Weaver.Persistence.EntityFrameworkCore`, feature `WeaverEntityFrameworkCore`) *(intra-domain — opt-in EF Core store)*. |
| [`IAgentRecoverySupport`](../Core/Contracts/AgentContracts.cs) *(preview, #3441)* | `DefaultAgentRecoverySupport`, scoped, over the composition's durable-recovery evaluation | `Describe()` returns an `AgentRecoveryModes` value: `durable`, `processLifetime` or `unsupported` | Normally not replaced: only the default can see whether the default owner participates with every default lifecycle service over a fencing store. Bootstrap reads it for `recovery` and reports `unsupported` when it throws. At most one registration; a second fails `AgentRegistrationValidator`. |
| [`IAgentFeedbackService`](../Core/Contracts/AgentContracts.cs) | `InMemoryAgentFeedbackService`, singleton | `AddAsync(AgentFeedback)` returns `Task<AgentFeedback>` | Supply feedback persistence. API consumes it; default emits audit linkage. |
| [`IAgentAuditSink`](../Core/Contracts/AgentContracts.cs) | `InMemoryAgentAuditStore`, singleton shared with reader | `EmitAsync(AgentAuditEvent)` returns `Task` | Supply audit persistence/forwarding. Sessions, context, tools, proposals, feedback and provider diagnostics emit records. |
| [`IAgentAuditReader`](../Core/Contracts/AgentContracts.cs) | Same `InMemoryAgentAuditStore` | `ListAsync(sessionId?, take?)` returns `Task<IReadOnlyCollection<AgentAuditEvent>>` (trusted, unscoped, in-process); preview default member `ListOwnedAsync(AgentAuditOwnerQuery)` returns `Task<AgentAuditQueryResult>`, `Unsupported` by default | Supply audit queries consistent with the sink. The API audit route reads only through `ListOwnedAsync`; a reader that does not implement it makes the route answer 409 `weaver.audit.query_unsupported`. |
| [`IAgentAuditOwnership`](../Core/Contracts/AgentContracts.cs) *(preview, #3440)* | Same `InMemoryAgentAuditStore`, singleton | `AssociateAsync(sessionId, actorId, tenantId)` returns `Task<bool>`; first association wins | Replace together with the reader so association and owner query stay coherent. The default session service associates each session before emitting any record for it. |
| [`IAgentProviderRegistry`](../Core/Contracts/AgentContracts.cs) | `DefaultAgentProviderRegistry`, scoped | `Active: IAgentProvider?` | Replace provider selection only while preserving the single-active-provider contract. Orchestrator/API consume it; multiple installed providers fail with `AgentHarnessConflictException`. |
| [`IAgentProvider`](../Core/Contracts/AgentContracts.cs) | No provider in base registration | Provider/session identity, `CreateSessionAsync`, `ContinueTurnAsync(AgentTurnContext)`, approval and diagnostics | Install one provider adapter through its feature. The orchestrator consumes it; zero providers is unavailable, never a deterministic production fallback. A provider reports an explicit outcome of its own (for example a conversation reset) as an `AgentProviderNotice(Code, Label)` payload on a `Progress` event; the turn records it, and it is never an accepted outcome *(preview, #3441)*. |

The audit sink, reader and ownership are one store's facets and are replaced together, with `services.SelectAgentAuditStore(...)` (`AgentPersistenceSelection`), which replaces the three default facets at once and throws on a second selection; a second registration of any of the three fails `AgentRegistrationValidator`. `EfAgentAuditStore` (`Elsa.Weaver.Persistence.EntityFrameworkCore`) persists the records and the owner association, so owner-scoped audit reads keep answering for recovered sessions; `settledBy` (`user` or `recovery`) is a metadata key.

`Elsa.Weaver.Api` calls `services.AddWeaverApi()` to replace the fail-closed `IAgentToolAuthorizer` with scoped `HttpAgentToolAuthorizer`. It uses the current normalized authenticated principal/tenant and exactly one `IPermissionAuthorizationService`, checking the descriptor's required permissions against the prepared target. Custom/non-HTTP hosts must install an equivalent current-authority replacement; installing a tool alone grants no execution authority.

`IAgentHarness` *(Core — `Elsa.Weaver.Core`)* is an optional companion bridge on the selected `IAgentProvider`, not another provider registration. Its [signature](../Core/Contracts/AgentHarnessContracts.cs) exposes `Capabilities` and `RunTurnAsync(AgentHarnessTurnRequest)`. The orchestrator delegates that provider's loop while its tools still pass through `IAgentToolInvoker`. Otherwise it drives `ContinueTurnAsync` itself. Both paths transport typed module results without interpreting their contents.

Known provider implementations:

- `WeaverGitHubCopilotProvider` (`Elsa.Weaver.GitHubCopilot`) *(intra-domain — provider adapter)* implements `IAgentProvider` and `IAgentHarness`; its feature registers the provider with `TryAddEnumerable(ServiceDescriptor.Scoped<IAgentProvider, WeaverGitHubCopilotProvider>())`.
- `WeaverAnthropicProvider` (`Elsa.Weaver.Anthropic`) *(intra-domain — provider adapter)* implements `IAgentProvider` for the host step loop; its feature uses the corresponding scoped enumerable registration.
- `DeterministicAgentProvider` (`Elsa.Weaver.Tests`) *(intra-domain — test fixture)* and the private journey `ScriptedProvider` *(cross-domain — qualification fixture)* are not shipped defaults. Their existence does not establish live-provider coverage.

## Implementable contributor interfaces

### `IAgentContextProvider` *(Core — `Elsa.Weaver.Core`)*

- **Kind:** Source; the existing interface name is retained. [Signature](../Core/Contracts/AgentContracts.cs): `Descriptor: AgentContextProviderDescriptor`, derived `ScopeKind`, and `CollectAsync(AgentContextRequest): ValueTask<AgentContextContribution>`.
- **Register:** `services.TryAddEnumerable(ServiceDescriptor.Scoped<IAgentContextProvider, MyContextSource>())`.
- **Consumed by:** the single `CollectAgentContext` handler for `AgentContextCollecting`. `CollectAgentTools` reads only public descriptor metadata to validate installed tool-to-source ownership. Modules register sources rather than additional fan-in handlers.
- **Ownership:** descriptor declares module, supported context kinds/schema versions, resource types and scope. The module admits/minimizes caller material, authorizes its own reads, supplies installed guidance and returns typed diagnostics. Caller origin/scopes are reset before source admission; only the installed source can contribute admitted scopes. `Descriptor.ScopeKind = "*"` opts an installed source into ambient collection for every request; it does not admit arbitrary owners, kinds, versions or permissions.
- **Unavailable owner (preview):** a source whose owner could not decide returns a required `AgentContributionDiagnostic` under its own descriptor id with `OwnerUnavailable` set and an owner code. That code must be a dotted ASCII identifier of at most 128 characters outside the reserved `agent.` namespace. When every required diagnostic of a collection is such a diagnostic, collection fails as unavailable: the owner's code, status 503 and a fixed engine message, never the diagnostic's own text. That covers the initial collection, a turn's initial and history admission, and the per-tool re-admission before dispatch. The same holds when the owner is asked about a retained lifecycle outcome (denied, refused or uncertain). Any collection failure of 500 or more from the selected `IAgentContextCollector`, a replacement included, passes through unchanged there, as on every other admission path, and a paused continuation then keeps its pause and claim and can be retried.

Within one admission call, the retained outcomes are evaluated in order, and evaluation stops at the first definite refusal. A definite refusal of any of them wins:
- provenance: `weaver.result.provenance_unavailable` with 409;
- a denial or a host capability-policy refusal: `weaver.result.disclosure_denied` with 403.

Only when no outcome is refused definitely is the first outage returned.

Across admissions the first error is returned, a 503 included. History admission of recorded messages and disclosures runs before the continuation's or tool's own outcomes, and each step can stop the turn. The final collection, with host context policy and the module sources, runs only when every retained outcome of that admission is admitted. The marker counts only on a diagnostic that the engine's own `CollectAgentContext` recorded for a source under that source's descriptor id. It is cleared on any other diagnostic, including one another `AgentContextCollecting` handler adds to the event. Every other required refusal, including a mix of definite and unavailable ones, keeps `weaver.context.incomplete` with 400. The marker is omitted from the wire unless set.
- **Known implementations:** `DefaultWorkflowAgentContextProvider` (`Elsa.Weaver.Workflows`, `internal sealed`; its server-produced `workflow.catalog` and `workflow.permissions` kinds are read again for every turn, see its [truth seams](../Workflows/Elsa.Weaver.Workflows/EXTENSION_POINTS.md#truth-seams-spec-1193); its active-draft snapshot also admits the designer's diagnostics, the selected activity's property context and graph activity references, see [explain and troubleshoot](../Workflows/Elsa.Weaver.Workflows/EXTENSION_POINTS.md#explain-and-troubleshoot-spec-1193-slice-d)) *(cross-domain)*; `WorkflowLibraryContextProvider` (`Elsa.Weaver.Workflows`, `internal sealed`; the library list, create dialog and template list, see [library, create dialog and templates](../Workflows/Elsa.Weaver.Workflows/EXTENSION_POINTS.md#library-create-dialog-and-templates-spec-1193-slice-e)) *(cross-domain)*; `WorkflowVersionContextProvider` (`Elsa.Weaver.Workflows`, `internal sealed`; one exact persisted version, read through the owning design services, see [versions](../Workflows/Elsa.Weaver.Workflows/EXTENSION_POINTS.md#versions-spec-1193-slice-f)) *(cross-domain)*; `WorkflowExecutableContextProvider` (`Elsa.Weaver.Workflows.Runtime`, `internal sealed`; one exact Executable bound to its Artifact Hash, see the [Runtime-side catalog](../Workflows/Runtime/EXTENSION_POINTS.md)) *(cross-domain)*; `WorkflowRunContextProvider` (`Elsa.Weaver.Workflows.Runtime`, `internal sealed`; one exact Run and its selected activity execution, see the [Runtime-side catalog](../Workflows/Runtime/EXTENSION_POINTS.md#runs-spec-1193-slice-g)) *(cross-domain)*; external `WidgetContextSource` (`Acme.Home.Weaver`) *(cross-domain — qualified package fixture, not shipped)*; private journey `NoteContextSource` (`Weaver.Journey.Backend`) *(cross-domain — source-composed fixture, not shipped)*.

### `IAgentCapabilityProvider` *(Core — `Elsa.Weaver.Core`)*

- **Kind:** Source. [Signature](../Core/Contracts/AgentContracts.cs): `GetCapabilitiesAsync(): ValueTask<IReadOnlyCollection<AgentCapability>>`.
- **Register:** `services.TryAddEnumerable(ServiceDescriptor.Scoped<IAgentCapabilityProvider, MyCapabilitySource>())`.
- **Consumed by:** the single `CollectAgentCapabilities` handler for `AgentCapabilitiesCollecting`. Advertise demonstrated supported journeys and limitations; discovery is not a permission grant.
- **Known implementations:** `WorkflowAgentCapabilityProvider` (`Elsa.Weaver.Workflows`, `internal sealed`) *(cross-domain)*; `WorkflowRuntimeAgentCapabilityProvider` (`Elsa.Weaver.Workflows.Runtime`, `internal sealed`) *(cross-domain)*; external `WidgetCapabilities` (`Acme.Home.Weaver`) *(cross-domain — qualified package fixture, not shipped)*; private journey `NoteCapabilities` (`Weaver.Journey.Backend`) *(cross-domain — source-composed fixture, not shipped)*.

### `IAgentTool` *(Core — `Elsa.Weaver.Core`)*

- **Kind:** Installed-tool contribution. [Signature](../Core/Contracts/AgentToolContracts.cs): `Descriptor: AgentToolDescriptor`, `PrepareAsync(AgentToolInvocation): Task<AgentResult<AgentToolPreparation>>`, and `ExecuteAsync(AgentPreparedToolInvocation): Task<AgentResult<AgentToolResult>>`.
- **Register:** `services.TryAddEnumerable(ServiceDescriptor.Scoped<IAgentTool, MyTool>())`.
- **Consumed by:** the single `CollectAgentTools` handler for `AgentToolsCollecting`, then the existing async `IAgentToolRegistry`. Duplicate names, missing owner/version, missing/ambiguous/foreign disclosure-source links and malformed input schemas fail discovery; per-turn context/current authority determine availability.
- **Ownership:** the module validates canonical arguments, exact target/revision, required context and risk during side-effect-free preparation. It supplies versioned preview/result schemas and renderer identity, applies through its owning service/native transaction, rechecks resource constraints and returns actual read-back evidence. `AgentToolPreparation`, `AgentPreparedToolInvocation`, `AgentPayload`, `AgentResourceTarget` and `AgentEffectReceipt` are defined in [Core contribution models](../Core/Models/AgentContributionModels.cs); the generic engine freezes and binds preparation rather than defining module payload meaning. Both provider paths use the existing thin `AgentToolAIFunctions` bridge.
- **Known implementations:** `WorkflowReadGraphTool`, `WorkflowSetDesignerPositionTool` and the read-only `WorkflowReadVersionTool` (`Elsa.Weaver.Workflows`) *(cross-domain)*; the read-only `WorkflowReadExecutableTool` and `WorkflowReadRunTool` (`Elsa.Weaver.Workflows.Runtime`) *(cross-domain)*; external `ReadWidgetTool` / `SetWidgetVisibilityTool` (`Acme.Home.Weaver`) *(cross-domain — qualified package fixture, not shipped)*; private journey `SetNoteTool` (`Weaver.Journey.Backend`) *(cross-domain — source-composed fixture, not shipped)*.

### `IAgentToolAuthorization` *(Core — `Elsa.Weaver.Core`)*

- **Kind:** Optional owner-authority companion on the same installed tool. [Signature](../Core/Contracts/AgentToolContracts.cs): `AuthorizeAsync(actorId, tenantId, target, cancellationToken)` returns `ValueTask<AgentPolicyDecision>`.
- **Register:** implement on the existing `IAgentTool`; no additional registry or fan-in. The shared engine requires host authority first, then this owner decision for advertisement, concrete dispatch, duplicate outcome delivery and reviewed execution/completion.
- **Ownership:** use the owning resource's native authorization and current scope. A null target is an availability hint; concrete preparation is checked again. It cannot grant authority denied by the host. Context and historical-result disclosure still use the original installed source's admission hook.
- **Unavailable owner (preview):** an owner that could not decide refuses with an `AgentViolation` whose `OwnerUnavailable` is set. Dispatch, duplicate delivery and reviewed execution then fail closed as unavailable, with that violation's code, status 503 and a fixed engine message; the violation's own message is never forwarded. The flag counts only on the tool's own `IAgentToolAuthorization` violations, never on a host `IAgentToolAuthorizer` violation, and only with an owner code: a dotted ASCII identifier of at most 128 characters outside the reserved `agent.` namespace. Every other refusal keeps `weaver.tool.forbidden` or `weaver.proposal.forbidden` with 403.
- **Known implementations:** workflow graph/read, layout and version tools delegate to the workflow domain's selected `IWorkflowDefinitionAuthorizer` (the version tool for the version's definition); the Executable tool asks the selected `IWorkflowExecutableInspection` whether the Executable still exists at its bound Artifact Hash, and the Run tool the selected `IWorkflowRunInspection` whether the Run and its selection are still visible, each under the host's `workflows/runtime:read`. Other modules can contribute their own rules without changes to Weaver, hosts or provider adapters.

### `IAgentClientTool` *(Core — `Elsa.Weaver.Core`)*

- **Kind:** Optional completion contract on the same installed `IAgentTool`. [Signature](../Core/Contracts/AgentContributionContracts.cs): `CompleteAsync(AgentPreparedToolInvocation, AgentClientAcknowledgement): Task<AgentResult<AgentToolResult>>`.
- **Register/consumed by:** implement on the tool already registered as `IAgentTool`; no separate completion registry or fan-in handler. `DefaultAgentActionProposalExecutor.CompleteClientAsync` resolves that same tool/owner/version and rechecks current authority/lifetime before completion.
- **Ownership:** validate/redact the browser's observation against the exact preparation and return a typed local working-state receipt. Completion admits evidence; it never executes the operation again. The module's private browser binding owns current selection/revision/generation, native application, validation, rendering and undo semantics.
- **Known implementation:** `WorkflowSetDesignerPositionTool` (`Elsa.Weaver.Workflows`) *(cross-domain)*. See the [Workflow Agent catalog](../Workflows/Elsa.Weaver.Workflows/EXTENSION_POINTS.md) for its supported subset.

The `Note*` examples refer to the private [source-composed journey fixture](../../../../tools/studio/Composition.Probe/fixtures/WeaverJourney.Backend.cs.in). The independent package example below has separate author/consumer evidence.

The separately authored [Acme Home fixture](../../../../tools/studio/Weaver.Conformance.Probe/fixtures/Resource.Backend) adds `WidgetContextSource` (`IAgentContextProvider`), `WidgetCapabilities` (`IAgentCapabilityProvider`), and `ReadWidgetTool` / `SetWidgetVisibilityTool` (`IAgentTool`) through `AcmeHomeWidgetFeature` *(cross-domain — qualification package)*. Its [Studio companion](../../../../tools/studio/Weaver.Conformance.Probe/fixtures/Resource.Studio) contributes public SDK context, prompt actions and result rendering. The [76ec independent witness](../../../../specs/1174-weaver-package-witness/evidence/qualification-76ec58d00.json) qualifies the actual external consumer journeys, native add/remove/incompatible/restore controls and both provider adapters at that code baseline. [Final-head review/CI, merge and resulting-main verification](https://github.com/elsa-workflows/elsa-foundation/issues/3438#issuecomment-6539679143) completed through [#3497](https://github.com/elsa-workflows/elsa-foundation/pull/3497). Durable shared lifecycle/recovery and broader Program M2–M5 coverage remain open. The [preview migration](../../../../specs/1018-weaver-module-contributions/contracts/contributions.md#preview-migration) identifies removed exports and package/namespace changes.

## Events

All three [Core events](../Core/AgentContributionsCollecting.cs) implement `IEvent`. Delivery is **Sequential** through `IInlineEventPublisher`: the publisher awaits its single aggregator and remains unshielded. The aggregator bounds individual sources and classifies their outcomes; cancellation and required unexpected failures propagate. Expected source limitations use `AgentContributionDiagnostic`; a required diagnostic blocks context admission. Each output collection is initialized and get-only, with direct mutable collection access for its one aggregator.

| Event | Meaning and payload | Contributor / single aggregator | Publication site |
|---|---|---|---|
| `AgentContextCollecting` | Collect applicable module context. `Request: AgentContextRequest`; `Attachments: ICollection<AgentContextAttachment>`; `Diagnostics: ICollection<AgentContributionDiagnostic>` | `IAgentContextProvider.CollectAsync` / `CollectAgentContext` | `DefaultAgentContextCollector.CollectAsync`, before sanitization and final host policy |
| `AgentCapabilitiesCollecting` | Collect supported capability descriptors. `Capabilities: ICollection<AgentCapability>` | `IAgentCapabilityProvider.GetCapabilitiesAsync` / `CollectAgentCapabilities` | `DefaultAgentCapabilityCatalog.ListAsync` |
| `AgentToolsCollecting` | Collect installed tools before per-turn admission. `Tools: ICollection<IAgentTool>` | Registered `IAgentTool` / `CollectAgentTools` | `DefaultAgentToolRegistry.ListAsync`, once per scoped registry |

`AddWeaver()` registers those aggregators with `TryAddEventHandler<TEvent, THandler>()`. Modules add typed contributors, preserving one aggregator per contribution event. Duplicate context source/attachment and capability identities are diagnosed, as are duplicate tool identities by the registry. No background event or global catch-and-ignore delivery policy is introduced. Optional source faults and source deadlines become visible diagnostics; they do not change other Sequential handlers.

<a id="current-candidate-limits"></a>

## Accepted M1 bounds

The M1 implementation admits at most one distinct mutating tool call per session/turn; identical delivery coalesces and conflicting delivery is rejected. One module-owned call can contain its native atomic batch. Server reads execute inline; reads that request review or client execution are explicitly unsupported. Server mutations, including FullAuto, share the same prepared proposal/effect pipeline. Every client working-state mutation requires explicit review, including FullAuto.

Client execution returns HTTP 202 with `awaiting-client`, `executed: false` and the exact prepared request. Only a matching authenticated observation accepted by the same installed tool can mark it Executed. Receipt continuation receives the retained result with no available tools and cannot start another mutation or replay the original prompt. Before dispatch a refusal has no effect; after dispatch cancellation, missing/invalid receipt or lost delivery can mean uncertainty. Retained duplicates cannot rerun effects or replace accepted evidence.

All lifecycle retention and deduplication here are in memory. Restart/shell replacement, cross-process coordination, durable history/approvals and broader recovery remain M2 work. #3441 adds them behind the record store; see [Durable recovery](#durable-recovery-3441-preview). The [owning receipt](https://github.com/elsa-workflows/elsa-foundation/issues/3438#issuecomment-6538498830) records completed package-consumer and deterministic provider-protocol evidence for 76ec; [final-head integration and resulting-main gates completed](https://github.com/elsa-workflows/elsa-foundation/issues/3438#issuecomment-6539679143). Registrations alone establish no runtime evidence, and no live-provider success is claimed.

## Session policy and owner-scoped audit (#3440, preview)

These preview members are the #3440 contract, frozen after the normal composed Studio and complete
package proofs passed. Pre-GA they may still change under ADR 0080. The [session policy contract](../../../../specs/1186-weaver-effects/contracts/session-policy.md)
records the wire shape, refusal codes, lifecycle outcomes and constitution/SemVer classification;
this section keeps only the extension rules.

The default session row owns the policy, its revision and internal work claims for prompts,
foreground turns, mutating-tool preparation, proposals and pauses. `IAgentSessionPolicyService`
reads it back and changes only `AutonomyMode`, conditionally on the expected revision. Accepted
messages (`AgentMessage.AcceptedPolicy`) and prepared proposals (`AgentActionProposal.PolicyRevision`)
keep the revision that admitted them. `AgentMessageCreateRequest.ExpectedPolicyRevision` lets a
custom admission path refuse an insertion whose authorized revision moved, with
`AgentSessionPolicyChangedException`.

**Participation.** Readback and the update are supported only when the resolved `IAgentSessionService`,
`IAgentStreamingService`, `IAgentToolInvoker` and `IAgentProposalService` are the default
participants of one owner instance. Any replacement, forwarding decorator or additive
last-registered service of those four returns `Unsupported`, with no shadow default state; admission
and turns keep their existing behavior. `IAgentTurnRegistry` and `IAgentTurnStateStore` replacements
stay supported because the orchestrator holds the claims around their calls. Only the default
registry's busy refusal is known to precede a write, so a replacement registry's refusal or
registration failure leaves the foreground claim held: every current-revision autonomy change on that
session, including a no-op, returns `weaver.session.busy` until the session expires (then
`weaver.session.closed`) or the process restarts. Likewise, an accepted prompt that never reaches a
terminal status (for example, a prompt whose stream is never opened) keeps its prompt claim; that
prompt already blocked new prompts before #3440. `IAgentActionProposalExecutor` is not a participant.
Only the default executor's engine-private pre-dispatch cancellation is recorded as Cancelled; any
failure a replacement returns, including the `weaver.proposal.cancelled` code, is recorded as a Failed
zero-effect refusal.

**Lifecycle disclosure.** For the engine's receipt-less `weaver.operation-outcome` envelope
(`denied`, `refused` or `uncertain`) of a mutating invocation, the owning module's
`IAgentContextProvider.AdmitDisclosureAsync` decision, not the tool's mutation authority, now
governs whether the outcome continues the originating turn. Module disclosure sources should admit
these envelopes for their own invocations and targets while read authority remains.

**Owner-scoped audit.** `ListOwnedAsync` filters by the associated actor and tenant and the
optional session, and orders newest first before the limit (1..`AgentAuditOwnerQuery.MaxTake`). It
never returns a record without an ownership association. A custom session service must call
`IAgentAuditOwnership.AssociateAsync` before emitting records, or its sessions stay ineligible.

## Durable recovery (#3441, preview)

The [recovery contract](../../../../specs/1189-weaver-recovery/contracts/recovery.md) (a candidate until its
qualification) records the wire shape, refusal codes and SemVer classification; this section keeps the
extension rules. Workflow-runtime agent continuation and its recovery stay with Program #2892 (leaf #2912) and
Program #3519; this store owns only the durability of Weaver foreground sessions, reviews and receipts, and adds no
detached turn owner.

**Record store and participation.** The default session owner writes every transition through
`IAgentSessionRecordStore` before applying it in memory, and hydrates a session it has not loaded before
acting on it. Durable recovery is Supported only when the session, streaming, invoker and proposal services
are the default participants of one owner (as in #3440), `IAgentTurnStateStore` is the default (it keeps the
paused turn on the owner's record, so saving the pause records the Pause claim) and the record store
`SupportsFencing`. A replaced `IAgentTurnStateStore` (now `SaveAsync`, `FindAsync`, `RemoveAsync`) or lifecycle
service keeps autonomy support where #3440 allows it, but cannot recover: with the in-memory default the composition
reports `unsupported`, and with a durable store selected it fails shell activation (`AgentDurableRecoveryValidator`).
A custom store that cannot fence always fails activation. Configuring the
EF feature gives the owner a durable store and does not replace it. Bootstrap reports the composition's support as
`recovery` (`durable`, `processLifetime` for the in-memory default, or `unsupported`), and shell activation fails
with a diagnostic when a durable store is configured into an Unsupported composition, rather than running it partly
durable.

**Ownership.** Each process has one owner token. Hydration takes ownership when the record has no live
owner, its lease lapsed or this process owns it; a takeover increments the fence. Every write is guarded on
owner and fence and extends the lease (EF: database time, 30 seconds by default); while a foreground turn
runs or a dispatched execution's executor is still running the lease is renewed every 10 seconds (an operation that only
waits on a client or on its owner keeps the session busy but does not renew its lease). A graceful host stop, or the drain of a
reloaded shell generation, gives every loaded session's lease back before the shell's container is disposed (the
`ReleaseAgentSessionLeasesTerminator` shell terminator), so the next process takes those sessions over at once;
only a crashed process's leases have to lapse. A live lease elsewhere answers the
retryable 409 `weaver.session.owned_elsewhere` with a retry hint; losing ownership mid-call answers
`weaver.session.ownership_lost`, evicts the row and cancels the turn. Authorization reads only the plain owner
columns (`FindOwnerAsync`, `FindSessionIdAsync`) before anything decodes, hydrates or takes ownership.

**Recovery outcomes.** Hydration settles what the recorded facts already decide, in one write, before
anything acts on the session: a message that was streaming becomes Cancelled with `StatusReason`
`interrupted`; a pending prompt keeps its Prompt claim until the owner starts or withdraws it; an approved or
pending proposal past its deadline is Expired once with zero effect and `settledBy=recovery`; Executing, or a
client request past its deadline, becomes Uncertain, never Expired (an outcome the store could not record after
dispatch settles that one operation the same way, without touching the session or other work in flight); an approved proposal executes only on an
explicit execute after the story 2.3 rechecks (a stale policy revision answers 409
`weaver.proposal.policy_stale`); a paused turn with a receipt continues once. Recorded state that is
unreadable, corrupt or fails verification answers 409 `weaver.session.unavailable_state` with its reason and
no effect. Recovery with no authenticated user records only lifecycle outcomes.

**What is recoverable (FR-001).** With a durable store: session identity, owner, status and expiry;
workspace and `ContinuedFrom`; the acknowledged policy and its revision; messages and their history,
including accepted-policy snapshots, delivery fingerprints and status reasons; each distinct disclosure
snapshot with its byte-identical canonical text and the digest references that form the provenance ledger;
retained session context; proposals with their exact authorized requests, approval bindings, statuses,
results, receipts, acknowledgement digests and owner resolutions; the paused turn with its pause
fingerprint; the call, invocation, per-turn mutation and call-input keys; and the audit records with their
owner association. Work claims are rebuilt from these facts at hydration, never stored. **Not recoverable:**
provider runtime objects and SDK sessions (the Copilot adapter may start a new conversation and says so),
open stream connections, cancellation sources and in-memory gates, hidden reasoning, credentials, and
unsaved browser working state. With the in-memory default nothing survives a process restart.

## Context and result disclosure (#3439, under qualification)

The existing `AgentContextCollecting` fan-in also admits retained evidence through
`AgentContextRequest.Disclosure`. The same installed `IAgentContextProvider` implements
`AdmitDisclosureAsync(AgentContextDisclosureRequest, CancellationToken)`; no second registry is
introduced. Its default returns a required unsupported-disclosure diagnostic. The host stamps
`AgentContextAttachment.ContributorId`; callers cannot confer that provenance. Original source ID,
module, context kind and schema must still match an installed contribution.

A tool declares `AgentToolDescriptor.DisclosureSourceId`. Preparation freezes this ID into the
reviewed invocation; the engine stamps the actual invocation, owner, contract and target onto each
observed result's `Access`. Result disclosure must resolve that exact installed source, even when
there are no retained context attachments. A module ID alone does not choose an arbitrary source.
Missing support or changed ownership is an explicit limitation, not permission to send the result.

The source owns current native resource authorization, guidance, minimization and the meaning of
historical evidence. Successful disclosure authorizes the supplied historical evidence and original
results unchanged; its returned attachments are the permitted current projection. It may withhold
an obsolete snapshot during receipt continuation, without changing the receipt or treating it as a
fresh read. Required authority failures block provider use. Source optionality never makes an
unavailable disclosure gate permissive. Current host policy also governs original retained evidence,
including history retained by a provider SDK, as well as the returned projection.
Before each SDK callback or host batch operation, current admission and tools are rechecked. A
projection that removes a scope cannot leave an older callback authorized. A changed owner resource
identity or revision requires fresh intent rather than silently moving the accepted turn. Adding
authorized detail targets while retaining the original binding is permitted.

Generated transcript and paused state retain the complete disclosure history separately from the
original request target and the current executable projection. The additive Core value
`AgentDisclosureSnapshot(Context, ToolResults)` preserves each whole snapshot, including different
versions with the same attachment ID. `AgentMessage`, `AgentMessageCreateRequest` and `AgentPausedTurn`
add nullable `IReadOnlyList<AgentDisclosureSnapshot> DisclosureHistory`. Historical source hooks
authorize those originals unchanged before later provider handoffs and callbacks; their returned
historical projection never replaces retained dependencies. Inherited result dependencies do not
become fresh execution results or change receipt-continuation semantics.

Custom session stores must round-trip this field and existing provenance. Generated assistant
`DeliveryFingerprint` covers content, original context, outcomes and disclosure history. Paused
state also adds nullable `DisclosureHistoryFingerprint`, a canonical checksum of its complete state
excluding the checksum itself. These checks detect incomplete or changed storage; they are not
authorization, signatures or protection against a malicious store recomputing them. Missing legacy
protected assistant/pause history refuses further provider use while accepted effect receipts remain
retained. Empty unprotected legacy messages remain supported. A turn with resource evidence but no
visible answer still retains a metadata-only journal, omitted from chat and provider message text.

Disclosure history is a verified write-ahead checkpoint: the engine retains a metadata-only row
before a provider handoff or a callback consumes newly admitted evidence, and retains an observed
tool outcome before returning it to a provider. A foreground fingerprint cache avoids appending
another checkpoint when its complete dependencies are unchanged; it does not use caller request
identities for deduplication. Current authority is still checked before every handoff. A failed
terminal answer insert therefore cannot erase the dependencies already exposed to an SDK session.
Pending projections remain separate from the last verified checkpoint, so a known failed insert
before any disclosure does not later journal that unexposed projection in cleanup.

A successful checkpoint is followed by current owner/host admission of the exact retained
dependencies before provider entry, callback dispatch or result delivery, including the UI result
events. Persistence grants no authority. Tool admission uses a fresh ordinary source projection
after the write, so withdrawn scopes remove tools while still-authorized explanation can continue.
That projection determines the tool set without replacing the checkpointed provider context. Dispatch
retains the checkpointed context and refuses changes to its generic ownership, schema, target,
client, sensitivity or scope bindings; an authorized refresh of opaque content, summaries or
references does not replace that frozen payload or require byte-for-byte equality. Canonical target
identity is module, resource type, resource ID and revision, matching preparation/receipt checks;
authorized display-name and route refreshes preserve the checkpointed presentation for dispatch.

Pre-disclosure writes honor foreground cancellation. Terminal and accepted-effect journals retain
their non-cancellable truth policy. Cleanup always releases the foreground lease, and does not
retry a known failed terminal insert or replace a primary domain failure with another journal
attempt. State removal, source status and lease release are each attempted even when another
cleanup operation fails. Cleanup failures cross the same Core boundary with the original primary
and cleanup causes retained in the inner exception chain. A store that commits and then reports failure has an uncertain write outcome; retaining
its evidence conservatively does not authorize provider use. Cooperative cancellation is supported;
durable recovery and uncooperative-store timeout policy remain outside this process-local contract.

Unexpected disclosure infrastructure failures cross `IAgentStreamingService` as the existing Core
`AgentContributionException`, retaining the original `InnerException` and localizing session and
operation. This is distinct from expected authority refusals and missing-provenance outcomes.
The API SSE endpoint is the higher transport caller: it logs the domain cause on the server and
sends a fixed redacted error. Custom in-process stream callers must choose their own logging,
retry or abort policy. SDK callbacks receive only safe cancellation when a disclosure fails; the
original domain cause remains observable to the stream caller. No new public exception type,
error payload or storage port is introduced.

The foreground boundary invokes replaceable stores, catalogs, registration, provider enumeration
and tool dispatch through delegates, covering synchronous throws as well as failed tasks. Distinct
primary causes remain retained if provider disposal or terminal cleanup also fails. A receipt
journal's known missing-metadata refusal remains bounded; an unexpected store failure crosses the
Core exception boundary with its original cause, retaining the accepted receipt and pause for a
supported retry. A failed read before source ownership is known does not guess a source to mutate.

The generic engine bounds retained disclosure history to 64 distinct complete snapshots and 8 MiB
of serialized evidence. It deep-copies snapshots and deduplicates by their complete canonical value,
never by attachment ID. A bound refuses another disclosure before handoff; it never evicts a prior
dependency to continue. These constants bound repeated SDK callbacks as well as host-driven turns;
they do not introduce durable retention or recovery guarantees.

`AgentContextOptions.SourceTimeout` defaults to10seconds and requires a positive finite supported
timer interval. Backend descriptors default `Required=true`. Optional collection faults/deadlines
produce bounded diagnostics; missing required evidence blocks use, and required unexpected faults
are wrapped in `AgentContributionException`. Completed source data is snapshotted before the next
source runs. Stop terminates waiting without requiring source cooperation; late results never
rejoin admission. These source boundaries do not shield unrelated Sequential event handlers.

The [owning feature](https://github.com/elsa-workflows/elsa-foundation/issues/3439) records runtime
qualification and the [scoped contract changes](../../../../specs/1176-weaver-turn-context/contracts/context-lifecycle.md).
The shared API additions also introduce a deliberate pre-GA behavior change: an older contribution
without disclosure support fails closed before another provider disclosure. Module authors must add
the source hook and explicit result link, and custom session stores must retain journal provenance.
The public workflow context provider additionally has a source/binary constructor break: direct
consumers must rebuild and supply `IAgentSessionService` and `IAgentToolAuthorizer`, or resolve the
provider through the supported workflow feature registration. See the scoped contract for the exact
old/new signatures. This is not an announcement of a release or a frozen full Program contract.
