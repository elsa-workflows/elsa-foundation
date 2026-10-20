# First finite ETL contracts

Version 1 is feature-owned and pre-GA, not a neutral execution-model API. Reject unsupported persisted formats before admitting work; do not reinterpret saved cursors with defaults.

## Artifact and bindings

The supported topology is ordered: CSV parsing/schema validation → exact-key lookup → mapping → equality/inequality filtering → accepted/error JSONL routing. Studio exposes those stages. Arbitrary code, paths and general graph nodes are not accepted.

An artifact contains its format version, behavioral ID/hash, definition/version provenance, schema, operations, ordered output fields, binding aliases, finite partition/batch/admission limits and diagnostic policy. Layout/timestamps do not affect behavioral identity. Native publication embeds the bounded artifact/hash in ETL-owned executable metadata, not a workflow-typed dependency row. Runtime never resolves a mutable draft. The ETL runtime retains its own artifact copy with the admitted run.

A host supplies role-specific named file bindings with tenant scope. Input revisions identify immutable content with an expected digest. Authors select aliases; they cannot set paths, storage roots, digests, credentials or tenant authority. Listings return safe names/roles, not paths. Admission resolves input revisions once; preparation verifies and retains their snapshots before partition dispatch. Editing an original binding does not replace accepted snapshots. Workers require the declared shared storage topology.

Output is run-owned. Admission pins destination revisions as well as input revisions; retargeting an alias cannot redirect an existing run's replay. Chunk identity includes run/partition/fixed batch range, independent of attempt/lease. Write to attempt-private staging, flush/close/verify, then atomically publish a complete chunk without overwriting. Interrupted staging never occupies the final key; unsupported publication semantics refuse the binding. Accepted/rejected records have separate chunks. The authoritative manifest lists only accepted identities/hashes in partition/range order. Deterministic JSONL materialization uses that manifest, never concurrent append. Partial/cancelled output remains visibly partial.

Runtime composition supplies an internal fixed-format file publisher factory for the existing CSV snapshots, source indexes and JSONL outputs. Preparation, batch processing and terminal materialization use that same publication contract: trusted destination root, immutable effect identity, finite byte allowance and an awaited writer whose destination remains owned by the publisher. The default is the existing staged/hash-verified/create-only filesystem implementation. This internal seam preserves identities and acceptance boundaries and allows the actual worker's read/write work to be decorated for deterministic interruption evidence; it introduces no authored format selection or public storage-provider contract.

## Terminal JSONL materialization

The runtime-owned materializer accepts trusted scope and run identity, not a caller path. It requires a retained terminal result so that the accepted manifest cannot grow during pagination. It resolves main/error destinations from that run's exact pinned binding revisions; removing a revision refuses output instead of selecting today's alias. A terminal run without admitted bindings has no output references.

Read bounded manifest pages in partition/range/route order. Stream only accepted chunks, verifying their exact key, length and digest as they are copied. Never enumerate the destination directory or include acceptance timestamps in canonical content. Use fixed copy buffers and checked length/count accounting; this is not a new total-output ceiling or a heap/RSS promise. Refuse a missing, altered or inconsistent accepted chunk with a stable diagnostic.

Publish each nonempty route through the existing attempt-private staging and atomic create-only mechanism, using a versioned identity containing the scoped run, terminal result and route. Matching replay returns the same immutable reference; conflicting content is refused. Empty routes have no file reference. Return outcome, counts and safe route object references; cancelled/faulted runs are explicitly partial. Do not modify the run, manifest, terminal result or native delivery responsibility. Each route is independently atomic; interruption can leave a complete unreferenced candidate, and replay must recover it. No atomic multi-file publication is implied.

This initial operation and descriptor remain internal to the runtime until a concrete API/Studio consumer establishes the public response contract. No new persisted record or schema migration is required.

## Native invocation

`RunEtl` selects a published definition; workflow publication pins an exact artifact/hash/schema. Immutable private state records stable run identity, pin, supported state version and trusted parent/scope address. Validate the actual bookmark registration instead of guessing its ID from user input.

The admission intent is added to the actual bookmark-created checkpoint. Identical replay converges; conflicting parent/pin/scope refuses. Input/binding validation refusal must still settle a durable business-error or fault result and terminal outbox, not acknowledge the start and orphan its wait. Storage unavailability leaves the start intent owed. Cancellation has its own stable identity and may create a tombstone before admission. The ETL store and native store use separate transactions; their outboxes bridge them.

Completion carries run/pin, expected parent/activity/bookmark, terminal status, safe output references/counts, diagnostic code and result version. Its typed delivery uses the frozen payload alias/schema and stable version-1 ETL completion provider ID, the explicit bookmark ID and an unchanged idempotency/delivery identity. Resume validates the complete address and durable accepted result. Success/business error are authored outcomes; execution fault and cancellation use normal native transitions. Existing composites decide continuation. A Sequence must reach its next sibling and a Flowchart must complete normally; root-only execution is insufficient.

The terminal outbox retains delivery until bookmark consumption or demonstrated parent/activity termination. Queueing a resume alone is insufficient acknowledgement. Repeated delivery cannot produce another accepted continuation.

## API and Studio

Use existing endpoint/capability/permission conventions. All lookups are scope-filtered; cross-tenant IDs disclose neither existence nor binding details. Logical routes under the host API prefix:

| Route | Purpose | Required permission role |
|---|---|---|
| `GET /etl/bindings` | Safe allowed aliases/roles | Author/read |
| `GET /etl/definitions` | Paginated summaries | Definition/read |
| `POST /etl/definitions` | Create definition/draft | Definition/write |
| `GET /etl/definitions/{id}` | Draft/published inspection | Definition/read |
| `PUT /etl/definitions/{id}/draft` | Desired state plus concurrency revision | Definition/write |
| `POST /etl/definitions/{id}/validate` | Structured diagnostics | Definition/write |
| `POST /etl/definitions/{id}/publish` | Immutable version/artifact | Definition/publish |
| `GET /etl/definitions/{id}/versions` | Paginated immutable version metadata and selection tokens | Definition/read |
| `GET /etl/definitions/{id}/versions/{version}` | Inspect one exact published pipeline | Definition/read |
| `GET /etl/runs` | Paginated summaries | Run/read |
| `GET /etl/runs/{id}` | Pin, outcome, counts, diagnostics | Run/read |
| `GET /etl/runs/{id}/partitions` | Paginated cursor/owed-batch/claim inspection | Run/read |
| `POST /etl/runs/{id}/cancel` | Idempotent durable cancellation | Run/cancel |

Implement identifiers with repository permission conventions. Authoring routes require the design feature; runtime inspection/cancellation requires no design database. No arbitrary worker/result posting API belongs to this slice.

The authoring feature declares `etl/definitions:read`, `etl/definitions:manage`, `etl/definitions:publish` and `etl/bindings:read`. Permission evaluation precedes dispatch. Tenant identity comes from the validated normalized principal and the persistence partition comes from ordinary host-selected persistence access. Neither is accepted from the authored body. Missing providers return unavailable, distinct from an empty catalog. Binding metadata contains only alias and role.

Create accepts an explicit definition ID and typed pipeline. Updates require the current draft revision. Validation requires that revision and returns bounded diagnostics plus the proposed positive publication version and full lowercase content hash when valid. Publication requires the explicit `(expectedRevision, definitionVersion, contentHash)` tuple; it never accepts a client-built artifact. Retrying an already published matching version returns `replay`, even after later draft edits or publications. A mismatched hash, stale unpublished revision, or conflicting next version returns conflict. A successful new publication returns `published`.

Definition, binding and version catalogs accept `limit` from 1 through 200 (default 50) and return `items` and an optional `nextCursor`. Cursors are signed, bounded and bound to their exact scope, resource, catalog and page size. Definition pages order by the stable ordinal definition key, binding pages by ordinal alias/role, and version pages by ascending version under the first page's frozen upper version. New publications do not enter an existing version traversal. Host replicas require the same configured cursor signing key for continuation across replicas; the default process-local key invalidates old cursors on restart. The complete authoring request body is capped at the artifact allowance plus 4096 bytes of envelope overhead before JSON binding, including streaming requests. Pipeline compilation retains its separate artifact-size and topology limits.

Studio provides a structured stage editor, binding/schema validation, publication, run list/detail and partition progress. Loading, empty, unavailable feature, forbidden, error and recovery states remain distinct. Diagnostics expose stable codes and safe record ordinals/field names, never raw row values or host paths. Logical reservations and expired claims are not presented as physical-memory reclamation or worker termination.
