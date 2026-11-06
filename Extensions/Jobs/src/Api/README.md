# Jobs operations API

The optional API exposes accepted Jobs state through the host's configured authority. A coordinator
does not need to install a handler or enable its attempt worker. The host configures Jobs persistence,
Foundation authentication/current-subject authorization, then registers `AddJobsApi` or `JobsApiFeature`.
Its logical partition must match the producers/workers whose work operators should manage.

The capability `elsa.api.jobs/1` supplies shell-relative links. Routes are:

| Route | Operation |
|---|---|
| `GET /jobs`, `POST /jobs` | Bounded metadata list; independent submission |
| `GET /jobs/access` | Current actor and module permissions |
| `GET /jobs/{jobId}` | Retained metadata and exact result delivery classification |
| `GET /jobs/{jobId}/input`, `/result` | Separately authorized bounded payloads |
| `GET /jobs/{jobId}/attempts`, `/physical-receipts`, `/holds` | Attempt facts, late receipt metadata and matching gate pages |
| `POST /jobs/{jobId}/cancel`, `/retry` | Cooperative cancellation; linked Retry as new run |
| `GET /jobs/queues` | Authorized queue counts |
| `GET /jobs/schedules`, `GET/PUT /jobs/schedules/{scheduleId}` | Schedule metadata; revisioned creation/edit |
| `GET /jobs/schedules/{scheduleId}/input` | Separately authorized schedule input |
| `POST /jobs/schedules/{scheduleId}/enabled` | Enable/disable without deleting accepted jobs |
| `GET/POST /jobs/holds`, `POST /jobs/holds/{holdId}/release` | Inspect/add independent holds; release one exact hold |
| `GET/PUT /jobs/capacity` | Inspect/change revisioned live-permission limits |

List requests use `limit` (1–100) and protected `after` (at most 16,384 characters); Jobs additionally
accepts `queue`, `jobType` and `state`. Holds accept a complete `kind`, `name`, `modelKind` gate filter.
Cursors encrypt provider state through the host's ASP.NET Data Protection key ring and bind it to the
server partition, tenant, subject, collection, exact filters and page size. The host owns shared key-ring
configuration across its nodes. Every page rechecks current authorization. Per-job holds return separate
pages for each matching gate. Continue those through `/jobs/holds` with that gate, page size and cursor;
never treat an incomplete page as eligibility.

Submissions have `idempotencyKey` and `invocation` fields. An invocation contains `handler`, `input`,
`resultSchema`, `queue`, `policy` and optional `notBefore`, matching the [Core contracts](../Core/README.md).
Administrative bodies include `operationId` and a bounded `reason`; revisioned actions also carry
`expectedRevision`. Keep these original identities after a lost or unconfirmed response and reconcile
with the same content. Retry returns a new linked JobId while preserving the previous run and wait.
An exact committed schedule save can replay while recurrence arithmetic is temporarily unavailable;
new saves still require the provider's available calculator and retained malformed definitions refuse edits.

Metadata labels distinguish an active permission from physical entry. Result `settled` means the
owner supplied accepted consumption or positive terminal-parent proof; `owed` remains a delivery
responsibility. Unknown/absent delivery evidence is never presented as consumed. Pause prevents later
durable permissions, while already granted work can finish. A cancellation request is not proof that
physical code stopped.

Input/result routes return `state: "expired"` after actual configured cleanup, with retained schema,
original payload digest and expiry time, plus result identity/outcome where applicable. No value is
fabricated. Ordinary job detail/list metadata, parent association, result identity and settled delivery
remain visible under their existing permissions. Current payload-read authorization still applies to
expired projections. Retry after input expiry returns `jobs.payload.expired`; the original terminal
revision remains unchanged. Studio explains the expired bytes and exports retained metadata separately
from value-bearing payload documents.

Current source is uncompiled and unqualified. API/security/secret-binding, full composed model/Studio
and immutable package acceptance remain required by the [feature acceptance matrix](../../../../specs/1218-standalone-jobs/acceptance-matrix.md).
