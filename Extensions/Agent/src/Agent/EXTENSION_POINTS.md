# Agent Foundation extension points

Owner project: `Elsa.Agent`. Contract project: `Elsa.Agent.Core`. Domain: `Elsa.Agent`.

This is the domain catalog for framework §2.22.1. The [root index](../../../../EXTENSION_POINTS.md) links here. Signatures and models live in [Core contracts](../Core/Contracts/) and [Core models](../Core/Models/); the [candidate contribution contract](../../../../specs/1018-weaver-module-contributions/contracts/contributions.md) records the current #3437 limits. The independently packaged extension proof in #3438 still gates contract freeze.

## Composition and lifetimes

`FoundationAgentAbstractionsFeature` in `Elsa.Agent` retains the configured feature name `FoundationAgentAbstractions`. Its virtual registration calls `services.AddFoundationAgentAbstractions()` idempotently. API, workflow and provider features inherit this feature and declare Type dependencies. The base declares the string dependency `Events`; consumers must explicitly discover ordinary Events infrastructure. Missing Events fails composition.

`Elsa.Agent.Core` contains contracts, models, named collecting events and thin provider bridges. Active defaults and registration are in `Elsa.Agent`; modules contribute through Core without referencing these defaults. Context collectors, catalogs, registries, invoker, proposal facade/executor, policy, authorizer, streaming and provider registry are scoped. The scoped tool registry caches only its own collected tools, never a cross-request tool instance.

`InMemoryAgentSessionService`, `InMemoryAgentFeedbackService`, `InMemoryAgentAuditStore`, `AgentTurnRegistry`, `InMemoryAgentTurnStateStore` and `InMemoryAgentProposalStore` are process-local singleton state. The proposal store holds data, synchronization and retained results; scoped facades resolve current tools and authority. It captures no tool instances, callbacks or `HttpContext`. `TimeProvider.System` is registered with `TryAddSingleton` for execution expiry. These defaults provide no durable restart or distributed coordination guarantee.

## Overridable contracts

All contracts below have layer badge **Core — `Elsa.Agent.Core`**. Defaults are internal to `Elsa.Agent` unless another project is named. Override one contract with `services.Replace(...)`; additive module contribution belongs in the next section. The linked contract files are the signature authority.

| Contract | Default and lifetime | Signature surface | When to override; dependants |
|---|---|---|---|
| [`IAgentSessionService`](../Core/Contracts/AgentContracts.cs) | `InMemoryAgentSessionService`, singleton | Create/find sessions; add/find/list messages and context; set message status | Supply persistent session/message storage. API, context sources, turn orchestration and proposal authorization consume it. |
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
| [`IAgentTurnStateStore`](../Core/Contracts/AgentTurnContracts.cs) | `InMemoryAgentTurnStateStore`, singleton | `Save(AgentPausedTurn)`, `Find(sessionId)`, `Remove(sessionId)` | Replace paused-turn retention. Orchestrator consumes the stored proposal/tool/history correlation after review. |
| [`IAgentFeedbackService`](../Core/Contracts/AgentContracts.cs) | `InMemoryAgentFeedbackService`, singleton | `AddAsync(AgentFeedback)` returns `Task<AgentFeedback>` | Supply feedback persistence. API consumes it; default emits audit linkage. |
| [`IAgentAuditSink`](../Core/Contracts/AgentContracts.cs) | `InMemoryAgentAuditStore`, singleton shared with reader | `EmitAsync(AgentAuditEvent)` returns `Task` | Supply audit persistence/forwarding. Sessions, context, tools, proposals, feedback and provider diagnostics emit records. |
| [`IAgentAuditReader`](../Core/Contracts/AgentContracts.cs) | Same `InMemoryAgentAuditStore` | `ListAsync(sessionId?, take?)` returns `Task<IReadOnlyCollection<AgentAuditEvent>>` | Supply audit queries consistent with the sink. API audit views consume it. |
| [`IAgentProviderRegistry`](../Core/Contracts/AgentContracts.cs) | `DefaultAgentProviderRegistry`, scoped | `Active: IAgentProvider?` | Replace provider selection only while preserving the single-active-provider contract. Orchestrator/API consume it; multiple installed providers fail with `AgentHarnessConflictException`. |
| [`IAgentProvider`](../Core/Contracts/AgentContracts.cs) | No provider in base registration | Provider/session identity, `CreateSessionAsync`, `ContinueTurnAsync(AgentTurnContext)`, approval and diagnostics | Install one provider adapter through its feature. The orchestrator consumes it; zero providers is unavailable, never a deterministic production fallback. |

`Elsa.Agent.Api` calls `services.AddFoundationAgentApi()` to replace the fail-closed `IAgentToolAuthorizer` with scoped `HttpAgentToolAuthorizer`. It uses the current normalized authenticated principal/tenant and exactly one `IPermissionAuthorizationService`, checking the descriptor's required permissions against the prepared target. Custom/non-HTTP hosts must install an equivalent current-authority replacement; installing a tool alone grants no execution authority.

`IAgentHarness` *(Core — `Elsa.Agent.Core`)* is an optional companion bridge on the selected `IAgentProvider`, not another provider registration. Its [signature](../Core/Contracts/AgentHarnessContracts.cs) exposes `Capabilities` and `RunTurnAsync(AgentHarnessTurnRequest)`. The orchestrator delegates that provider's loop while its tools still pass through `IAgentToolInvoker`. Otherwise it drives `ContinueTurnAsync` itself. Both paths transport typed module results without interpreting their contents.

Known provider implementations:

- `GitHubCopilotAgentProvider` (`Elsa.Agent.GitHubCopilot`) *(intra-domain — provider adapter)* implements `IAgentProvider` and `IAgentHarness`; its feature registers the provider with `TryAddEnumerable(ServiceDescriptor.Scoped<IAgentProvider, GitHubCopilotAgentProvider>())`.
- `AnthropicAgentProvider` (`Elsa.Agent.Anthropic`) *(intra-domain — provider adapter)* implements `IAgentProvider` for the host step loop; its feature uses the corresponding scoped enumerable registration.
- `DeterministicAgentProvider` (`Elsa.Agent.Tests`) *(intra-domain — test fixture)* and the private journey `ScriptedProvider` *(cross-domain — qualification fixture)* are not shipped defaults. Their existence does not establish live-provider coverage.

## Implementable contributor interfaces

### `IAgentContextProvider` *(Core — `Elsa.Agent.Core`)*

- **Kind:** Source; the existing interface name is retained. [Signature](../Core/Contracts/AgentContracts.cs): `Descriptor: AgentContextProviderDescriptor`, derived `ScopeKind`, and `CollectAsync(AgentContextRequest): ValueTask<AgentContextContribution>`.
- **Register:** `services.TryAddEnumerable(ServiceDescriptor.Scoped<IAgentContextProvider, MyContextSource>())`.
- **Consumed by:** the single `CollectAgentContext` handler for `AgentContextCollecting`. Modules register sources rather than additional fan-in handlers.
- **Ownership:** descriptor declares module, supported context kinds/schema versions, resource types and scope. The module admits/minimizes caller material, authorizes its own reads, supplies installed guidance and returns typed diagnostics. Caller origin/scopes are reset before source admission; only the installed source can contribute admitted scopes. `Descriptor.ScopeKind = "*"` opts an installed source into ambient collection for every request; it does not admit arbitrary owners, kinds, versions or permissions.
- **Known implementations:** `DefaultWorkflowAgentContextProvider` (`Elsa.Agent.Workflows`) *(cross-domain)*; private journey `NoteContextSource` (`Weaver.Journey.Backend`) *(cross-domain — qualification fixture, not shipped)*.

### `IAgentCapabilityProvider` *(Core — `Elsa.Agent.Core`)*

- **Kind:** Source. [Signature](../Core/Contracts/AgentContracts.cs): `GetCapabilitiesAsync(): ValueTask<IReadOnlyCollection<AgentCapability>>`.
- **Register:** `services.TryAddEnumerable(ServiceDescriptor.Scoped<IAgentCapabilityProvider, MyCapabilitySource>())`.
- **Consumed by:** the single `CollectAgentCapabilities` handler for `AgentCapabilitiesCollecting`. Advertise demonstrated supported journeys and limitations; discovery is not a permission grant.
- **Known implementations:** `WorkflowAgentCapabilityProvider` (`Elsa.Agent.Workflows`) *(cross-domain)*; private journey `NoteCapabilities` (`Weaver.Journey.Backend`) *(cross-domain — qualification fixture, not shipped)*.

### `IAgentTool` *(Core — `Elsa.Agent.Core`)*

- **Kind:** Installed-tool contribution. [Signature](../Core/Contracts/AgentToolContracts.cs): `Descriptor: AgentToolDescriptor`, `PrepareAsync(AgentToolInvocation): Task<AgentResult<AgentToolPreparation>>`, and `ExecuteAsync(AgentPreparedToolInvocation): Task<AgentResult<AgentToolResult>>`.
- **Register:** `services.TryAddEnumerable(ServiceDescriptor.Scoped<IAgentTool, MyTool>())`.
- **Consumed by:** the single `CollectAgentTools` handler for `AgentToolsCollecting`, then the existing async `IAgentToolRegistry`. Duplicate names, missing owner/version and malformed input schemas fail discovery; per-turn context/current authority determine availability.
- **Ownership:** the module validates canonical arguments, exact target/revision, required context and risk during side-effect-free preparation. It supplies versioned preview/result schemas and renderer identity, applies through its owning service/native transaction, rechecks resource constraints and returns actual read-back evidence. `AgentToolPreparation`, `AgentPreparedToolInvocation`, `AgentPayload`, `AgentResourceTarget` and `AgentEffectReceipt` are defined in [Core contribution models](../Core/Models/AgentContributionModels.cs); the generic engine freezes and binds preparation rather than defining module payload meaning. Both provider paths use the existing thin `AgentToolAIFunctions` bridge.
- **Known implementations:** `WorkflowReadGraphTool` and `WorkflowSetDesignerPositionTool` (`Elsa.Agent.Workflows`) *(cross-domain)*; private journey `SetNoteTool` (`Weaver.Journey.Backend`) *(cross-domain — qualification fixture, not shipped)*. The fixture is not the independent #3438 package-consumer proof.

### `IAgentClientTool` *(Core — `Elsa.Agent.Core`)*

- **Kind:** Optional completion contract on the same installed `IAgentTool`. [Signature](../Core/Contracts/AgentContributionContracts.cs): `CompleteAsync(AgentPreparedToolInvocation, AgentClientAcknowledgement): Task<AgentResult<AgentToolResult>>`.
- **Register/consumed by:** implement on the tool already registered as `IAgentTool`; no separate completion registry or fan-in handler. `DefaultAgentActionProposalExecutor.CompleteClientAsync` resolves that same tool/owner/version and rechecks current authority/lifetime before completion.
- **Ownership:** validate/redact the browser's observation against the exact preparation and return a typed local working-state receipt. Completion admits evidence; it never executes the operation again. The module's private browser binding owns current selection/revision/generation, native application, validation, rendering and undo semantics.
- **Known implementation:** `WorkflowSetDesignerPositionTool` (`Elsa.Agent.Workflows`) *(cross-domain)*. See the [Workflow Agent catalog](../Workflows/EXTENSION_POINTS.md) for its supported subset.

The unrelated-resource examples above are the private [owned journey backend fixture](../../../../tools/studio/Composition.Probe/fixtures/WeaverJourney.Backend.cs.in), not a shipped resource module or completed independent-consumer qualification.

## Events

All three [Core events](../Core/AgentContributionsCollecting.cs) implement `IEvent`. Delivery is **Sequential** through `IInlineEventPublisher`: the publisher awaits contributions and unexpected exceptions/cancellation propagate. Expected source limitations use `AgentContributionDiagnostic`; a required diagnostic blocks context admission. Each output collection is initialized and get-only, with direct mutable collection access for its one aggregator.

| Event | Meaning and payload | Contributor / single aggregator | Publication site |
|---|---|---|---|
| `AgentContextCollecting` | Collect applicable module context. `Request: AgentContextRequest`; `Attachments: ICollection<AgentContextAttachment>`; `Diagnostics: ICollection<AgentContributionDiagnostic>` | `IAgentContextProvider.CollectAsync` / `CollectAgentContext` | `DefaultAgentContextCollector.CollectAsync`, before sanitization and final host policy |
| `AgentCapabilitiesCollecting` | Collect supported capability descriptors. `Capabilities: ICollection<AgentCapability>` | `IAgentCapabilityProvider.GetCapabilitiesAsync` / `CollectAgentCapabilities` | `DefaultAgentCapabilityCatalog.ListAsync` |
| `AgentToolsCollecting` | Collect installed tools before per-turn admission. `Tools: ICollection<IAgentTool>` | Registered `IAgentTool` / `CollectAgentTools` | `DefaultAgentToolRegistry.ListAsync`, once per scoped registry |

`AddFoundationAgentAbstractions()` registers those aggregators with `TryAddEventHandler<TEvent, THandler>()`. Modules add typed contributors, preserving one aggregator per contribution event. Duplicate context source/attachment and capability identities are diagnosed, as are duplicate tool identities by the registry. No background event or catch-and-ignore collection fallback is introduced.

## Current candidate limits

The #3437 implementation admits at most one distinct mutating tool call per session/turn; identical delivery coalesces and conflicting delivery is rejected. One module-owned call can contain its native atomic batch. Server reads execute inline; reads that request review or client execution are explicitly unsupported. Server mutations, including FullAuto, share the same prepared proposal/effect pipeline. Every client working-state mutation requires explicit review, including FullAuto.

Client execution returns HTTP 202 with `awaiting-client`, `executed: false` and the exact prepared request. Only a matching authenticated observation accepted by the same installed tool can mark it Executed. Receipt continuation receives the retained result with no available tools and cannot start another mutation or replay the original prompt. Before dispatch a refusal has no effect; after dispatch cancellation, missing/invalid receipt or lost delivery can mean uncertainty. Retained duplicates cannot rerun effects or replace accepted evidence.

All lifecycle retention and deduplication here are in memory. Restart, cross-process coordination, durable history/approvals and broader recovery remain M2 work. This catalog describes the candidate surface; completed composed-host, package-consumer and live-provider evidence must be recorded on their owning issues and is not inferred from these registrations.
