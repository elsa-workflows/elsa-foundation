# Implementation Plan: Finite HTTP ETL

**Branch**: `agent/codex/3151-http-etl` | **Date**: 2026-10-21 | **Spec**: [spec.md](spec.md)
**State**: Approved for implementation after three bounded design review passes on 2026-10-21. Root exercised owner-delegated product/design authority; implementation acceptance is not established. See [design review](evidence/design-review.md).

## Summary

Deliver #3151 inside optional Elsa.Etl, extending the actual file/native/Studio product with HTTP ingestion and delivery. The current v1 file range and chunk model remains readable. New connector-capable artifacts use explicit v2 serialization and model-specific positions/effects. Reuse common run admission, lease authority, quotas, terminal settlement and native result delivery; do not copy the entire runtime or invent a generic execution kernel.

## Technical context

C#/.NET 10, existing EF runtime/design stores and SQLite recovery fixtures, BCL HTTP/JSON, existing Connections Core authentication contracts, React/TypeScript Studio module. No external connector library is needed. HTTP payloads/pages live in ETL's durable object store; relational state holds their identities, cursors and acknowledgement metadata. Runtime requires no design database. Heavy local validation is serialized through the machine's existing build queue.

No throughput SLA is added. Demonstrate finite logical payload/storage/admission bounds and independently progressing partitions. Page/cache/input/lookup/output limits do not assert a whole-process RSS cap.

## Constitution check

Read framework/Elsa v6.1.0, both gate indexes, review checklist and ADR 0087 at main `b555bd8460270476e49496e519c69b65180e9505`.

| Gate | Design disposition |
|---|---|
| Framework 2.1/2.2/2.18; Elsa E2.1 | Existing ETL domain; data/store shapes in Core, policies in scoped internal runtime/design implementations. No new root domain or foundation package. |
| 2.6.4; E2.2/E2.6 | Split design validation/catalog and runtime binding/execution. Native compiler embeds the supported artifact; no runtime draft query. |
| 2.9/2.10/2.20 | Typed domain state/store transitions; EF owns transactions/migrations. Share existing run/fence/accounting logic, keep HTTP page/effect state explicit. |
| 2.5/2.6/2.7/2.23.3 | Reuse public Connections Core ports rather than its internal/workflow applicator. Real replacement contracts declare single registration and fail on ambiguity. Track public additions; no cross-domain internal grants. |
| 2.13/2.14/2.16 | HTTP transport, worker orchestration and native/UI consumption remain separated. Keep BCL-only HTTP behavior in the current runtime package unless actual dependency/composition requires a separate leaf. No empty connector umbrella. |
| 2.21/2.23; E2.6 | Keep valid v1 tests and retained artifact fixtures. Failure cases, two-process recovery and actual producer/consumer/native journeys prove claims. |
| 2.22; E6 | Update actual extension catalogs/registered task documentation and public API baselines as contracts change. |
| 4/E5 | Keep computed package versioning. Wire/artifact readers explicitly accept v1 and v2; do not rename v1 constants or reinterpret saved state. Record any provider-facing source break before adoption; prefer additive interfaces for new HTTP operations. |
| 2.24/E2.8/E2.9 | Draft/provisional sections are not ratified here. No change to composite semantics, generic workflow node fields or catalog policy. |

No new exception requested. Review the final source/assembly dependency graph and actual provider API delta, not this table alone.

## Architecture and delivery decisions

### Versioned pipeline and compatibility

Keep the v1 `EtlPipeline`, canonical writer, compiler hash recipe and persisted reader unchanged. Add an explicit connector-capable document/reader for v2, sharing scalar schemas, mapping/filter declarations and limits only where semantics match. V2 has source and route destinations (CSV/HTTP source; JSONL/HTTP output) plus an optional CSV lookup. Native artifact pinning/validation dispatch by declared format and preserve original bytes/hash. Unknown versions fail with a safe diagnostic. Retained v1 fixtures come from the pre-change producer through actual publication and persistence: one published native workflow/artifact and a runtime database with a prepared source plus an owed/reserved file batch. Current readers migrate and resume the old state through a successor, preserving identities and accounting; recreating an old-shaped row with new producer code is not the upgrade proof.

### Source position and page retention

HTTP admission pins a bounded ordered set of host-defined shard seeds. Do not split an opaque remote collection by fabricated row ranges. One partition owns `(shard, request identity, page sequence, item offset, exhausted)` plus its retained current page. A request descriptor is persisted before fetching; source response content becomes replay-stable only after immutable page publication/attachment. Deterministic request-addressed cache publication may recover a complete unattached page; conflicting bytes are refused. No partial staging file is a page.

Bound raw/decompressed response bytes, JSON depth/fields/records, continuation bytes, per-partition page count and retained run bytes before allocating unbounded structures. The page reader selects arrays/fields without converting JSON to CSV. Share typed transform evaluation below the source parser. Cache retention and cleanup never remove a page/body referenced by an owed reservation or live attempt.

A page/item position changes only through accepted batch accounting or an explicit fenced empty-page transition. A nonempty batch whose records are all filtered has zero route effects: accept its exact input identity and filtered counts under the current fence, settle its reservation and advance without issuing an HTTP request. A zero-effect batch is valid only when its accounted rows prove that no output was owed. An empty page with continuation is not EOF. Maintain a bounded visited-request digest set up to the finite page limit to detect loops. Source consistency beyond retained pages is the endpoint contract; do not claim a remote collection-wide snapshot.

### Shared durable coordination

Introduce the smallest typed HTTP/connector store operations needed by actual runtime and provider consumers, leaving v1 provider contracts usable. Reuse common EF run lookup, authoritative clock, current lease/fence/terminal validation, quota accounting and terminal outbox helpers. HTTP claims must not appear as fake file ranges; file claims must not pick HTTP rows. Add explicit state format/kind, page/position, retry and route-effect persistence with upgrade migrations.

Reserve deterministic input bytes/records before transformation/effect dispatch. Retain exact per-route request bodies before sending; identity binds run/partition/input range and pinned destination, independent of worker/fence. One fenced transaction accepts matching effects and advances position/settles quota. HTTP receipt means an observed synchronous response, not a file publication or exactly-once proof. Partial main/error success retains route receipts; uncertain routes replay the same bytes/key. Only all exhausted/settled partitions allow terminal success.

### HTTP and authentication

Endpoints and shard request seeds belong to host-approved revisioned bindings, not authored URLs. V2 definitions configure record/field JSON Pointers and pagination selectors: token parameter or same-origin JSON next link. Resolve relative next links against the current page request, then validate against the pinned endpoint policy before attaching credentials. No automatic redirects in this slice. Disallow URI user-info/fragments and unapproved scheme/origin. Host explicitly opts into HTTP for controlled environments, including anonymous bindings. Next links must match a canonical path in a finite host-configured exact allowlist, defaulting to the seed paths, as well as the pinned origin. No implicit path-prefix expansion; query continuation can vary within the bounded request policy.

For authenticated bindings reuse `IConnectionResolver` and `IHttpRequestAuthenticator`. Resolve in the retained tenant scope; compare connection type/auth scheme and a canonical hash of non-secret settings with the admission pin. Never use a live BaseUrl to retarget a pinned destination. Re-resolve credentials per attempt; secret rotation is permitted, while revocation/configuration mismatch is diagnosed. Publication uses the existing `etl/definitions:publish` permission in the caller's scope, and validates the host-approved alias. Current Connections intentionally has no separate use permission; workflow binding likewise relies on workflow-design authority. Do not add an invented `connections:use` permission. Authenticated bindings require a non-null retained tenant at publication/admission, without fallback to another tenant. Anonymous bindings require no connection and still enforce ETL endpoint policy; the additional connection host policy applies only when a connection exists. Use request-local authentication and transport with automatic redirects/cookies disabled; do not reuse the activity-only applicator or cache scoped credentials in pooled handlers. Observe current connection policy again before sending. The check is point-of-use, not atomic with a remote send: a revocation/configuration update racing already-started I/O cannot recall it; subsequent attempts recheck.

Keep ETL-owned diagnostics and spans value-free: stable codes/counts/opaque identity, no raw exception messages/inner exceptions, bodies, headers or URLs. The existing `IRuntimeSecretMask` is an activity fault-boundary contract; importing its implementation would couple ETL to workflow execution. Clarify the broad masking rule in `Essentials/src/Connections/Core/EXTENSION_POINTS.md`: workflow adapters register tokens with that mask; non-workflow consumers omit or redact values in their own diagnostics boundary. Disable URI-bearing automatic request logging for the ETL-owned client. Prove ETL logs/errors and observe activity output with recording fixtures using query API keys, bearer/basic material and opaque continuation. The current connector emits no custom ETL spans; explicitly assert that absence alongside positively observed platform request spans. Any future ETL instrumentation must add positive value-free span assertions, not rely on an empty enumeration.

Built-in `System.Net.Http` activities are host-controlled and include origin/path even with .NET's default query redaction. Do not claim the connector can scrub every listener/exporter or arbitrary host instrumentation. Distinguish ETL spans from platform spans in tests, document the default query-redaction boundary, and document required host filtering/exclusion when endpoints or path-based tokens are sensitive. Hosts must not opt out of runtime URI redaction for credential-bearing traffic. No global telemetry configuration is changed by this slice.

POST JSON arrays to main/error routes. Retain body/hash/key before first send; use configurable host-approved idempotency header, default `Idempotency-Key`. Reject collisions with authentication/reserved transport headers. Accepted acknowledgement statuses: 200/201/204. Discard response content only through bounded reading/disposal; 202 is explicitly unsupported. Retry 408/429/500/502/503/504 and transport uncertainty within persisted attempt/deadline bounds; other statuses are terminal diagnostics. Never treat a timeout as proof the receiver did no work.

Use a trusted clock and deterministic capped backoff; persist attempt allocation before dispatch and not-before on retry. Respect Retry-After date/delta (RFC 9110 10.2.3). If the response asks for a delay beyond the remaining deadline, settle exhaustion instead of retrying early. Default five attempts/fifteen-minute deadline and a thirty-second request timeout (clamped to remaining deadline), configurable within host ceilings; tests use controlled time. Delayed work retains reservations/identities but yields physical execution capacity. Claims select eligible work rather than busy polling under a lease.

### Native/API/Studio

Reuse RunEtl's existing bookmark/admission/terminal boundary. Extend immutable pin validation for v2; no generic edges/joins enter native composites. Safe API views distinguish file ordinals from HTTP page/item progress and show retry time/attempt and accepted route-body counts. The optional read-only inspector supplies consistent snapshots; the SQLite provider aggregates indexed settled route-kind metadata, backfilled additively without rewriting retained JSON. An acknowledgement/publication remains owed until batch acceptance. Do not expose raw cursor, endpoint, request/response body, auth configuration or credential. Studio edits connector-specific forms, not serialized internals; existing file forms and run inspection remain available.

## Project structure

- `Extensions/Etl/src/Core`: additive versioned connector artifact and typed persistence/inspection contracts.
- `src/Design/Elsa.Etl.Design` and `src/Design/Api`: compiler, validation, scoped binding/ETL publication permission checks.
- `src/Runtime/Elsa.Etl.Runtime/{Http,Execution,Transformation,Output,Bindings,Hosting}`: HTTP behavior, narrow shared coordination, typed source-to-transform bridge, retained delivery.
- `src/Persistence/EntityFrameworkCore`: explicit HTTP state and migrations; common store invariant helpers.
- `src/Workflows/{Design,Runtime}`, `src/Runtime/Api`: actual publication/runtime integration.
- Existing ETL test project/WorkerHost/shared fixtures, `e2e-tests/etl`, ETL Studio module and live browser suite.

## Validation funnel and implementation order

1. Lock pre-change v1 producer/consumer and in-flight runtime upgrade fixtures; add v2 model/canonical reader/compiler/validation.
2. Add bounded JSON/pagination/request policy tests and live local transport fixture.
3. Add durable HTTP partition/page/effect/retry storage, shared invariant reuse and migration tests.
4. Compose worker page acquisition, transformations, retained delivery and retry eligibility; prove mixed connectors and two-process interruption/recovery before UI claims.
5. Extend real native publication/continuation, scoped API and Studio; run rebuilt Workbench REST/browser journeys.
6. Self-review exact diff across correctness/standards/acceptance, repair verified findings with negative controls, full affected suites, architecture/maps/filters, exact-head CI and post-merge verification.

## Risks and limits

Remote pagination may change before a page is captured; capture stability is not collection consistency. Remote effects remain at least once, and cancellation cannot recall them. HTTP position/effect shapes must not force future continuous/SQL/RabbitMQ semantics into file ranges; only the proven common coordination is shared. External source/destination retention and receiver deduplication windows are host contracts. Bounded finite page counts do not implement continuous processing. Task-runner/pause semantics remain follow-up scope.

## Complexity tracking

No kernel/provider redesign, generic plugin registry, new actor layer, workflow-file change or separate credential subsystem. Extract shared helpers only where file and HTTP execution demonstrably need the same invariant; preserve connector-specific semantics explicitly.
