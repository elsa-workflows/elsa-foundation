# Standalone triggers

Use this guide to configure shared HTTP, Event, Timer and Cron entry points without changing a workflow's graph. Terminology is maintained in the [Elsa glossary](glossary/elsa.md); module contracts are maintained in the [Runtime extension catalog](../Essentials/src/Workflows/Runtime/Elsa.Workflows.Runtime/EXTENSION_POINTS.md#standalone-trigger-authoring-and-source-boundaries).

**Implementation status:** This guide describes the source implementation being prepared for [#3582](https://github.com/elsa-workflows/elsa-foundation/issues/3582). The examples are source-checked request shapes, not passing host, provider, restart or Studio acceptance evidence. The [qualification guide](../specs/1217-standalone-triggers/quickstart.md) records the required checks. The [specification and proposed contracts](../specs/1217-standalone-triggers/spec.md) also contain requirements whose acceptance remains open.

## Compose the host

Install the following features in the shell that owns the resources. Feature names below are composition keys, not a complete `shells.json` file.

| Purpose | Composition |
|---|---|
| Management routes, permissions and API discovery | `StandaloneTriggersApi` |
| HTTP configuration metadata and validation | `StandaloneHttpTriggerAuthoring` |
| Event configuration metadata and validation | `StandaloneEventTriggerAuthoring` |
| Timer/Cron configuration metadata and validation | `StandaloneScheduleTriggerAuthoring` |
| HTTP request intake | `ActivitiesHttp`, with its `WorkflowsRuntimeHttp` dependency and the ordinary Runtime composition |
| Timer/Cron intake | `StandaloneScheduleTriggers`, with its `WorkflowsRuntimeRecurringTriggers` dependency, ordinary Runtime scheduling and the normal Tasks lifecycle |
| Workflow-origin broadcast Events | `ActivitiesPrimitives` and the ordinary durable `PublishEvent`/Runtime delivery composition |
| Application-origin Event producer | `AddStandaloneEventIngress<TSource>` plus the application's trusted scoped source context; see the [Event module example](../Essentials/src/Activities/Primitives/StandaloneTriggers/README.md#application-owned-event-producer) |
| Restart-surviving storage candidate | The complete `WorkflowsRuntimeEntityFrameworkCore` provider, including its resource, ingress, publication, material, queue and checkpoint participants |

Authoring metadata does not install intake. `StandaloneTriggersApi` deliberately does not start Runtime, an HTTP listener or a schedule pump. A Design-only host can manage never-activated paused resources only when a resource store, target discovery and input mapper have been supplied. Its default authoring committer refuses enabled associations, activation and changes to previously activated resources. Installing the API feature alone supplies none of those three replacement services.

`AddWorkflowRuntime()` selects the coherent in-memory family by default. Its state is process-local. The complete EF provider replaces that family together; independently replacing a resource store or an outbox does not establish durable acceptance. Mixed physical providers, missing source adapters and unavailable retention/queue capabilities refuse operations. Distributed placement, arbitrary third-party storage combinations and remote queue acknowledgements have no acceptance claim here. Genuine direct BPMN dispatch currently requires the ordinary in-process actor and qualified model queue.

The checked-in Workbench feature list does not select the standalone management/authoring/schedule features automatically. Add the features required for your host explicitly. Keep the host's existing identity, permission, persistence and shell-route configuration.

## Create a paused resource

Discover capability `elsa.api.standalone-triggers`, contract major `1`, and resolve its links against the selected shell's API base. All routes below are suffixes relative to that base. Do not assume `/elsa`, the server root or a particular port. Capabilities describe installed operations; resource `actions`, `findings` and `effectiveState` describe what the current caller and composition can do.

`POST runtime/standalone-triggers` creates a paused resource. `schemaVersion` is the string `"1"`. Choose a new `requestId` for a new mutation; retry the identical request with the same ID to reconcile an uncertain response. Reusing an ID with different content conflicts.

HTTP example:

```json
{
  "requestId": "create-orders-http-001",
  "name": "Orders webhook",
  "typeKey": "http",
  "schemaVersion": "1",
  "configuration": {
    "method": "POST",
    "path": "orders",
    "sourceAccess": "Authenticated"
  },
  "http": {
    "mode": "AsyncFanOut",
    "admissionPolicy": "RequireAll"
  }
}
```

`http` is a top-level request property beside `configuration`. Supplying it inside `configuration` is rejected. `sourceAccess` is either `Authenticated` or `Anonymous`; public intake still requires resource invocation grants for its workflow targets. The route is relative to `HttpEndpointOptions.BasePath`, which defaults to `/workflows/http`. With that default, `path: "orders"` receives requests at `/workflows/http/orders` under the shell mount.

The other three families use the same create route:

```json
{
  "requestId": "create-order-event-001",
  "name": "Order received",
  "typeKey": "event",
  "schemaVersion": "1",
  "configuration": { "eventName": "OrderReceived" }
}
```

```json
{
  "requestId": "create-reconcile-timer-001",
  "name": "Reconcile every five minutes",
  "typeKey": "timer",
  "schemaVersion": "1",
  "configuration": { "interval": "PT5M" }
}
```

```json
{
  "requestId": "create-business-close-001",
  "name": "Business day close",
  "typeKey": "cron",
  "schemaVersion": "1",
  "configuration": {
    "expression": "0 17 * * 1-5",
    "timezone": "Europe/Amsterdam"
  }
}
```

Timer accepts a positive ISO-8601 duration or invariant `TimeSpan`. Cron accepts five fields, or six fields with seconds first, and an installed `TimeZoneInfo` ID. Supply the timezone explicitly; these examples use UTC instants for inputs and local time only for Cron calculation. The calculator uses the repository's pinned Cronos dependency. Actual DST, clock-change and cross-host timezone qualification remains part of the pending matrix.

## Associate a serving workflow and map inputs

Publish the target with a declared input contract before enabling its association. Use `GET runtime/standalone-trigger-targets` and the `target` relation to inspect its actual serving slot and contract. The association selects a logical `definitionId` and `slotName`; omitting the slot selects `default`. It does not select a Design draft or a fixed version.

Changes use a preview/apply pair. Read the current resource first, copy its `revision` to `expectedRevision`, and submit the command to `POST runtime/standalone-triggers/{triggerId}/changes/preview`. For example, if the resource is still at revision `1`:

```json
{
  "expectedRevision": 1,
  "command": {
    "kind": "AddSubscription",
    "target": { "definitionId": "orders", "slotName": "default" },
    "enabled": true,
    "invocationRequest": "Grant",
    "mappings": [
      {
        "inputName": "OrderId",
        "binding": { "kind": "SourceField", "path": ["payload", "orderId"] }
      },
      {
        "inputName": "Origin",
        "binding": { "kind": "Literal", "value": "webhook" }
      }
    ]
  }
}
```

Replace `orders`, `OrderId` and `Origin` with the selected workflow's actual logical ID and declared input names. This example requires the caller to manage and operate the trigger, authorize invocation and execute the selected target. It grants and enables the association while leaving the resource paused.

Inspect `canApply`, `impact` and `findings`. To apply, send the same `expectedRevision` and identical `command` to the `changes/apply` route, adding a new `requestId` and the returned `previewId`. A preview expires after five minutes and binds the actor, scope, resource revision and observed targets. Reload and preview again after a conflict; do not increment revisions locally. Repeat this process with `{ "kind": "EnableTrigger" }` using the resource's latest revision when at least one enabled association is eligible.

The trigger detail and workflow editor operate on the same association records. `GET runtime/standalone-trigger-subscriptions` requires exactly one of `triggerId` or `definitionId`; the workflow filter can also take `slotName`. Editing an association there has the same authority, revision and impact checks.

Mapping paths contain exact, case-sensitive object property names, not JSONPath or array indexes. Map values from the detached source envelope:

| Source | Useful paths and sample input |
|---|---|
| HTTP | `["payload", "orderId"]` for JSON body `{ "orderId": "A-42" }`; `["metadata", "method"]`, `["metadata", "path"]`, `["metadata", "query"]`, `["metadata", "routeValues"]`, `["metadata", "contentType"]` |
| Event | `["payload", "orderId"]` for Event payload `{ "orderId": "A-42" }`; `["metadata", "eventName"]` on the ordinary broadcast adapter or a producer that supplies it |
| Timer/Cron | `["metadata", "dueAt"]` for the persisted UTC due instant; `["metadata", "timezone"]` for `UTC` on Timer or the Cron timezone; `["payload", "generation"]` for the schedule generation |

For a schedule workflow declaring a string `DueAt` input, use:

```json
[
  {
    "inputName": "DueAt",
    "binding": { "kind": "SourceField", "path": ["metadata", "dueAt"] }
  }
]
```

An absent source property omits that input; explicit JSON `null` remains a value. The ordinary target input validator applies defaults, requiredness, nullability and type validation. Unknown or duplicate destinations fail validation. Preview a representative envelope through `mapping-preview` with `expectedRevision`, `target`, `mappings`, `sample` and, when available, `contractRevision`. The `sample` is the envelope, for example `{ "payload": { "orderId": "A-42" }, "metadata": {} }`, rather than the body alone. Preview is advisory and performs no admission or execution.

The Core binding model includes pure `Expression` templates with detached literal/source parameters. `mapping-context` lists the languages supplied by installed portable handlers and reports each language's evaluator availability. An empty list means no handler is installed; an unavailable entry includes a reason. Literal and SourceField mappings above require no expression language. Any supplied expression still needs the composed portable evaluator and a binding-pure capability profile; discovery is not proof that an arbitrary language/profile/template will evaluate.

## Grants and publication changes

`triggers:manage` controls configuration and associations; `triggers:operate` controls enable/pause. `triggers:authorize` controls resource invocation grants. Granting also requires `workflows/runtime:execute` on the selected target; ordinary workflow read access is required to see its contract and serving details. Delivery read, retry and cancellation use separate `triggers/deliveries:read`, `triggers/deliveries:retry` and `triggers/deliveries:cancel` permissions. Request JSON cannot choose the trusted tenant or persistence scope.

Invocation grants belong to the resource and association. The creator and granting manager are audit identities, not credentials reloaded to authorize every source delivery. Removing the creator's membership does not revoke that resource grant. Use the reviewed `RevokeInvocation` command to stop fresh admission through it. Explicit host start policy is still checked at admission and dispatch; a later policy denial leaves accepted work retained for recovery.

Changing source configuration revokes existing grants. Changing an association's target or mappings clears its grant unless the change explicitly requests a new authorized grant. Inspect grants again after edits. Provider-owned read-only configuration/associations reject local configuration and authority edits; use their projected actions for permitted local operations.

Fresh admissions follow the slot's current serving version. A compatible replacement needs no association rewrite; an incompatible declared input contract blocks fresh admission until mappings are corrected. Unpublication makes fresh admission unavailable. Restoration makes the association eligible only when the serving contract, adapter and grant are valid. Already accepted work retains its captured version, normalized inputs, source provenance and authority; it does not retarget to the replacement.

## Choose HTTP response and admission behavior

| HTTP settings | Source behavior in this implementation |
|---|---|
| `"http": { "mode": "WorkflowResponse" }` | Exactly one enabled subscription, with workflow-response support. The request waits for that root's committed `HttpResponseInstruction`, normally produced by `WriteHttpResponse`. Direct BPMN targets advertise no workflow-owned response support. Do not supply `admissionPolicy`. |
| `"http": { "mode": "AsyncFanOut", "admissionPolicy": "RequireAll" }` | All enabled subscriptions must pass admission and mapping. Any refusal accepts no cohort; otherwise return `202`. Omitting the policy selects this default. |
| `"http": { "mode": "AsyncFanOut", "admissionPolicy": "AcceptAvailable" }` | Accept independently valid subscriptions and record refusals. Return `202` only with at least one accepted recipient; mixed results report `PartiallyAccepted`. |

A `202` confirms accepted delivery responsibility. It does not establish that workflows have started or completed. The response contains `deliveryId`, `statusLocation`, `admission`, `admissionPolicy`, `acceptedCount`, `refusedCount`, opaque recipient references and `replayed`. Reading the delivery still requires management-side delivery permission.

With the default endpoint mount, send the HTTP example using:

```bash
curl --request POST "$trigger_endpoint" \
  --header "Authorization: Bearer $access_token" \
  --header 'Content-Type: application/json' \
  --header 'Idempotency-Key: order-A-42' \
  --data '{"orderId":"A-42"}'
```

Set `trigger_endpoint` to the resolved shell endpoint, such as its `/workflows/http/orders` route, and obtain `access_token` through the host's ordinary authentication. The idempotency key is optional, but supplying a stable key lets a producer reconcile uncertain admission. It is bounded to 128 characters without control characters. Preserve the same submitted request and source identity on retry; changed intent under an accepted key conflicts. Accepted replay returns the captured result before today's mappings, grants or enabled state are consulted. A new key is a new source occurrence.

Invalid or unsupported request bodies return `400`; bodies exceeding `HttpEndpointOptions.MaxRequestBodyBytes` return `413` before admission (default 1 MiB, narrowed by matching ordinary wait policy when necessary). Complete admission refusal is `400` for input/mapping findings, otherwise `503`. Source authorization uses `401`, generation/ownership/replay conflicts use `409`, and unavailable protected composition uses `503`.

WorkflowResponse uses `HttpEndpointOptions.SyncRequestTimeout` for response observation (default 30 seconds). A terminal workflow without a committed response instruction returns `502 ResponseMissing`; a response deadline returns `504 ResponseTimedOut`. Timeout, disconnection or failed response writing does not cancel accepted work. Response observations (`WriteCompleted`, `TimedOut`, `ClientDisconnected`, `WriteFailed`, `ReceiptUnknown`) are separate from recipient dispatch and workflow lifecycle. A response-write receipt does not prove the client received the bytes.

An activated HTTP resource reserves its route across pause. Publication of an embedded endpoint and activation of a standalone resource arbitrate the same physical namespace; conflicting owners refuse. Pausing stops fresh admission but preserves the reservation. Ordinary matching waits are captured before new roots execute, retain their existing execution and bookmark generation, and receive no request services or response stream. RequireAll refusal or zero accepted subscriptions causes no wait resumption; only the configured subscribed root owns WorkflowResponse.

## Events and missed schedule occurrences

The ordinary broadcast `PublishEvent` path selects enabled Event resources matching `eventName`, alongside its ordinary embedded starts and saved waits. Local Events keep their existing same-execution behavior and do not start standalone subscriptions. The producer checkpoint records an owed send; the later physical cohort commit captures the subscribers and serving versions. These are distinct times. An application-bound Event producer instead submits to the explicit resource ID through its typed port; it supplies its own detached payload/metadata and trusted server context.

Timer and Cron own one schedule per resource, regardless of subscriber count. After downtime or failed admission, the persisted due occurrence stays overdue with the same occurrence identity. No eligible subscriber is a refusal, so the cursor does not advance. After a nonempty cohort is accepted, the pump advances to the next future instant using its current clock; it does not replay every missed interval. Inspect `nextDueUtc`, `generation`, `timezone`, `offset`, `isOverdue`, `failureCount`, `nextRetryAt` and `occurrenceInFlight` when present.

Intentional pause deactivates the schedule. Re-enable calculates a new future occurrence from now; source configuration/timezone edits also create a replacement generation. Already accepted deliveries continue against their old capture. A backward clock does not move the cursor back; a forward jump makes the persisted occurrence overdue and uses the one-catch-up policy. Timezone IDs must be available consistently on participating hosts.

## Inspect and recover accepted work

Use the discovered `deliveries` and `delivery` relations; the list supports `triggerId`, `limit` (1–100, default 25) and opaque `after`. Current implementation does not provide the proposed outcome/time-range filters. Treat server-issued delivery and recipient IDs/revisions as opaque. The views keep admission, dispatch, linked execution and the latest HTTP transport observation separate.

| Recipient evidence | Operator meaning |
|---|---|
| `NotDispatched` with accepted admission | Responsibility exists; no dispatch seal has committed. |
| `DispatchCommitted` | Runtime has irrevocably committed this exact target command. It may still need enqueue or receipt reconciliation; queued/running is not established. |
| `Queued` | Actual scheduler/model-input receipt was observed. |
| `Started` with `executionReference` | The ordinary execution can be inspected under workflow read permission. Its lifecycle is separate from delivery status. |
| `Refused` / `NotDispatched` | This association was refused before acceptance and is not retryable as accepted work. Submit a new occurrence after fixing it. |
| `Cancelled` | Pending new-root responsibility was explicitly cancelled before dispatch commitment. |

Recovery uses the ordinary Runtime outbox processor, deterministic recipient/command IDs and captured inputs. Automatic retry waits five seconds between attempts. Missing adapters, unavailable target evidence and policy denial preserve owed work and report recovery findings; successful recipients are skipped on later attempts. `TargetAccepted` transfers responsibility to the normal runtime, not to workflow completion.

Retry and cancel-pending use the same preview/apply routes as configuration changes. Their command contains `kind` (`RetryRecipients` or `CancelPendingRecipients`), `deliveryId`, `expectedDeliveryRevision`, and `recipients: [{ "recipientId": "…", "expectedRevision": 1 }]` copied from the latest view. Select 1–500 distinct accepted new-root recipients. Follow each returned effect; a concurrent dispatch can win after preview. Cancellation then reports `DispatchCommitted` and leaves that command owed. Cancellation never cancels an already queued/running workflow, and ordinary saved waits are read-only on this surface.

Pause, association removal, grant revocation, unpublication and trigger deletion stop or change future intake; they do not cancel accepted work. Deletion retains a tombstone for delivery-history authorization. Do not manually delete source/outbox rows to clear an alarm: those rows retain captured material, deduplication and command authority. The protocol has no automatic source-key expiry or configurable receipt-retention window. Material remains rooted while accepted work is unresolved; after source delivery completes, guarded collection still requires the deterministic target's deletion tombstone. Missing target state alone cannot release it. Explicitly cancelled unsealed roots can release their own material hold, while operational receipts remain.

## Limits still requiring qualification

Provider/process restart, multi-worker races, actual due Timer/Cron firing, publication replacement, genuine BPMN inputs/service work, external module consumers and the complete Studio experience require the linked qualification evidence before support is claimed. Limited delivery filters and absent automatic receipt expiry are current implementation limits. Expression availability depends on the actual installed portable handlers/evaluator and profile support.

The direct BPMN target accepts captures from the standalone source arm and from a saved workflow-origin cohort, validating the tenant from the corresponding physical source capture. Its dispatch still requires the ordinary model protocol, in-process actor, queue and committed source seal. The full real PublishEvent-to-BPMN host journey remains pending qualification. Neither this guide nor a green isolated test establishes the full feature's delivery acceptance.
