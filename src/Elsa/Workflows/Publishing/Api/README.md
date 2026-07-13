# Workflows Publishing API

`Elsa.Workflows.Publishing.Api` is the supported management-client surface for compiling, preflighting,
publishing, inspecting, unpublishing, restoring, and test-running workflows. It belongs to the Publishing
domain rather than a reference server, so custom Elsa hosts and Elsa Studio can compose the same behavior
without copying endpoints from `Elsa.Server`.

The canonical authority decision is [ADR 0043](../../../../../docs/adr/0043-publication-slots-define-start-authority.md).
Use the [Elsa glossary](../../../../../docs/glossary/elsa.md) and
[root glossary](../../../../../docs/glossary/root.md) for shared architecture vocabulary; this README describes
how this module realizes those concepts and does not redefine them.

## Composition

Enable the `WorkflowsPublishingApi` shell feature. It depends on `WorkflowsRuntimeTriggers` and registers the
Publishing endpoints, request handlers, compiler, policy/preflight/activation services, and process-local stores.
The in-memory stores are useful for tests and single-process development, but publication authority, policies,
records, and reconciliation intents must be durable in a production host.

For Groundwork-backed authority state, reference
`Elsa.Workflows.Publishing.Persistence.Groundwork` and compose:

```csharp
services.AddGroundworkPublishingStores();
```

The registration replaces (or, when composed first, prevents) the API feature's in-memory defaults for
`IPublicationSlotStore`, `IPublicationRecordStore`, `IPublicationPolicyStore`, and
`IPublicationProjectionIntentStore`; it does not couple Publishing to a particular server application.
The host must also compose the Runtime persistence used for executable artifacts, source references, trigger
bindings, and recurring schedules.

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
5. Persist idempotent per-publication projection intents. If stores cannot share a transaction, retries replay
   the same intent identity and converge. A failed activation compensates by restoring the previous authority
   before the candidate is removed; observers refresh only from the final serving state.
6. Retire or restore the publication source reference as provenance. Existing executions remain pinned to their
   immutable executable artifact; unpublishing does not delete that artifact.

Clients should call preflight immediately before publish, display the resolved action/slot and conflicts, and
send `ExpectedPublicationId` when protecting against a stale Studio view. A `409` means the client must refresh
authority state and preflight again; it must not assume that a candidate became active.

## HTTP endpoint surface

All routes are relative to the host's Elsa API base path.

| Method | Route | Permission | Purpose |
|---|---|---|---|
| `GET` | `publishing/activities` | `WorkflowPublishingRead` | List constructable activity catalog rows. |
| `GET` | `publishing/activities/{activityId}/construct` | `WorkflowPublishingRead` | Construct an activity from a catalog row. |
| `POST` | `publishing/workflows/{versionId}/preflight` | `WorkflowPublishingRead` | Resolve policy and return trigger changes/conflicts without changing authority. |
| `POST` | `publishing/workflows/{versionId}/publish` | `WorkflowPublishingManage` | Compile, prepare, CAS-activate, reconcile, and return the publication. |
| `GET` | `publishing/workflows/{definitionId}/slots` | `WorkflowPublishingRead` | List publication slots and visible lifecycle state. |
| `GET` | `publishing/workflows/{definitionId}/slots/{slotName}` | `WorkflowPublishingRead` | Read one slot. |
| `DELETE` | `publishing/workflows/{definitionId}/slots/{slotName}` | `WorkflowPublishingManage` | Unpublish the slot authority and its serving projections. |
| `POST` | `publishing/workflows/{definitionId}/slots/{slotName}/restore` | `WorkflowPublishingManage` | Restore the latest eligible retired publication with a new authority transition. |
| `GET` | `publishing/workflows/{definitionId}/policy` | `WorkflowPublishingRead` | Read the effective workflow/host policy. |
| `PUT` | `publishing/workflows/{definitionId}/policy` | `WorkflowPublishingManage` | CAS-update workflow publication policy. |
| `POST` | `publishing/workflows/{versionId}/test-runs` | `WorkflowPublishingManage` | Compile and run a persisted Design version without granting publication authority. |
| `POST` | `publishing/workflows/drafts/test-runs` | `WorkflowPublishingManage` | Compile and run a supplied draft snapshot without granting publication authority. |

The version route excludes the reserved literal `drafts`, so the two test-run routes cannot overlap.

## Failure and recovery expectations

- Policy errors and malformed requests return `400`; stale expected authority, preflight conflicts, and losing
  CAS transitions return `409`; missing Design/publication resources return `404`.
- Publication records and projection intents expose pending/failed lifecycle facts. A host must retain them long
  enough for operational diagnosis and reconciliation; HTTP success must not be synthesized while projections
  are only partially switched.
- Unpublish and restore use the same revisioned slot lifecycle and projection compensation rules as publish.
- HTTP route tables are derived from active trigger bindings. Runtime HTTP contributes a neutral trigger-index
  observer; Publishing does not reference the HTTP or Scheduling modules.

See [the Publishing extension-point catalog](EXTENSION_POINTS.md) for supported replacements and provider work,
and [the feature quickstart](../../../../../specs/091-domain-owned-apis/quickstart.md) for the `/foo` to `/bar`
replacement scenario.
