# Workflows Publishing API

`Elsa.Workflows.Publishing.Api` is the supported management-client surface for compiling, preflighting,
publishing, inspecting, unpublishing, restoring, and test-running workflows. It belongs to the Publishing
domain rather than a reference server, so custom Elsa hosts and Elsa Studio can compose the same behavior
without copying endpoints from `Elsa.Workbench`.

The canonical authority decision is [ADR 0043](../../../../../docs/adr/0043-publication-slots-define-start-authority.md).
Use the [Elsa glossary](../../../../../docs/glossary/elsa.md) and
[root glossary](../../../../../docs/glossary/root.md) for shared architecture vocabulary; this README describes
how this module realizes those concepts and does not redefine them.

## Composition

Enable the `WorkflowsPublishingApi` shell feature. It depends on `WorkflowsPublishing` (the endpoint-free
publish + compile engine) and `ApiCapabilities`, and registers only transport: the Publishing HTTP endpoints,
the API capability declarations, transport authorization (`IActivityPublishingAuthorizationContext` over
`HttpContext`), and the activity-draft publish/test-run services. The compiler, publication authority stores,
and the policy/preflight/activation/projection services are supplied by the engine feature via `DependsOn` —
see [the engine README](../README.md). The engine's in-memory stores are useful for tests and single-process
development, but publication authority, policies, and records must be durable in a production host.

For Groundwork-backed authority state, reference
`Elsa.Workflows.Publishing.Persistence.Groundwork` and compose:

```csharp
services.AddGroundworkPublishingStores();
```

The registration replaces (or, when composed first, prevents) the API feature's in-memory defaults for
`IPublicationRecordStore` and `IPublicationPolicyStore`; it does not couple Publishing to a particular server
application.
Activation is not included — the slot ledger is `IWorkflowActivationAuthority`, owned by the runtime
store family (spec 151, FR-B-006).
The host must also compose the Runtime persistence used for executable artifacts, source references, trigger
bindings, and recurring schedules.

Groundwork also replaces the process-local activity publication receipt store. Activity publication
requires this durable store and the cross-domain Groundwork commit command to share one transaction:
an Applied receipt, immutable activity version, definition head, executable template, Source
Reference, layout, and dependency edges become visible together or not at all.

## Publication lifecycle

The management flow is capability-oriented but the authority transition is one coordinated Publishing
operation:

1. Resolve intent using `explicit request > workflow policy > host policy`. With no override, publishing
   replaces the `default` slot. Side-by-side publication requires an explicit non-default slot.
2. Compile the candidate and preflight its publication-scoped trigger claims. Exclusive claims (including HTTP
   routes) conflict with authoritative claims in other slots; FanOut claims may coexist.
3. Prepare inactive trigger-binding and recurring-schedule projections. Prepared rows are not visible to new
   starts, the HTTP route table, or the recurring pump.
4. Activate the slot with compare-and-swap using its expected revision, then switch the prepared serving
   projections to the new publication and retire the replaced projection.
5. A failed activation compensates in-process: the coordinator restores the previous authority and re-activates
   its projections before removing the candidate's; observers refresh only from the final serving state. There
   is no delivery-intent ledger to replay — the retry is a fresh request, and every step is idempotent.
6. Retire or restore the publication source reference as provenance. Existing executions remain pinned to their
   immutable executable artifact; unpublishing does not delete that artifact.

Clients should call preflight immediately before publish, display the resolved action/slot and conflicts, and
send `ExpectedPublicationId` when protecting against a stale Studio view. A `409` means the client must refresh
authority state and preflight again; it must not assume that a candidate became active.

## HTTP endpoint surface

All routes are relative to the host's Elsa API base path.

| Method | Route | Permission | Purpose |
|---|---|---|---|
| `GET` | `publishing/activities` | `workflow-publishing.read` | List constructable activity catalog rows. |
| `GET` | `publishing/activities/{activityId}/construct` | `workflow-publishing.read` | Construct an activity from a catalog row. |
| `GET` | `publishing/incident-strategies` | `workflow-publishing.read` | List safe incident-strategy descriptors and the effective default publication strategy. |
| `GET` | `publishing/value-conversion/profiles` | `workflow-publishing.read` | List safe value-conversion profiles. |
| `POST` | `publishing/workflows/{versionId}/preflight` | `workflow-publishing.read` | Resolve policy and return trigger changes/conflicts without changing authority. |
| `POST` | `publishing/workflows/preflight` | `workflow-publishing.read` | Preflight a supplied workflow snapshot and issue a review token. |
| `DELETE` | `publishing/workflows/{definitionId}/slots/{slotName}` | `workflow-publishing.manage` | Unpublish the slot authority and its serving projections. |
| `POST` | `publishing/workflows/{definitionId}/slots/{slotName}/restore` | `workflow-publishing.manage` | Restore the latest eligible retired publication with a new authority transition. |
| `GET` | `publishing/workflows/{definitionId}/policy` | `workflow-publishing.read` | Read the effective workflow/host policy. |
| `PUT` | `publishing/workflows/{definitionId}/policy` | `workflow-publishing.manage` | CAS-update workflow publication policy. |
| `POST` | `publishing/workflows/{versionId}/publish` | `workflow-publishing.manage` | Compile, prepare, CAS-activate, reconcile, and return the publication. |
| `GET` | `publishing/workflows/{versionId}/executable-export` | `workflow-publishing.read` | Export the portable executable-artifact closure for one Published version (FR-B-010a). |
| `POST` | `publishing/workflows/{versionId}/test-runs` | `workflow-publishing.manage` | Compile and run a persisted Design version without granting publication authority. |
| `POST` | `publishing/workflows/drafts/test-runs` | `workflow-publishing.manage` | Compile and run a supplied draft snapshot without granting publication authority. |
| `POST` | `publishing/preflight` | `workflow-publishing.read` | Validate Runtime Evidence requirements for supplied executable artifacts. |
| `POST` | `design/activities/drafts/{draftId}/publication-preflight` | `workflow-publishing.read` | Return exact draft/head-bound diagnostics, diff, dependencies, readiness, SemVer choices, and review token. |
| `POST` | `design/activities/drafts/{draftId}/publish` | `workflow-publishing.manage` | Recheck and atomically apply an idempotent reviewed activity publication. |
| `GET` | `design/activities/publications/{idempotencyKey}` | `workflow-publishing.read` | Read the durable activity publication receipt and terminal outcome. |
| `POST` | `publishing/activity-drafts/{draftId}/test-runs` | `workflow-publishing.manage` | Start an activity draft test run. |
| `GET` | `publishing/activity-test-runs/{testRunId}` | `workflow-publishing.manage` | Read an activity draft test run. |
| `GET` | `publishing/activity-drafts/{draftId}/test-runs/idempotency/{idempotencyKey}` | `workflow-publishing.manage` | Resolve an activity test run by its draft-scoped idempotency key. |
| `POST` | `publishing/activity-test-runs/{testRunId}/cancel` | `workflow-publishing.manage` | Request cancellation of an activity draft test run. |

The version route excludes the reserved literal `drafts`, so the two test-run routes cannot overlap.

**Activation-slot reads are not here.** `GET /runtime/workflows/activation-slots/{definitionId}` and
`GET /runtime/workflows/activation-slots/{definitionId}/{slotName}` are served by `Elsa.Workflows.Runtime.Api`
under the `elsa.api.runtime` capability: the activation slot is a runtime concept, and a runtime-only engine has
one without ever having published anything. Publishing keeps the two slot lifecycle **commands** above, whose
responses still join the resulting slot to its `PublicationRecord` — that join is a publishing concern because
only publishing holds the journal. The retired `publication-slots` capability relation is now
`workflow-activation-slots` / `workflow-activation-slot` on `elsa.api.runtime`.

Activity publication clients must preflight immediately before publish and submit the returned
opaque review token, one exact offered version, and a caller-stable idempotency key. Replaying the
same operation identity returns the recorded receipt without another publication. `Stale` requires
a new preflight; `OutcomeUnknown` requires receipt reconciliation before choosing a new key.

## Failure and recovery expectations

- Policy errors and malformed requests return `400`; stale expected authority, preflight conflicts, and losing
  CAS transitions return `409`; missing Design/publication resources return `404`.
- Publication records expose pending/failed lifecycle facts — they are the only such record publishing keeps. A
  host must retain them long enough for operational diagnosis; HTTP success must not be synthesized while
  projections are only partially switched.
- Unpublish and restore use the same revisioned slot lifecycle and projection compensation rules as publish.
- HTTP route tables are derived from active trigger bindings. Runtime HTTP contributes a neutral trigger-index
  observer; Publishing does not reference the HTTP or Scheduling modules.

See [the Publishing extension-point catalog](EXTENSION_POINTS.md) for supported replacements and provider work,
and [the feature quickstart](../../../../../specs/092-domain-owned-apis/quickstart.md) for the `/foo` to `/bar`
replacement scenario.
