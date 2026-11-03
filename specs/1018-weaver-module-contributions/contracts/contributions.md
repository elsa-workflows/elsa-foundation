<a id="candidate-contribution-contract"></a>

# M1 Contribution Contract

Status: accepted M1 minimum, supported by the independent packed-module witness at `76ec58d0061135cfa028e1bc47ec7639fd139e7f` ([owning receipt](https://github.com/elsa-workflows/elsa-foundation/issues/3438#issuecomment-6538498830), [curated evidence](../../1174-weaver-package-witness/evidence/qualification-76ec58d00.json)). This implementation PR records that bounded contract; final-head qualification, CI, independent review, merge and resulting-main verification remain open in [#3497](https://github.com/elsa-workflows/elsa-foundation/pull/3497). Follow existing [Studio extension contracts](../../../Studio/CONTEXT.md) and [review gates](../../../docs/contributing/review-checklist.md). Module resource schemas remain module-owned.

## Package composition

Core exposes contracts, models, named events and thin bridges. New `Elsa.Agent` owns active defaults and `FoundationAgentAbstractionsFeature`; its configured name stays `FoundationAgentAbstractions`. API/provider/workflow features inherit it, declare Type dependencies and call idempotent base registration. The base declares string dependency `Events` in `new object[] { "Events" }`; the consumer explicitly discovers ordinary Events infrastructure. Missing Events is a diagnosed composition failure, not a promise that a leaf recursively discovers it. Deterministic providers are test fixtures.

Default implementation types are internal except genuine consumer feature/extension APIs; tests use domain-owned visibility. The matching package closure and independently authored consumers are recorded in the witness; the preview migration below remains a source migration.

### Preview migration

The #3437 preview API changes form part of this M1 minimum. Rebuild existing integrations against matching backend and Studio SDK artifacts; removed types have no binary-compatibility guarantee.

| Previous surface | Current integration |
|---|---|
| `Elsa.Agent.Core` supplied defaults and `AddFoundationAgentAbstractions` | Hosts reference `Elsa.Agent` and import `Elsa.Agent.Extensions`. Contract-only module authors continue referencing `Elsa.Agent.Core`; interfaces/models remain under `Elsa.Agent.Core.Contracts` and `.Models`. |
| `Elsa.Agent.Core.FoundationAgentAbstractionsFeature` | `Elsa.Agent.FoundationAgentAbstractionsFeature`, in package `Elsa.Agent`. The configured feature name stays `FoundationAgentAbstractions`. Discover ordinary `Events` infrastructure in the host. |
| Subclasses/imports of `Elsa.Agent.Core.Services` defaults | Defaults now belong to `Elsa.Agent.Services` and are internal. Register an implementation of the public Core contract rather than depending on a default's concrete type. |
| SDK `StudioAiContextAttachment` / `StudioAiContextProviderContribution` | `StudioAgentContextAttachment` / `StudioAgentContextProviderContribution` through `api.agent.contextProviders`; a collector can return `StudioAgentContextCollection` with explicit diagnostics. |
| SDK `StudioAiSurfaceContribution` / `StudioAiToolContribution` | Use the generic `StudioAgentSurface` request context and the applicable `api.agent` capability, action, tool-slot or tool-contract contribution. Installed backend tools still own authority and execution. This is not a mechanical type rename. |
| SDK `StudioAiProposalRendererContribution`, `StudioAiProposalRendererProps`, `StudioAiProposalSummary` | `StudioAgentResultRendererContribution` / `StudioAgentResultRendererProps`, with versioned module payloads and the generic prepared invocation/result/receipt. Shared controls own approval and execution status. |
| SDK `StudioWorkflowContextActivity`, `StudioWorkflowContextDiagnostic`, `StudioWorkflowContextSnapshot` | Keep workflow document shapes in the workflow module. Contribute generic attachments with that module's target, revision, client binding and schema; the host SDK no longer defines a workflow snapshot. |
| Direct construction of `JsonShellFeatureConfigurationStore` | Supply the standard `HostBuilderContext` in addition to environment, shell settings and options. Ordinary host DI supplies it; saved feature configuration refreshes before shell activation. |

`StudioAiPromptRequest`, `StudioAiPromptResult` and `StudioAiPromptActionContribution` remain aliases for their `StudioAgent*` counterparts. `api.ai` retains prompt dispatch compatibility; it is not a second context/tool registry. Other retained `StudioAi*` names and `StudioWorkflowContextConnection` are not removed by this migration. The [SDK barrel](../../../Studio/src/essentials/Elsa.Studio.Hosting/Client/src/sdk/index.ts) is the export authority.

Build an external module against the actual `@elsa-workflows/studio-sdk` archive and mark the SDK external in its browser bundle, so runtime imports use the host's shared SDK instance. Declare `StudioModule.RequiredSdkVersion` for the host's SDK API version. Current manifest admission accepts `*`, an exact version, or a `^` value with the same major; it does not establish source compatibility between preview artifacts. Keep this admission separate from the module's tool/context/payload schema versions. The [independent witness](../../../tools/studio/Weaver.Conformance.Probe/README.md) demonstrates the unsupported-SDK control and restoration from original archives.

## Accepted author signatures

These are the existing public author seams exercised by the witness. Cancellation tokens below are optional with `default`; the linked declarations and [Agent catalog](../../../Extensions/Agent/src/Elsa.Agent/EXTENSION_POINTS.md) own the full type definitions and registrations.

| Public Core seam | Signature |
|---|---|
| [`IAgentContextProvider`](../../../Extensions/Agent/src/Core/Contracts/AgentContracts.cs) | `Descriptor: AgentContextProviderDescriptor`; derived `ScopeKind`; `CollectAsync(AgentContextRequest, CancellationToken): ValueTask<AgentContextContribution>` |
| [`IAgentCapabilityProvider`](../../../Extensions/Agent/src/Core/Contracts/AgentContracts.cs) | `GetCapabilitiesAsync(CancellationToken): ValueTask<IReadOnlyCollection<AgentCapability>>` |
| [`IAgentTool`](../../../Extensions/Agent/src/Core/Contracts/AgentToolContracts.cs) | `Descriptor: AgentToolDescriptor`; `PrepareAsync(AgentToolInvocation, CancellationToken): Task<AgentResult<AgentToolPreparation>>`; `ExecuteAsync(AgentPreparedToolInvocation, CancellationToken): Task<AgentResult<AgentToolResult>>` |
| [`IAgentClientTool`](../../../Extensions/Agent/src/Core/Contracts/AgentContributionContracts.cs) | On the same installed tool: `CompleteAsync(AgentPreparedToolInvocation, AgentClientAcknowledgement, CancellationToken): Task<AgentResult<AgentToolResult>>` |
| [`IAgentToolAuthorizer`](../../../Extensions/Agent/src/Core/Contracts/AgentContributionContracts.cs) | `AuthorizeAsync(AgentToolDescriptor, string actorId, string tenantId, AgentResourceTarget?, CancellationToken): Task<AgentPolicyDecision>` |

The [public SDK](../../../Studio/src/essentials/Elsa.Studio.Hosting/Client/src/sdk/index.ts) exposes `api.agent.contextProviders`, `capabilities`, `toolSlots`, `toolContracts` and `resultRenderers`. A context contribution implements `collect(context: StudioAgentContextRequest): Promise<StudioAgentContextCollection | StudioAgentContextAttachment[]>`. An optional tool `executeClient?(invocation: StudioAgentPreparedInvocation, signal?: AbortSignal): Promise<StudioAgentClientExecutionResult>` applies the exact prepared local operation. Presentation uses `StudioAgentResultRendererContribution.component: ComponentType<StudioAgentResultRendererProps>` with declared result types/schema versions. These client declarations do not grant backend execution authority.

## Backend contribution fan-in

| Named Core event | Existing contributor family | One default aggregator |
|---|---|---|
| `AgentContextCollecting` | Evolved `IAgentContextProvider` sources | Compose applicable context and typed availability diagnostics |
| `AgentCapabilitiesCollecting` | Evolved capability providers | Compose supported capabilities and limitations |
| `AgentToolsCollecting` | Existing tools plus evolved tool sources | Produce admitted tool descriptors/invocation bindings |

Events implement `IEvent`, expose their request and initialized get-only output collections directly, and publish with `IInlineEventPublisher`. Register aggregators with `TryAddEventHandler`. Sequential is unshielded: expected optional absence/failure is a typed source outcome; unexpected exceptions/cancellation propagate. Aggregation is not catch-and-ignore policy. Diagnose duplicate identities and unsupported versions. Existing tool registry/invoker consumes this admitted snapshot; there is no second registration engine.

Required and optional availability belong to each `AgentContributionDiagnostic.Required` outcome. The context source descriptor contains identity, applicability and supported schemas; it has no independent required-source flag. Duplicate caller attachment identities fail before module reads, and unknown owner/kind/version inputs cannot disappear behind an admitted attachment's identity.

## Frontend ownership

Evolve public `api.agent` context/tool contribution contracts and existing Slots/Contributions for module presentation. Every producer migrates; `api.ai` can remain only a prompt-dispatch alias. Preserve complete prompt request attachments, source, request correlation and mode through shell/admission/turn; busy/steer delivery preserves these or explicitly refuses unsupported input. Admission, redaction and exclusion remain visible. Caller attachment kinds/schema versions require installed module admission; unknown kinds/versions are denied. Wildcard support means installed admitted types, never arbitrary payload authorization. The server stamps caller data as `ClientSnapshot`; clients cannot self-assert authoritative facts or trusted guidance. Trusted guidance comes only from installed sources.

A module registers context collection and renderer contributions once, reading module-private live bindings. A binding supplies exact selection/revision/generation, context snapshots and a direct local executor; unmount/removal invalidates it. Callbacks and live resource objects stay client-private. Client descriptors advertise support, while trusted backend tool contracts grant execution. Shell and providers never decode workflow IDs, graph operations or payload meaning.

Common review controls own approval, denial, status and policy display. Module renderers own typed result/review content; absent renderer yields an explicit safe generic fallback. Renderers cannot bypass common controls or expand authority. Unknown owner/type/version is diagnosed rather than silently dispatched.

## Prepare, review and apply

1. Existing tool invocation resolves an admitted backend contract. Owning module validates canonical arguments, permissions, target/revision, required context and risk, preparing immutable review data.
2. Existing proposal lifecycle binds that exact prepared invocation. Application rechecks current actor/tenant, availability, host policy and module constraints. Client snapshots are admitted input, not authoritative backend facts.
3. Server executor applies through the owning service and returns read-back evidence. Unsupported executor refuses; no successful no-op fallback.
4. Client mutation is always explicitly reviewed in M1, including FullAuto. UI Apply receives HTTP 202 plus a generic pending client request. It is not an applied result.
5. Originating private binding rechecks exact target/base revision/generation and executes through the native module transaction. It acknowledges only after observing resulting document state. Authenticated acknowledgement stores the typed receipt; then the proposal becomes Executed and the paused turn resumes from that real stored result.

The generic result envelope retains invocation identity, outcome, effect location (local working state/durable server), module result type, schema version, JSON payload and evidence. Both provider paths transport this envelope without module decoding. Policy-bearing tool projection lives in implementation behind Core contracts/thin bridges.

## Bounded client transport

The existing session/message/stream/proposal approve/deny/execute transport carries pending-client requests and receipts. Its authenticated completion route is:

```text
POST /_elsa/agent/sessions/{sessionId}/client-invocations/{invocationId}/acknowledgements
```

Pending records bind session, turn, current principal/tenant, tool, canonical arguments, target/revision, originating generation and expiry. Singleton state stores data; the scoped proposal facade/executor resolves authorization and tools. The endpoint admits a matching receipt and never calls the executor. Read-back/resumption uses the accepted stored result.

<a id="candidate-wire-agreement"></a>

### HTTP wire agreement

JSON property names are camelCase. Typed HTTP/SSE enums use the existing camelCase converter (`clientWorkingState`, `serverPersisted`, `clientSnapshot`, `installedGuidance`); review UI status strings retain their existing normalized form. Internal provider-SDK serialization can instead carry CLR enum numbers or names: `AgentEffectScope.ClientWorkingState = 0`, `ServerPersisted = 1`. Those representations do not change the HTTP contract or authorize coercion inside opaque module payloads.

Session, message and attachment identities are opaque and case-sensitive. Default storage, exact stored-context selection and audit filtering preserve Base62 identity distinctions. Orchestration uses the stored session identity when a custom session service resolves an alias; it cannot create a second lifecycle lease by changing URL casing.

- A target is `{ moduleId, resourceType, resourceId, revision, displayName?, route? }`. Collections/read-only resources may have absent ID/revision; the owning mutation tool requires its exact applicable target and revision. Workflow participation uses owner `Elsa.Studio.Workflows`; the optional Weaver workflow integration supplies its own prompt/rendering contributions.
- A typed payload is `{ typeId, schemaVersion, data }`. Context adds `moduleId`, `schemaVersion`, `target?`, `origin`, `client?: { clientId, generationId }` and admitted `agentScopes` to the existing attachment fields. The server stamps/adopts these only through an installed source. Explicit prompt caller attachments are input to that admission, not trusted facts.
- `preparedInvocation` carries `{ invocationId, sessionId, turnId, toolCallId, toolName, toolContractVersion, moduleId, actorId, tenantId, target, arguments, approvalDigest, risk, preview, executionLocation, requiresApproval, client, expiresAt }`. The engine creates the identity/digest and stores frozen module preparation; approval cannot replace its arguments or target. `preview` is a typed payload.
- Execute returns the existing result fields `{ proposalId, executed, resourceType, resourceId, summary }` plus `status`, `clientInvocation?` and `toolResult?`. HTTP 202 with `status: "awaiting-client"`, `executed: false` and `clientInvocation` (the prepared invocation) is pending. A true applied result contains the actual `toolResult` and receipt.
- An authenticated terminal execute refusal retains its ordinary HTTP/error status and `{ error, data }` envelope. `data` uses that same execution-result shape with exact `proposalId`, `executed: false`, terminal `status` (`expired`, `failed`, `denied` or `cancelled`) and the retained unsuccessful `toolResult`, when one exists. Pre-dispatch expiry may have `toolResult: null`. Nonterminal conflicts have no terminal data. Studio preserves this structured outcome, settles the original request as failed, and drains the retained outcome stream before accepting another prompt; it never guesses terminal state from an error code or replays the original effect.
- Acknowledgement body is `{ client: { clientId, generationId }, approvalDigest, outcome: "applied" | "refused" | "uncertain", summary, after?, result? }`. `after` is the observed target/revision and `result` is a typed module payload. Session/invocation IDs are route-bound; body actor identity is never authority. Its response uses the same execution-result envelope. A module validates/redacts the completion before it becomes provider-bound. A refused operation retains typed status `refused`, `executed: false` and an unsuccessful result without an effect receipt; its stored/displayed proposal status is `Failed`/`failed`.
- A tool result retains `{ toolCallId, toolName, succeeded, content, payload?, receipt? }`. A receipt is `{ invocationId, before, after, scope, evidence }`, with module-owned typed evidence. Generic transport never decodes graph operations, undo tokens or fixture resource values.

In M1, local tools force review. UI Apply obtains the client request, dispatches to the exact registered callback and posts acknowledgement. The accepted receipt updates review and is supplied to the resumed/next provider turn as a generic completed-tool outcome; the original user prompt must not be replayed to trigger another effect. An unsupported unattended path is explicit rather than reported as applied. Recovery beyond the retained in-memory record remains [#3441](https://github.com/elsa-workflows/elsa-foundation/issues/3441).

Before dispatch, cancellation/expiry/stale binding means zero-effect refusal. After dispatch, lost acknowledgement or cancellation means an available receipt or explicit uncertainty; no replay or rollback claim. Identical duplicates coalesce within memory, conflicting receipts are rejected/diagnosed, and late receipts cannot reopen execution. Restart or shell replacement loses these records and fails explicitly. Durable approval/history/recovery belongs to M2.

<a id="current-candidate-limits"></a>

## Accepted M1 bounds

These bounds describe the accepted minimum demonstrated by the independent M1 package witness. Canonical signatures, defaults, lifetimes and contribution events are indexed in the [Agent extension catalog](../../../Extensions/Agent/src/Elsa.Agent/EXTENSION_POINTS.md); this section adds no wire fields. Final PR integration remains separate from that qualified code baseline.

- **Discovery and admission:** installed context/capability/tool contributors feed the three named Sequential events through one aggregator each. The scoped async tool registry retains its collected installed instances only for that scope. An installed context source can declare `Descriptor.ScopeKind = "*"` for ambient collection on every request; its supported owner/kind/version admission and module-owned authorization still apply. Caller attachment scopes and origin cannot grant authority. Expected absence is a typed diagnostic; unexpected exceptions/cancellation propagate.
- **Current authority:** the default `IAgentToolAuthorizer` denies execution until replaced. Agent API supplies `HttpAgentToolAuthorizer`, using the current normalized principal/tenant and canonical `IPermissionAuthorizationService` against the prepared resource. Custom hosts must supply equivalent current-authority integration. Preparation is side-effect free and must use authorized owning services for reads; exact-target authorization, current policy, tool owner/version, risk and expiry are checked before effects. Same-tool client completion rechecks the current contract and authority before admitting provider-bound evidence.
- **One mutation per turn:** one distinct mutating tool call is admitted per session/turn, including FullAuto server effects. Identical repeated calls coalesce; changed input/context or a second distinct mutator is explicitly refused. A single module call may own a native atomic batch. Parallel independent mutation proposals and unattended client effects are outside M1.
- **Reads and review:** server reads execute inline through installed preparation/execution. A read that requires review, or declares client execution, is explicitly unsupported. All mutations use the prepared proposal/effect pipeline; FullAuto server execution does not bypass receipt checks. Client working-state mutations always require explicit review, including FullAuto.
- **Observed application:** module authors own preview/result schemas, native validation, risk, exact resource constraints, read-back evidence, rendering and application semantics. A server result needs an actual owning-resource receipt. A browser request remains pending until `IAgentClientTool` on the same installed tool validates/redacts the exact observation and returns a local working-state receipt. Client-reported text, pending dispatch, absent executors and unvalidated receipts cannot establish successful application. Local evidence does not establish durable save/publish.
- **Continuation and uncertainty:** receipt continuation receives the retained actual result with no tools available; it cannot replay the original user request or authorize another mutation. In-memory matching execution/acknowledgement duplicates return retained results; conflicting acknowledgements cannot replace accepted evidence. An unvalidated applied report can later be resolved by valid evidence without executing again. Before-dispatch refusal is zero effect; after dispatch cancellation, expired/revoked completion or an unverifiable observation preserves uncertainty rather than promising rollback or allowing replay.
- **Retention and proof boundary:** singleton proposal state stores data/synchronization/results only; scoped facades resolve tools and authority. Sessions, reviews, paused turns, results, audit and deduplication are process local. Restart or shell replacement loses these records; cross-process coordination and durable recovery/history/approval remain M2 [#3441](https://github.com/elsa-workflows/elsa-foundation/issues/3441). The earlier private source-composed fixture remains an example. The separately built Acme packages and external consumers supply the independent distribution proof. Deterministic provider-protocol evidence establishes the recorded adapter journeys; live-provider success remains unclaimed.

<a id="required-examples-and-independent-proof"></a>

## Demonstrated examples and independent proof

#3437 connects a real workflow `SetDesignerPosition` edit to existing native atomic transaction/validation/dirty state, exact definition ID/hash revision/generation, observed-state receipt and guarded whole-batch undo. Authorized execution has no `active-draft` fallback. It also connects an unrelated server resource with actual read-back effect. Both cover caller attachments and protocol transport through existing providers.

#3438's [76ec witness](../../1174-weaver-package-witness/evidence/qualification-76ec58d00.json) separately built retained product NuGet/SDK distributions, external resource/protocol packages and package-only consumers. All 38 cases completed: 16 positives and 22 recognized failing controls, with add/remove/incompatible/restore transitions and both original/restored journeys through the actual Copilot and Anthropic adapters. Common baseline files stayed identical through extension; consumer source references and frontend build invocations were zero. Package authoring/build evidence and actual resource/browser observations are separate parts of that proof.

The supported workflow subset is graph read and reviewed `SetDesignerPosition`, native validation, atomic unsaved application, whole-change undo and explicit Save. Broader workflow authoring/troubleshooting remains [#3442](https://github.com/elsa-workflows/elsa-foundation/issues/3442). Full current-module coverage, durable shared lifecycle/recovery and live-provider qualification remain Program M2–M5. Documentation integration, exact final-head checks, independent review, merge and resulting-main qualification are tracked in [spec1174 tasks T026–T028](../../1174-weaver-package-witness/tasks.md).
