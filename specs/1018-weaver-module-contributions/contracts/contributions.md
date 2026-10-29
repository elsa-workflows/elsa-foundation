# Candidate Contribution Contract

Status: provisional #3437 interface design. #3438 independent packed-module proof precedes freeze. Follow existing [Studio extension contracts](../../../Studio/CONTEXT.md) and [review gates](../../../docs/contributing/review-checklist.md); this document adds no universal resource schema.

## Package composition

Core exposes contracts, models, named events and thin bridges. New `Elsa.Agent` owns active defaults and `FoundationAgentAbstractionsFeature`; its configured name stays `FoundationAgentAbstractions`. API/provider/workflow features inherit it, declare Type dependencies and call idempotent base registration. The base declares string dependency `Events` in `new object[] { "Events" }`; the consumer explicitly discovers ordinary Events infrastructure. Missing Events is a diagnosed composition failure, not a promise that a leaf recursively discovers it. Deterministic providers are test fixtures.

All 16 Agent Core implementation-shape exceptions are removed when their implementations move. Remove Core feature registration/manifest-generator dependencies when no longer used, preserving only genuinely thin AI bridges. Default implementation types are internal except genuine consumer feature/extension APIs; tests use domain-owned visibility. Record public API and SemVer delta, update affected catalogs/EXTENSION_POINTS and verify package closure.

## Backend contribution fan-in

| Named Core event | Existing contributor family | One default aggregator |
|---|---|---|
| `AgentContextCollecting` | Evolved `IAgentContextProvider` sources | Compose applicable context and typed availability diagnostics |
| `AgentCapabilitiesCollecting` | Evolved capability providers | Compose supported capabilities and limitations |
| `AgentToolsCollecting` | Existing tools plus evolved tool sources | Produce admitted tool descriptors/invocation bindings |

Events implement `IEvent`, expose their request and initialized get-only output collections directly, and publish with `IInlineEventPublisher`. Register aggregators with `TryAddEventHandler`. Sequential is unshielded: expected optional absence/failure is a typed source outcome; unexpected exceptions/cancellation propagate. Aggregation is not catch-and-ignore policy. Diagnose duplicate identities and unsupported versions. Existing tool registry/invoker consumes this admitted snapshot; there is no second registration engine.

## Frontend ownership

Evolve public `api.agent` context/tool contribution contracts and existing Slots/Contributions for module presentation. Every producer migrates; `api.ai` can remain only a prompt-dispatch alias. Preserve complete prompt request attachments, source, request correlation and mode through shell/admission/turn; busy/steer delivery preserves these or explicitly refuses unsupported input. Admission, redaction and exclusion remain visible. Caller attachment kinds/schema versions require installed module admission; unknown kinds/versions are denied. Wildcard support means installed admitted types, never arbitrary payload authorization. The server stamps caller data as `ClientSnapshot`; clients cannot self-assert authoritative facts or trusted guidance. Trusted guidance comes only from installed sources.

A module registers context collection and renderer contributions once, reading module-private live bindings. A binding supplies exact selection/revision/generation, context snapshots and a direct local executor; unmount/removal invalidates it. Callbacks and live resource objects stay client-private. Client descriptors advertise support, while trusted backend tool contracts grant execution. Shell and providers never decode workflow IDs, graph operations or payload meaning.

Common review controls own approval, denial, status and policy display. Module renderers own typed result/review content; absent renderer yields an explicit safe generic fallback. Renderers cannot bypass common controls or expand authority. Unknown owner/type/version is diagnosed rather than silently dispatched.

## Prepare, review and apply

1. Existing tool invocation resolves an admitted backend contract. Owning module validates canonical arguments, permissions, target/revision, required context and risk, preparing immutable review data.
2. Existing proposal lifecycle binds that exact prepared invocation. Application rechecks current actor/tenant, availability, host policy and module constraints. Client snapshots are admitted input, not authoritative backend facts.
3. Server executor applies through the owning service and returns read-back evidence. Unsupported executor refuses; no successful no-op fallback.
4. Client mutation is always explicitly reviewed for this candidate, including FullAuto. UI Apply receives HTTP 202 plus a generic pending client request. It is not an applied result.
5. Originating private binding rechecks exact target/base revision/generation and executes through the native module transaction. It acknowledges only after observing resulting document state. Authenticated acknowledgement stores the typed receipt; then the proposal becomes Executed and the paused turn resumes from that real stored result.

The generic result envelope retains invocation identity, outcome, effect location (local working state/durable server), module result type, schema version, JSON payload and evidence. Both provider paths transport this envelope without module decoding. Policy-bearing tool projection lives in implementation behind Core contracts/thin bridges.

## Bounded client transport

Extend current session/message/stream/proposal approve/deny/execute transport with pending-client request and receipt models. A candidate authenticated route is:

```text
POST /_elsa/agent/sessions/{sessionId}/client-invocations/{invocationId}/acknowledgements
```

Route/signature spelling is provisional. Pending records bind session, turn, current principal/tenant, tool, canonical arguments, target/revision, originating generation and expiry. Store only data in singleton state; scoped proposal facade/executor resolves authorization and tools. The endpoint admits a matching receipt, never calls the executor. Read-back/resumption must use the accepted stored result, not fabricated pending text.

### Candidate wire agreement

JSON property names are camelCase. Enum values in typed backend payloads use the existing camelCase converter (`clientWorkingState`, `serverPersisted`, `clientSnapshot`, `installedGuidance`); review UI status strings retain their existing normalized form.

- A target is `{ moduleId, resourceType, resourceId, revision, displayName?, route? }`. Collections/read-only resources may have absent ID/revision; the owning mutation tool requires its exact applicable target and revision. Workflow participation uses owner `Elsa.Studio.Workflows`; the optional Weaver workflow integration supplies its own prompt/rendering contributions.
- A typed payload is `{ typeId, schemaVersion, data }`. Context adds `moduleId`, `schemaVersion`, `target?`, `origin`, `client?: { clientId, generationId }` and admitted `agentScopes` to the existing attachment fields. The server stamps/adopts these only through an installed source. Explicit prompt caller attachments are input to that admission, not trusted facts.
- `preparedInvocation` carries `{ invocationId, sessionId, turnId, toolCallId, toolName, toolContractVersion, moduleId, actorId, tenantId, target, arguments, approvalDigest, risk, preview, executionLocation, requiresApproval, client, expiresAt }`. The engine creates the identity/digest and stores frozen module preparation; approval cannot replace its arguments or target. `preview` is a typed payload.
- Execute returns the existing result fields `{ proposalId, executed, resourceType, resourceId, summary }` plus `status`, `clientInvocation?` and `toolResult?`. HTTP 202 with `status: "awaiting-client"`, `executed: false` and `clientInvocation` (the prepared invocation) is pending. A true applied result contains the actual `toolResult` and receipt.
- Acknowledgement body is `{ client: { clientId, generationId }, approvalDigest, outcome: "applied" | "refused" | "uncertain", summary, after?, result? }`. `after` is the observed target/revision and `result` is a typed module payload. Session/invocation IDs are route-bound; body actor identity is never authority. Its response uses the same execution-result envelope. A module validates/redacts the completion before it becomes provider-bound.
- A tool result retains `{ toolCallId, toolName, succeeded, content, payload?, receipt? }`. A receipt is `{ invocationId, before, after, scope, evidence }`, with module-owned typed evidence. Generic transport never decodes graph operations, undo tokens or fixture resource values.

For the bounded candidate, local tools force review. UI Apply obtains the client request, dispatches to the exact registered callback and posts acknowledgement. The accepted receipt updates review and is supplied to the resumed/next provider turn as a generic completed-tool outcome; the original user prompt must not be replayed to trigger another effect. An unsupported unattended path is explicit rather than reported as applied. Recovery beyond the retained in-memory record remains #3441.

Before dispatch, cancellation/expiry/stale binding means zero-effect refusal. After dispatch, lost acknowledgement or cancellation means an available receipt or explicit uncertainty; no replay or rollback claim. Identical duplicates coalesce within memory, conflicting receipts are rejected/diagnosed, and late receipts cannot reopen execution. Candidate restart loses records and fails explicitly. Durable approval/history/recovery belongs to M2.

## Required examples and independent proof

#3437 connects a real workflow `SetDesignerPosition` edit to existing native atomic transaction/validation/dirty state, exact definition ID/hash revision/generation, observed-state receipt and guarded whole-batch undo. Authorized execution has no `active-draft` fallback. It also connects an unrelated server resource with actual read-back effect. Both cover caller attachments and protocol transport through existing providers.

#3438 separately packs real backend/frontend artifacts, builds an independently authored non-workflow module/consumer, and proves context -> tool -> review -> applied result plus removal/incompatibility with zero Weaver/host/provider implementation edits. In-repo fixtures do not satisfy that gate. Amend the candidate from this evidence before freezing signatures.
