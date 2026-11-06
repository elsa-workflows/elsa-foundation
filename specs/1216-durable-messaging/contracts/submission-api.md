# Submission, receipt and activity contract

**Status: implementation-plan candidate; NOT IMPLEMENTED / NOT RUN.** Q1–Q3 are accepted product decisions; the names, limits and mechanisms below are engineering proposals for review. Apply the [delivery design](delivery-design.md), especially model-neutral definition identity, complete frozen eligibility, reservation fencing and independent clocks. No route, capability, permission or provider is certified by this document.

## Existing integration patterns

Reuse [Runtime API capability declarations](../../../Essentials/src/Workflows/Runtime/Api/Capabilities/RuntimeApiCapabilities.cs), the [physical-boundary capability check pattern](../../../Essentials/src/Workflows/Runtime/Api/UserTasks/UserTaskCapabilitySource.cs), NativeEndpoints/API problems, [permission contributions](../../../Essentials/src/Workflows/Runtime/Api/Authorization/WorkflowRuntimePermissionContributor.cs) and [canonical current-subject authorization](../../../Essentials/src/Workflows/Runtime/Api/UserTasks/TaskRequestAccess.cs). These are observed seams; none currently implements Messages.

Propose optional capability `elsa.api.messages`, version `1`, source feature `WorkflowsRuntimeApi.Messages`. Declare links through the existing capability service only when the actual composed physical source, interest, quota, reservation, checkpoint, payload and retention boundaries support the promised operation. Clients discover relations instead of assuming hardcoded URLs. Capability discovery describes host support, not a grant to the current caller. Recheck capabilities and current authorization at every request; do not advertise durable support merely because an interface is registered. Unsupported or mixed compositions refuse before acceptance.

## Candidate HTTP relations

Paths below are relative to the existing API base; path identities are opaque server-issued IDs, never concatenated raw definition IDs or business keys.

| Relation | Method and candidate path | Behavior |
|---|---|---|
| `message-policy` | `GET runtime/messages/policy` | Permitted effective limits, lifetime defaults, mode/provider/model support and producer-identity kind; no secret or foreign scope values. |
| `message-submit` | `POST runtime/messages` | Atomically accept one message or resolve its existing producer-ID acceptance. |
| `messages-page` | `GET runtime/messages/page` | Authorized bounded metadata collection; filters and opaque continuation. |
| `message` | `GET runtime/messages/{receiptId}` | Authorized receipt, effective policy and truthful progress summary; no payload. |
| `message-deliveries-page` | `GET runtime/messages/{receiptId}/deliveries/page` | Paged authorized per-definition states and available target links/actions. |
| `message-payload` | `GET runtime/messages/{receiptId}/payload` | Explicit separately authorized/audited payload resolution. |
| `message-delivery-retry` | `POST runtime/messages/{receiptId}/deliveries/{deliveryId}/retry` | Idempotent redrive only of an open Selected winner with a retryable/intervention attempt. Closed Failed/Abandoned/Delivered outcomes cannot reopen. |
| `message-delivery-abandon` | `POST runtime/messages/{receiptId}/deliveries/{deliveryId}/abandon` | Audited closure under proved-unconsumed/closed capability rules; never a blind unlock. |

Read actions return only links the caller may follow; target run/definition access remains separately checked by its owner. A submitter may resolve their own acceptance/duplicate without receiving tenant-wide read permission. Direct receipt access is scoped and authorized before existence disclosure. Collection filtering never expands scope. Use a maximum page size of `100` (default `50`), deterministic ordering and context-bound opaque cursors; stale/foreign cursors fail safely rather than falling back to another tenant. Business-key search uses a request query value, not a path identity, and follows normal protected request-log policy.

## Submission wire and equality

```json
{
  "name": "PaymentReceived",
  "correlationKey": "Order 123",
  "messageId": "payment-provider:transaction-945",
  "lifetimeSeconds": 86400,
  "payload": { "amount": 125.50, "currency": "EUR" }
}
```

`name`, `correlationKey` and `payload` are required; payload may explicitly be JSON null. Omit `messageId` to disable producer-ID recognition. Omit `lifetimeSeconds` to use the snapshotted deployment default; `0` selects immediate-only. Explicit null for optional ID/lifetime is invalid. No caller-supplied tenant, producer namespace, definition cohort, execution authority, recipient, clock, receipt ID or deduplication duration is accepted. Reject unknown envelope members and duplicate object members rather than silently ignoring conflicting intent.

Names, keys and IDs use exact ordinal matching without trimming, case folding, Unicode normalization or numeric parsing. Empty/whitespace-only values are invalid; leading/trailing nonblank content remains significant. Payload is durable JSON, not a CLR type token, live object, stream or automatic Secret resolver. Check size and representability before acceptance; recipient conversion occurs only after address/order selects and persists its exact winner.

Use a versioned fingerprint of name, exact key, defined payload and sender lifetime intent. Canonicalization preserves property/string casing and JSON number token spelling, ignores insignificant whitespace/property order, and rejects duplicate payload object members. Thus `1` and `1.0` are different submitted content in this candidate; no global serializer-policy change is implied. Include every future delivery-affecting envelope field under an explicit fingerprint version. Exclude transport tracing, access tokens and raw credentials. Omitted lifetime remains distinct intent from an explicit duration, even if both resolve to the same original default.

For a live producer-ID address: identical intent returns the original acceptance, original policy snapshot and current permitted summary; changed content returns conflict and leaves every original delivery intact. Compare the original request intent before resolving today's defaults so a configuration change cannot break an identical retry. A duplicate never extends either deadline. After the independent deduplication deadline, that ID may accept a new message even if the original still has pending deliveries; its original receipt/source remain separate.

## Trusted producer namespaces and access

Propose default HTTP producer namespace `user-v1:<canonical-user-id>` within the already selected persistence scope and canonical tenant. Derive it from one validated normalized `IdentityClaimTypes.UserId` and the owning canonical tenant/global-tenant policy, with current persisted user/grant checks. Reject ambiguous/missing identities; never substitute display name, email, token text, an arbitrary `sub`/`client_id`/application claim or request header. Credential rotation for the same canonical user retains the namespace. Canonical tenant attribution must agree with the explicit persistence scope; null/global attribution never grants across-scope access.

A narrow host-owned application producer resolver is required for the first-release integrator example. Ship a configured adapter that authenticates using the explicitly configured host scheme, requires a successful result and one unambiguous validated issuer/subject, then maps that pair through current server configuration to stable application ID, tenant and permitted operations. Reject absent/disabled/ambiguous mappings; recheck current mapping/policy even for duplicates. Credential rotation preserves the stable mapping. Claims/headers cannot choose namespace or tenant. Do not invent another authentication scheme or trust app-like claims. A host may replace this narrow resolver with its existing persisted application adapter. Qualify real authenticated HTTP submission, rotation, revocation, conflict and cross-application/tenant isolation without requiring the separate #2852 management product.

Propose dedicated permissions contributed through Foundation Identity's existing catalog:

| Permission | Scope |
|---|---|
| `workflows/messages:submit` | Submit and reconcile the caller's own producer-ID acceptance. |
| `workflows/messages:read` | Read permitted receipt/delivery metadata; resource filtering still applies. |
| `workflows/messages:read-payload` | Resolve permitted payload, in addition to authorized receipt access; audit reveal. |
| `workflows/messages:manage` | Offered retry/abandon operations under current resource policy; never authority to reselect. |

Use current-subject/resource authorization and the owning Runtime execution policy rather than static role-name checks. Payload permission is separate from metadata read and management. A namespace conflict or foreign receipt cannot be used as an existence oracle. Lists, errors and normal logs exclude raw payload, credentials, resolved secrets and unauthorized identities; safe counts/target names obey their own resource disclosure policy. Authored sends use immutable runtime origin plus current permission checks, not a recovered HTTP principal.

## Receipts and HTTP outcomes

```json
{
  "receiptId": "msg_opaque",
  "acceptance": "Accepted",
  "duplicate": false,
  "acceptedAt": "2026-11-05T12:00:00Z",
  "mode": "Buffered",
  "bufferUntil": "2026-11-06T12:00:00Z",
  "deduplicateUntil": "2026-11-12T12:00:00Z",
  "progress": { "pending": 2, "selected": 0, "delivered": 0, "expired": 0, "failed": 0, "unmatched": 0 },
  "revision": 1
}
```

Include discovered relation links in the actual response. `deduplicateUntil` is null without producer ID; an immediate-only receipt has mode `ImmediateOnly`, no open buffer deadline and its original selected/unmatched outcomes. Acceptance is separate from delivery and recipient-run completion. Per-definition delivery exposes exact safe state/reason/revision, permitted target links and currently offered recovery actions; immutable captured target pins need not be disclosed to an unauthorized caller. Partial success remains visible. If outcome counts are restricted, expose a disclosure-limited summary rather than misleading complete totals.

| HTTP status | Candidate outcome and retry rule |
|---|---|
| `202 Accepted` | Newly committed receipt; `Location` links to it. Includes confirmed unmatched immediate-only; success does not imply delivery. |
| `200 OK` | Same-ID/same-intent duplicate; original receipt/deadlines, `duplicate=true`, permitted current progress. |
| `400 Bad Request` | Invalid shape/key/duration/content/size; no accepted source, consumed ID or target reservation. |
| `401 Unauthorized` / `403 Forbidden` | Existing authentication/permission semantics for unauthenticated or denied operations; do not disclose foreign resource existence. |
| `404 Not Found` | Missing or non-disclosable receipt/delivery/payload resource under scoped authorization. |
| `409 Conflict` | Same-ID changed intent, stale recovery revision or unavailable recovery transition; stable safe reason code. Original work unchanged. |
| `429 Too Many Requests` | Explicit preacceptance quota refusal; bounded `Retry-After`; no partial cohort/ID/key/wait reservation. Retry same ID/content. |
| `503 Service Unavailable` | Unsupported/unavailable required physical capability or unresolved commit outcome, distinguished by safe `outcome=NotAccepted` or `outcome=Unknown`. Unknown means reconcile/retry the same ID/content; never assume rejection. |

Use existing API Problem Details integration and bounded machine-readable reason codes; preserve internal causes privately. Connection loss, cancellation after commit submission and `Unknown` are uncertain regardless of what a client observed. A same-ID retry inside its window reconciles original committed evidence; without ID an HTTP retry can create another acceptance. Recovery posts require caller `operationId` plus expected delivery revision; replay of the same operation returns its committed result, and changed operation content conflicts. Responses/reads carry no-store cache policy for protected state/payload.

## Workflow activities and permanent source idempotency

Candidate activities are `SendMessage` and `ReceiveMessage`, with published start mode under ordinary publication authority. Start mode captures the admitted envelope business key rather than evaluating a pre-existing workflow value; wait mode evaluates its expected business key. Both output exact message name, correlation key, payload and receipt reference. Later waits can bind the original envelope key even when absent from payload. Carry the same typed result through native/direct-model paths; do not assume execution CorrelationId supplies it. Declare fixed or explicitly finite Message names. Start payload uses the dedicated stimulus channel, not merged workflow inputs or a synthetic publisher.

`SendMessage` accepts the same name/key/payload/lifetime/optional producer ID and outputs the durable receipt reference plus acceptance/duplicate result. It commits its canonical send intent and private exact receipt wait at the owning workflow checkpoint before any acceptance effect. Existing Runtime delivery accepts that committed source, then resumes the private wait only with durable acceptance evidence; the activity does not complete merely because a command was queued. Temporary unavailability keeps the same owed source/wait; ambiguous acceptance reconciles it. Permanent validation/refusal produces the owning activity's explicit failure with no fabricated successful receipt.

Runtime-origin producer namespace derives from the trusted logical sender definition namespace/ID and tenant, never authored namespace text. Independently, permanently map the exact committed send-source identity/fingerprint to its accepted-message identity. This source-to-outcome mapping is sticky beyond producer deduplication expiry, source redelivery, restart and recipient completion, including when the author omitted `messageId`. Retain acceptance/retirement tombstones through source/execution collection so replay can never create a second acceptance. A genuine new invocation/send occurrence has a new committed source identity; intentional producer-ID reuse still follows its independent window. Source idempotency is not a hidden sliding producer window. Persist a create-only source outcome union: Accepted(original receipt), PermanentlyRejected(sanitized reason) or Cancelled(authoritative sender cancellation proof). Definitive post-checkpoint refusal without an inbox record still creates an exact rejection continuation; its guarded callback consumes the private wait and faults the same SendMessage invocation atomically. Temporary or uncertain results remain unresolved source work. A rejected/cancelled source cannot later accept after permissions change; a genuine new invocation has a new source. Retain callback responsibility until consumption or authoritative sender retirement, never mere missing state.

Owner decision, 6 November: a workflow cancellation that commits before Message acceptance stops that pending native send; record a distinct Cancelled source outcome without an inbox receipt or callback. Acceptance and native cancellation serialize through one retained sender control inside their actual transactions. The first send checkpoint reserves its outcome/continuation capacity; cancellation seals the control without enumerating source history. Cancellation uses sender checkpoint authority, independently of the source worker's transport claim; the current claimant later settles Cancelled without accepting. If acceptance committed first, keep the accepted message and all recipient obligations; close an undeliverable sender continuation only with authoritative cancellation/retirement proof. A cancellation request, lost acknowledgement or missing sender state alone cannot establish the winner. Before the original send checkpoint committed there is no durable send to retract.

## Candidate defaults and complete admission bounds

These are conservative implementation-plan defaults for review/testing, not approved production sizing. Deployment can configure lower effective quotas within module bounds; sender overrides cannot exceed deployment policy. Capture the effective values/version at acceptance.

| Setting | Candidate default / supported bound |
|---|---|
| Default buffer lifetime | `24h`; configurable deployment maximum defaults to `30d` within `1s..30d`, and default must not exceed it. Reject sender overrides beyond that maximum without clamping. `0` is immediate-only, never an expired pending buffer; integer seconds only. |
| Producer-ID deduplication lifetime | `7d`; deployment setting `1s..90d` when ID is supplied. Omit ID for no producer dedup; zero does not silently disable recognition. Independent of buffer lifetime. |
| Request size / text limits | Raw HTTP body and shared normalized transport envelope each at most `1MiB`; name `256`, key `1024`, producer ID `256` UTF-8 bytes. The shared envelope includes name/key/payload and present optional fields, canonical payload and explicit JavaScriptEncoder.Default escaping; exclude internal fingerprint-version/null omission markers. Native/API use the same bound. Reject before acceptance. |
| Frozen definition cohort | At most `256` eligible logical definitions per message; never truncate. An over-limit complete population explicitly refuses acceptance. |
| Acceptance-time candidate work | At most `4096` exact candidates across the complete immediate-only cohort; exceedance refuses the whole acceptance, never reports a clipped set as unmatched. |
| Scope backlog / payload capacity | `10,000` accepted sources with outstanding delivery obligations and `64MiB` outstanding payload bytes; reserve complete cohort obligations atomically. |
| Scope recipient obligations / dedup identities | `100,000` outstanding per-definition obligations and `100,000` live producer-ID identities. Count selected owed work; reserve identity capacity before promising its window. |
| Retained native-source bindings / terminal receipts | `100,000` replay-proof source bindings and `100,000` terminal receipt metadata records per scope. Reserve independently from outstanding work/live producer IDs; do not evict replay proof merely because delivery/dedup completed. |
| Operator history retention | `30d` by default, configurable `1d..365d`. This bounds eligible terminal audit metadata, not pending/selected work or the minimum receipt/proof required by a longer deduplication window or live source. |

Quotas apply to actual authoritative state across workers, not process-local counters. Original accepted duplicates resolve without allocating another source/payload/cohort. All new acceptance writes, ID/source mapping, complete interest snapshot, quotas and immediate-only reservations commit together or leave none. Reject incomplete/unavailable discovery before commit. If completion of that boundary is uncertain, reconcile its existing identity instead of rerunning immediate-only selection against newer listeners. Accepted obligations are never evicted to relieve quota pressure; independent window/history cleanup releases only eligible state.

Native-source binding capacity is released only after authoritative source retirement closes every possible redelivery/continuation path and retained proof prevents replay from accepting anew. It is never reclaimed on a producer-window timer. If that proof cannot be established, retain the binding and apply explicit backpressure. Preserve a minimal original receipt for at least the deduplication window and all outstanding obligations even when configured operator-history retention is shorter; inspection reports unavailable history/payload truthfully. Payload deletion follows actual owed-reference/disclosure roots, independently of these metadata quotas.

## Intended support declaration — all NOT RUN

First-release targets are authored native workflows, embedded BPMN and genuine directly hosted BPMN starts/catches, sharing model-neutral authored `WorkflowDefinition` identity. Runtime-only and combined Runtime/Design hosts use persisted provenance/interests without Design queries. EF SQLite is single-process with restart durability; EF PostgreSQL, SQL Server and MySQL include single-worker and at least two independently composed shared-store workers. Balanced remains the host default; every Message acceptance/selection/consumption/release boundary requires coherent physical durability under the claimed cadence. Additional modes require explicit qualification.

Default upgrade support is a quiesced homogeneous binary fleet. Workflow publication versions still compete across the same logical definition as selected by the owner; that is separate from software rolling-upgrade support. Native Message source/finalization requires the existing outbox family's successor v3, guarded target work requires scheduler v4 and a Message-aware required participant, and producer guards refuse earlier profiles. Legacy Event writes remain valid at v2. In-process fleet membership answers cover only the local host. Live mixed-binary rolling operation is not advertised without complete durable membership and actual claim/rewrite/target/cleanup qualification; metadata alone does not fence stale or ungated writers.

In-memory storage is only an explicitly disclosed volatile development mode and cannot advertise durable production support. Mixed unsupported physical stores, unqualified SQLite multiprocess/multinode and undeclared model adapters refuse capability/admission with a reason. Qualify all advertised providers/models/topologies with the [acceptance matrix](../acceptance-matrix.md), package consumers and composed API/activity/Studio journeys. No claim in this support declaration is currently implemented or certified.
