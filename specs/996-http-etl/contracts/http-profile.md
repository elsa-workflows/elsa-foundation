# Supported HTTP profile

## Host and author boundary

Hosts configure scoped revisioned aliases, endpoint origins/paths, bounded shard seed requests and optional connection references. Authors select aliases and configure JSON selection/pagination/transforms through ETL authoring. Published pipelines contain no raw endpoint/credential. A host update cannot retarget an already-admitted run; removing its retained revision yields an explicit unavailable-binding outcome.

Credentials are resolved per attempt. Pin the selected connection type/auth scheme and canonical non-secret settings digest at admission. Rotation of a secret value is permitted; revocation, missing connection and changed non-secret settings prevent dispatch. Publication checks the caller's existing `etl/definitions:publish` authority plus the scoped host-approved alias. Connections has no separate use permission; connection creation/test/secret-binding rights are unchanged. Authenticated bindings require a non-null retained tenant, checked at publication/admission; no fallback tenant is permitted. Anonymous aliases remain available in null-tenant scope when ETL endpoint policy permits. The worker uses the retained tenant rather than ambient HTTP request context.

The authoring API preserves the file-only v1 `/etl/...` surface and adds `/etl/v2/...` for connector pipelines.
The versions share definition identities and immutable history, with format-specific listings and reads.
A filtered page can be empty while its signed next cursor advances over records of the other format.

Host HTTP bindings use `Source`, `MainOutput` or `ErrorOutput` roles; CSV lookup remains a file binding.
Current alias/role collisions across HTTP and file kinds are invalid host configuration. For a source, `Endpoint`
must name an approved initial path, and empty `AllowedPaths` defaults to the shard seed paths. An explicit list is
not widened; every seed and the endpoint must satisfy it. Output defaults to its endpoint path. Endpoint policy
compares canonical scheme, host and port and exact canonical paths, rejects user information and fragments, and
refuses encoded path separators, controls and nested percent escapes. A literal percent without a nested escape
remains valid. Credentials are not read during binding validation. The Connections public contract exposes
non-secret settings but not credential-source reference identity; changing the latter is treated as credential
rotation, while changing any public setting invalidates the retained handle. A fresh connection check precedes
each physical attempt; it cannot recall an already-started request if revocation occurs afterward.

## Pagination

A page is a JSON document. A JSON Pointer identifies its record array, and per-column pointers identify scalar fields relative to a record. Strict pointer escapes and bounded indices are validated. Duplicate properties are refused rather than interpreted differently by parsers. Types preserve null/missing distinctions, reject non-finite or out-of-range numbers, and do not coerce arbitrary objects into strings.

Record items may be objects, arrays or scalars. An empty field pointer selects the item itself; array-index pointers select within array items. Requiredness and declared scalar types determine whether a selected value is valid, rather than an implicit object-only restriction.

Decoded JSON decimal fields require an exactly representable decimal value; conversion must not silently round or underflow. Equivalent exponent and trailing-zero spellings are valid. Invalid UTF-8 or escaped Unicode anywhere in the page, including unselected metadata, produces a value-free page diagnostic. The declared-field limit bounds selectors; raw page bytes and JSON depth also bound traversal of unselected members. Decoder item indices are meaningful only within the retained shard/request/page identity, never as standalone durable cursors.

Continuation is either a scalar opaque token inserted into one named query parameter of the pinned seed, or a URI reference selected from JSON. Missing/null/empty continuation ends the shard; empty record array alone does not. Next links are resolved then checked against the pinned origin and finite exact canonical-path allowlist before authentication. By default only seed paths are approved; hosts may explicitly add paths. A same-origin link to any other path is refused. Raw continuation text is retained only in protected runtime data, not keys/logs/public API. Page count/visited request digests bound loops.

## Source acquisition and retained-page recovery

Only a complete bounded HTTP 200 JSON representation is a source page. Other 2xx responses, including
201, 202, 204 and 206, do not prove a complete page and are refused. Source retry uses the same selected
transient statuses and bounded timing mechanics as delivery, but source capture and output acknowledgement
remain different facts. Malformed or over-limit JSON is a deterministic page failure, not empty-source success.

Allocate the source attempt durably before GET. Its identity, current fence, expiry and fixed request deadline
are separate from the immutable request identity. A successful response is not authoritative input until its
raw body is retained and verified and the current owner attaches its page descriptor. Keep the complete-response
receipt time and original attempt provenance so a replacement can validate a previously published page without
inventing a new response or attributing it to its own fence.

The file cache publishes a content-addressed raw JSON body first, then a request-addressed immutable manifest.
The manifest is the single publication point; a raw body without that manifest is not a recoverable page.
Recovery checks the fixed-size manifest checksum and revalidates the referenced body's bytes and decoded count/continuation before
allocating another GET. A missing manifest is distinct from an unavailable cache or a manifest whose body is
missing/corrupt. Those failures never silently become permission to fetch again. Identical content may reuse
the first manifest and its provenance; different content cannot overwrite it. This does not guarantee that
remote pages observed at different times form one consistent remote collection.

The manifest belongs to protected runtime storage. Its key binds the admitted invocation, source revision,
partition, page sequence and request hash; it contains no endpoint, headers or credential. Body references retain
the actual raw JSON hash/length rather than the manifest's byte identity. Body/manifest publication and SQL
attachment are separate durability boundaries. Unreferenced bodies after interruption require runtime cleanup;
logical retained-input limits do not promise to bound filesystem overhead or all crash leftovers.

## Delivery and retry

Each nonempty route sends a bounded JSON array to its pinned destination via POST. Request identity and body are retained before sending. Default key header is `Idempotency-Key`, host-configurable to an approved non-conflicting name. The key is stable across worker replacement and binds the retained route/body; it is not an exactly-once guarantee. The receiver must explicitly offer deduplication for stronger external-effect behavior.

200/201/204 acknowledge this synchronous profile. 202 is unsupported; other statuses are explicit failure except 408/429/500/502/503/504, which are retryable within the retained budget. Transport uncertainty is retryable because the same request may already have taken effect. Persist each attempt and deadline before dispatch. Retry-After delays/date values may extend the normal capped backoff; exceeding the remaining deadline ends retry rather than causing an early request. Retry and cancellation never silently discard an owed reservation.

Automatic redirects and cookies are disabled. Default HTTPS; explicit host opt-in permits controlled HTTP. Authentication is request-local and ETL endpoint policy always applies, with connection host policy additionally checked for authenticated requests. Safe diagnostics identify failure kind/route/position, not URLs, token values, bodies or secrets.

## Native and Studio

RunEtl's result boundary remains unchanged. HTTP progress is page/item/exhaustion/retry metadata, not a misleading file percentage; file runs keep ordinal views. Publication pins the exact v1/v2 artifact and existing scope/permission rules apply to authoring/inspection/cancellation.

## Diagnostic and timeout boundary

Each request has a finite per-attempt timeout bounded by its retained retry deadline. Timeout/late completion preserves uncertain-effect accounting; physical allowances remain held until the actual attempt unwinds. Revocation/configuration checks happen before each attempt and do not recall already-started I/O.

ETL-owned logs, errors and spans expose stable codes and opaque identity, never raw exceptions, URI, header, body or token. The current connector emits no custom ETL spans; tests assert that absence, and future ETL spans require positive assertions of the same value-free contract. Disable URI-bearing request logging for its client. Recording tests distinguish these surfaces from built-in `System.Net.Http` activities: the latter retain origin/path under default .NET query redaction and are host-controlled. Hosts with sensitive endpoint paths must filter/drop those activities before export and must preserve query redaction; the connector does not claim to sanitize arbitrary host listeners. The workflow `IRuntimeSecretMask` remains the native activity fault boundary, not the background transport mechanism.


## Runtime inspection contract

The optional `IEtlConnectorRuntimeInspector` reads retained run state independently of design services and
workers. `etl-run-connector-partitions` advertises `/etl/runs/{id}/connector-partitions`; existing file-only
partition views retain their shape. Run detail `formatVersion` distinguishes a v2 admission rejection from
a file run even when no connector pins were admitted. Inspector absence is explicit, never a fabricated
empty HTTP progress view. Cursor scope includes endpoint kind, run, tenant/partition and page size.

HTTP progress uses zero-based page/item coordinates and an explicit exhaustion flag. Fetch/delivery summaries
include phase, attempt count, deadline, next eligible time and last recorded status. No raw URI, credentials,
continuation, request identity, body, effect identity or binding handle is included. Recorded claim expiry does
not prove physical execution stopped. Owed HTTP acknowledgements and JSONL publications are distinct from
accepted progress; accepted effect counts count settled route bodies, not attempts or physical executions.
See the [inspection checkpoint](../evidence/connector-inspection-checkpoint.md) for provider and validation limits.
