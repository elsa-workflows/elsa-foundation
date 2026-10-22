# T011: scoped finite ETL authoring API

Date: 20 October 2026. Scope: #3109 / draft PR #3111. This checkpoint completes T011, not the finite ETL product or parent program.

## Implemented boundary

`Elsa.Etl.Design.Api` maps nine NativeEndpoints operations for safe bindings, definition listing/creation/read,
draft update, validation, publication, version listing and exact published-pipeline inspection. It contributes
its own capability, permissions, generated JSON metadata and sanitized failure shape. Shared schema/dormancy
refusals preserve the framework's actionable 409 metadata. See the [canonical contract](../contracts/journey.md).

The API depends on ETL Core/Design, API/identity infrastructure and the existing ordinary persistence-access
abstractions. It does not depend on the ETL worker implementation, native workflow engine or actor provider.
The optional Core binding catalog returns current aliases/roles only; absent providers return 503, distinct
from an empty available catalog. Runtime binding resolution, revisions and physical publication stay private.

The new Design API follows the repository's name-derived project layout. The existing Design implementation
moved under `Design/Elsa.Etl.Design` because `Design` is now a project container. Its assembly name, public
contracts and persisted formats did not change; references and the solution follow the move.

## Actual HTTP/storage evidence

The fixture uses ASP.NET Core TestServer, the production Foundation Identity permission pipeline, the real
ETL design feature/compiler, actual SQLite migrations/store/catalog and real host binding metadata. Test-only
authentication and persistence middleware supply trusted host context; request DTOs do not carry that authority.
The transport is in-process TestServer, not a separate Workbench/browser deployment. No allow-all permission
provider, fabricated artifact or fake definition store is used.

The executed cases prove:

- Create/read/update/validate/publish and exact-version inspection use the actual persisted pipeline and compiler.
- Publication accepts an explicit revision/version/full-hash tuple. A matching v1 retry replays v1 after v2 edits
  and publication; stale unpublished revisions and mismatched hashes conflict. Concurrent identical publication
  produces one immutable version and replay.
- A deterministic forwarding decorator pauses one actual SQLite immutable-read response after a miss. A second
  actual HTTP request publishes before the first reads the draft. The first rechecks the immutable version and
  replays instead of incorrectly returning a stale next-version conflict.
- Anonymous and unnormalized principals are challenged; permission denial, ambiguous normalized tenant claims,
  global and privileged-global persistence contexts cannot create/read scoped definitions. Cross-tenant and
  cross-partition reads do not disclose the other identity. Authored tenant/partition fields are rejected.
- Signed definition cursors reject tampering, malformed tokens, changed limits and other scopes. Published
  version traversal retains its first-page high-water mark when a newer publication arrives.
- Binding responses contain only scoped alias/role pairs, with missing versus empty providers distinguished.
- Malformed JSON and both known-length and streaming/chunked oversized bodies are refused before a write.
  The body cap is separate from the compiler's artifact/topology limits.

Catalog queries project metadata only and apply their ordering/limit in SQLite before materialization.
Selected schema/identity/hash-shape/timestamp corruption is refused. A row whose key is displaced outside an
exact-key query is not discovered by a global scan; metadata-only listing does not recompute content hashes
against artifact payloads. Exact artifact reads/publication retain their existing stronger envelope validation.

## Safeguard removal and restoration

[Regression receipt](authoring-publication-race-regression.json) records source hashes and the exact focused command.
Removing only the immutable-version recheck made the deterministic race test fail with expected `OK` versus
actual `Conflict`. This was an HTTP assertion failure, not a compile error or timeout oracle. Restoring the file
byte-for-byte restored the complete affected ETL suite: **338 passed, zero failed/skipped**.

## Validation and self-review

- ETL tests: **338 passed**, zero failures/skips, before mutation and again after exact restoration.
- Architecture guards: **581 passed**, zero failures/skips, including the new owner's schema/dormancy failure cases.
- The CI Release plus isolated Debug restore script passed in locked mode.
- Maps regenerated, reviewed and fresh; solution filters generated and checked. The reviewed findings show 75
  source catalogs, all indexed, zero missing catalogs and zero runtime-to-design direct-reference signals. The new
  Design API to Workflow Runtime Core signal is its ordinary persistence-scope abstraction, not a worker dependency.
- Rebuilt Workbench passed ordinary workflow publication/execution, Sequence continuation and nested-fault REST
  tests against one owned fresh content root. The nested fault's one incident is intentional. These are native
  regression smokes; they do not establish the pending ETL Workbench journey.
- Three root self-review passes ended clean after correcting wire-context initialization, strict fixture enum
  serialization, the replay race, schema-refusal preservation and repository project layout. A bounded worker
  review supplied input; root integrated and verified the result. No independent approval gate was introduced.
- Public API declarations, extension catalogs, project references and staged diff were reviewed. Only the dependency
  dataset and solution filters are committed map outputs; documentation maps/manifests/findings remain ignored.

## Remaining acceptance

T018 still needs its picker/catalog acceptance. Runtime inspection/cancellation APIs, Studio authoring/inspection,
T025's wider terminal/address/format matrix, migration/provider qualification and integrated ETL Workbench/browser
acceptance remain open. No API/Studio/full-product completion or resulting-main CI claim is made here. PR #3111
remains draft, and fresh exact-head CI must be reported separately after push. External effects remain at least
once; the historical receipt/HTTP timeout causes remain unproved. The separately recorded future task-runner
intent does not introduce queue/workflow pause support in this checkpoint.
