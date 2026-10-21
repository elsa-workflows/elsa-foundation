# Supported HTTP profile

## Host and author boundary

Hosts configure scoped revisioned aliases, endpoint origins/paths, bounded shard seed requests and optional connection references. Authors select aliases and configure JSON selection/pagination/transforms through ETL authoring. Published pipelines contain no raw endpoint/credential. A host update cannot retarget an already-admitted run; removing its retained revision yields an explicit unavailable-binding outcome.

Credentials are resolved per attempt. Pin the selected connection type/auth scheme and canonical non-secret settings digest at admission. Rotation of a secret value is permitted; revocation, missing connection and changed non-secret settings prevent dispatch. Publication checks the caller's existing `etl/definitions:publish` authority plus the scoped host-approved alias. Connections has no separate use permission; connection creation/test/secret-binding rights are unchanged. Authenticated bindings require a non-null retained tenant, checked at publication/admission; no fallback tenant is permitted. Anonymous aliases remain available in null-tenant scope when ETL endpoint policy permits. The worker uses the retained tenant rather than ambient HTTP request context.

## Pagination

A page is a JSON document. A JSON Pointer identifies its record array, and per-column pointers identify scalar fields relative to a record. Strict pointer escapes and bounded indices are validated. Duplicate properties are refused rather than interpreted differently by parsers. Types preserve null/missing distinctions, reject non-finite or out-of-range numbers, and do not coerce arbitrary objects into strings.

Continuation is either a scalar opaque token inserted into one named query parameter of the pinned seed, or a URI reference selected from JSON. Missing/null/empty continuation ends the shard; empty record array alone does not. Next links are resolved then checked against the pinned origin and finite exact canonical-path allowlist before authentication. By default only seed paths are approved; hosts may explicitly add paths. A same-origin link to any other path is refused. Raw continuation text is retained only in protected runtime data, not keys/logs/public API. Page count/visited request digests bound loops.

## Delivery and retry

Each nonempty route sends a bounded JSON array to its pinned destination via POST. Request identity and body are retained before sending. Default key header is `Idempotency-Key`, host-configurable to an approved non-conflicting name. The key is stable across worker replacement and binds the retained route/body; it is not an exactly-once guarantee. The receiver must explicitly offer deduplication for stronger external-effect behavior.

200/201/204 acknowledge this synchronous profile. 202 is unsupported; other statuses are explicit failure except 408/429/500/502/503/504, which are retryable within the retained budget. Transport uncertainty is retryable because the same request may already have taken effect. Persist each attempt and deadline before dispatch. Retry-After delays/date values may extend the normal capped backoff; exceeding the remaining deadline ends retry rather than causing an early request. Retry and cancellation never silently discard an owed reservation.

Automatic redirects and cookies are disabled. Default HTTPS; explicit host opt-in permits controlled HTTP. Authentication is request-local and ETL endpoint policy always applies, with connection host policy additionally checked for authenticated requests. Safe diagnostics identify failure kind/route/position, not URLs, token values, bodies or secrets.

## Native and Studio

RunEtl's result boundary remains unchanged. HTTP progress is page/item/exhaustion/retry metadata, not a misleading file percentage; file runs keep ordinal views. Publication pins the exact v1/v2 artifact and existing scope/permission rules apply to authoring/inspection/cancellation.

## Diagnostic and timeout boundary

Each request has a finite per-attempt timeout bounded by its retained retry deadline. Timeout/late completion preserves uncertain-effect accounting; physical allowances remain held until the actual attempt unwinds. Revocation/configuration checks happen before each attempt and do not recall already-started I/O.

ETL-owned logs, errors and spans expose stable codes and opaque identity, never raw exceptions, URI, header, body or token. Disable URI-bearing request logging for its client. Recording tests distinguish these surfaces from built-in `System.Net.Http` activities: the latter retain origin/path under default .NET query redaction and are host-controlled. Hosts with sensitive endpoint paths must filter/drop those activities before export and must preserve query redaction; the connector does not claim to sanitize arbitrary host listeners. The workflow `IRuntimeSecretMask` remains the native activity fault boundary, not the background transport mechanism.
