# Feature Specification: Finite HTTP ETL

**Feature Branch**: `agent/codex/3151-http-etl`
**Created**: 2026-10-21
**Status**: Approved
**Input**: Owner-approved HTTP connector scope in [#2908](https://github.com/elsa-workflows/elsa-foundation/issues/2908), delivered through [#3151](https://github.com/elsa-workflows/elsa-foundation/issues/3151) in [Program #2892](https://github.com/elsa-workflows/elsa-foundation/issues/2892).

## User Scenarios & Testing

### User Story 1 — Publish and run an HTTP integration (P1)

An integration author selects approved source/destination bindings, describes the JSON records and pagination, maps and validates fields, optionally joins a bounded local lookup, and publishes a pipeline in Studio. A workflow invokes that published version and continues after its declared outcome.

**Independent test:** Publish through the real authoring API/Studio, select the version in a Sequence and a Flowchart, execute against a controlled HTTP source/receiver, and observe the following native activity after successful delivery.

**Acceptance scenarios:**
1. Given two configured source shards with multiple pages, both partitions progress independently and produce the mapped records; one delayed shard does not stop the other.
2. Given either HTTP input or output, the author can combine HTTP input with JSONL output, CSV input with HTTP output, or HTTP at both ends. A lookup is optional for the new profile.
3. Given a retained published workflow and subsequent draft/configuration changes, execution uses its retained artifact and selected binding revision without consulting design storage.
4. Given an existing v1 file artifact and saved workflow, opening, pinning, executing and inspecting it preserve the original hash and behavior.

### User Story 2 — Recover without losing accepted responsibility (P1)

An operator can restart workers while input pages or remote deliveries are in progress. Elsa preserves accepted responsibility and reports only progress that was durably accepted.

**Independent test:** Interrupt real worker processes at page publication, reservation, remote acknowledgement and progress acceptance; a successor finishes from retained state against the same durable database/object store.

**Acceptance scenarios:**
1. After a page is retained, a replacement uses that page rather than silently substituting changed remote content. It reuses the owed batch body and effect identities.
2. If a receiver accepted a request but its response or Elsa's progress write was lost, the same body/key is replayed. A cooperative receiver deduplicates it; an ordinary receiver may observe repeats, which are explicitly at least once.
3. An expired/replaced owner may finish physical I/O but cannot advance progress, release another attempt's resources or win terminal settlement.
4. A successful main route followed by an error-route failure remains recoverable with stable identities for both routes. No accepted route is hidden by the retry.

### User Story 3 — Handle delay and failure visibly (P1)

Rate limits yield worker capacity while retaining the retry and owed work. Operators see safe progress, next-attempt timing and explicit terminal outcomes.

**Independent test:** A controlled endpoint returns rate limits, transient failures, malformed bodies, permanent failures and 202; restart during delay and inspect persisted and displayed state.

**Acceptance scenarios:**
1. A valid Retry-After is honored after restart; no request is issued earlier. Other partitions can run while one is delayed.
2. Retry exhaustion, malformed/oversized pages, pagination loops and unsupported acknowledgements are explicit diagnostics. HTTP 202 is not presented as completed delivery.
3. Cancellation stops eligibility for new effects/progress and settles once; already-started remote effects may finish. The surrounding workflow's cancellation contract remains intact.
4. Inspection contains safe binding identity and page/item counts, retry timing and accepted-effect counts, never credentials, URLs or raw continuation tokens.

## Requirements

### Functional requirements

- **FR-001** Publish a versioned HTTP-capable artifact with explicit connector kinds, JSON record/field selection, pagination policy, typed schema, mapping/filter/validation/error routing, optional bounded CSV lookup and finite limits. Existing v1 bytes and interpretation are unchanged.
- **FR-002** Bind endpoints and shard seeds through host-approved aliases scoped to tenant/persistence partition. Pin revisions at admission. Authors cannot insert arbitrary destinations or credentials into a pipeline.
- **FR-003** Support JSON Pointer record-array/field selectors and two declared continuation profiles: opaque scalar token inserted into a named query parameter, or a same-origin next-link selected from JSON. Missing/null/empty continuation ends the shard; a nonempty next value continues even when the page has no records. Invalid shape, repeated continuation and configured page limit produce diagnostics.
- **FR-004** Each HTTP shard has its own durable page/item position and exhaustion state. Retain one bounded immutable current page per partition before record effects. Do not require complete input materialization or a known total record count.
- **FR-005** Preserve finite logical record/byte admission before dispatch, independently from host-local allowances held until a physical attempt unwinds. Page fetch/parse, retained-page storage, lookup and output bodies also have finite bounds. Lease expiry proves neither worker termination nor memory release.
- **FR-006** Retain exact output bytes and route identity before HTTP dispatch. POST one JSON array per nonempty route batch, sending a stable idempotency key in the configured header. Accept only 200, 201 or 204 under the supported synchronous receiver contract; 202 and other unrecognized success shapes do not complete work.
- **FR-007** Retry network uncertainty and selected transient statuses (408, 429, 500, 502, 503, 504) with persistent bounded attempt/elapsed budgets and not-before time. Honor valid Retry-After delta/date values; if they exceed the remaining retry deadline, settle exhaustion rather than retry early. Other statuses fail explicitly. Retry the identical retained request body and effect key. Each attempt has a finite timeout no longer than the remaining retry deadline; timeout is an uncertain external effect, not proof of failure before delivery. Cooperative cancellation may outlive this deadline; do not release its physical allowance until unwind.
- **FR-008** Atomically accept effects, advance the partition position and settle admitted quota under the current unexpired fence. A fully filtered nonempty batch may accept zero effects, settle its reservation and advance without remote requests. Duplicate matching acceptance is accounting-neutral; conflicting/stale acceptance is refused. A finite run completes only after every shard is exhausted and every owed effect settles.
- **FR-009** Resolve connection credentials at point of use using existing tenant-scoped authentication services. Pin endpoint/seed metadata and non-secret authentication configuration; changed configuration, revocation or missing connection prevents further requests. Secret rotation is supported without persisting secret values. Authorize publication with the existing `etl/definitions:publish` permission and require the scoped host-approved binding alias. Authenticated bindings require a non-null retained tenant and are rejected at publication/admission otherwise; never substitute a fallback tenant. Avoid credentials in shared handler state.
- **FR-010** Validate continuation destinations before applying credentials. Automatic redirects are disabled in this profile; redirects are explicit unsupported responses. Default HTTPS; a host must explicitly enable HTTP for a controlled environment. ETL endpoint policy always applies, including anonymous requests. Connection host policy additionally applies when a connection is configured. Next links must match a host-approved canonical path as well as the pinned origin; the default path allowlist contains only the seed paths.
- **FR-011** Persist one terminal outcome and owed native delivery. Sequence/Flowchart continuation, business error, fault and cancellation retain their existing contracts. Physical effects are not rolled back.
- **FR-012** Studio and the scoped authoring/runtime APIs expose connector selection, pagination/schema configuration and safe partition/retry/effect inspection, retaining existing file authoring and run inspection.
- **FR-013** Unknown persisted formats are diagnosed without being reinterpreted. Upgrade tests resume previously accepted v1 file runs and new HTTP work. A runtime-only deployment needs no draft or design database.

### Key entities

Published pipeline; revision-pinned endpoint binding; source shard; page position and retained page; owed batch; retained delivery request and acknowledgement; retry schedule; accepted progress; terminal result. Meanings remain ETL-owned under [ADR 0087](../../docs/adr/0087-etl-owns-its-execution-domain.md).

### Edge cases

Fully filtered batch with zero effects; empty page with/without continuation; malformed, duplicate-key, deeply nested or oversized JSON; invalid pointer; optional/missing/null/ill-typed fields; pagination loops; page count exhaustion; lost response; body/key conflict; two routes with partial remote success; delayed retry through restart; request timeout; unsupported 202; cancellation versus acknowledgement; binding disappearance/configuration change/credential rotation/revocation; stale owner; missing/corrupt cache; unknown artifact/state format; old v1 publication/run after upgrade.

## Success criteria

- **SC-001** Real Studio/HTTP/native journeys demonstrate all three connector combinations, with two independently progressing HTTP shards and visible continuation into Sequence and Flowchart.
- **SC-002** Every named interruption boundary has a recovery result showing retained request identity, no loss of accepted owed work and no duplicate progress accounting; physical request duplicates are reported honestly.
- **SC-003** Deterministic rate-limit tests show no request before the retained not-before time, including after restart, and demonstrate another partition progressing during delay.
- **SC-004** Existing persisted v1 artifacts retain canonical hashes and file behavior after upgrade. A pre-change runtime database containing an owed/reserved file batch migrates and resumes under a successor without changing its identity or double accounting; runtime-only execution succeeds with design storage unavailable.
- **SC-005** Full affected tests, rebuilt server/browser evidence, reviewed negative controls, exact-head checks and resulting-main verification pass before the slice is complete. Skips and unexecuted evidence are listed separately.

## Assumptions and boundaries

This is a finite connector slice, not completion of the parent ETL feature. Continuous/SQL/RabbitMQ/BPMN integration and broader transform/topology obligations remain on #2908. APIs must provide a documented finite pagination contract; Elsa cannot create a collection-wide snapshot of an arbitrarily changing remote API. A durably retained page is replay-stable; records never captured from a changing endpoint have no invented no-loss guarantee. Endpoint snapshots/watermarks are honored when supplied by the host's shard seed. Receiver idempotency is a receiver capability, not an HTTP-header guarantee.

No new actor provider, generic execution kernel, worker framework, task-runner/pause feature, workflow permissions or release configuration is included. The owner's future task-runner intent remains in the program follow-up.
