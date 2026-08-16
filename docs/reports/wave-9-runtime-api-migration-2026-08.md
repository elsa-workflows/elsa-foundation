# Wave 9 Runtime API migration

Issue: #1375
Owner: `Elsa.Workflows.Runtime.Api`
Scope: 24 first-party Runtime registrations

Implementation spec: [specs/164-runtime-api-minimal-migration](../../specs/164-runtime-api-minimal-migration/spec.md)

## Baseline provenance

The FastEndpoints oracle was captured before production endpoint deletion. The detached runner is committed in the baseline-only history ending at `30323b6ec` (built on the baseline runner history `bafc4f2ab`/`db4231ac1`), and the frozen HTTP/receipt update is reproduced against migration parent `67ba4b3b9bec3a6c2aac0d6d332099baf723e802`. The receipt records runner commit `b1d92cf5b5bea7a1a3599b15dade47c0f40d2139`, 24 registrations, 77 HTTP cases, and 24 OpenAPI operations.

The current frozen HTTP SHA-256 is `25b6895f014aa6fbfeae60b80588aa33abdd1a79ee0b739b4aa030bd62028a6e`; the OpenAPI SHA-256 is `990c5c4cbde8297b2e4cf4a3e3b8a30cb1e7215f0081d5df4e7d4b123a949eb4`. The historical projection asserts that every captured ProblemDetails `traceId` is a non-empty JSON string, then replaces it with the deterministic value `capture-trace-id`; two independent captures from source `67ba4b3b9bec3a6c2aac0d6d332099baf723e802` produced byte-identical HTTP, OpenAPI, and receipt files and these hashes.

The 77 HTTP cases are observed from the FE host, not authored from the Minimal API implementation:

| Evidence | Count |
|---|---:|
| Anonymous challenges | 24 |
| Authenticated success cases, one per route | 24 |
| Malformed, literal-null, empty, and absent-content-type body cases | 20 |
| Not-found cases | 5 |
| Route/body precedence cases | 2 |
| Invalid `take` and missing idempotency-header cases | 2 |

The capture records actual route values and deserialized request values through a capture-only response diagnostic that is removed from the frozen response headers. It exercises query filters, paging, diagnostics, executables, dispatches, activity inspection, value evidence, incidents, and alteration routes. In particular, FE observed 415 for body requests with absent/non-JSON content type and 400 ProblemDetails for empty JSON; the Minimal reader preserves those dispositions.

## Production implementation

Runtime now exposes one owner-local Minimal API mapper with all 24 method/path registrations, stable operation names, owner/tag/authoring metadata, typed request/response metadata, and one catalog-owned permission action per endpoint. Wildcard remains evaluator-level behavior. `WorkflowsRuntimeApiFeature` is public, non-sealed, and has a virtual `ConfigureServices` seam. The production project no longer references `Elsa.Api.FastEndpoints`; the test project retains it only for the historical/coexistence oracle.

Serialization uses `WorkflowsRuntimeJsonContext`. Route values override conflicting JSON IDs for execution and dispatch redrive. Alteration submission preserves 202/Location, cancellation preserves the old plan body and 202/200 active/terminal disposition, and safe RFC 7807 responses are emitted for Runtime failures.

## Evidence currently green

- Runtime owner suite: 93 passed.
- Runtime composition: 24 published routes, 24 OpenAPI operations, and anonymous 401 for every mapped route.
- Baseline receipt/hash assertion and HTTP status mutation bite pass.
- Deep OpenAPI comparison consumes 18 exact route approvals. Each approval is limited to named, two-sided `mediaTypes`, `requestBody`, `schemas`, or response-schema facets; the validator verifies the recorded before/after values against both documents and rejects duplicate, no-op, one-sided, unknown, stale, or unrecognized approvals. All unapproved operation structure is compared deeply, with mutation bites covering the validator and common response structure.
- Runtime collectibility: three cycles execute a mapped delegate, generated request/response serialization, Foundation Identity evaluator, DI setup/disposal, and weak-reference collection.

## Remaining gates

The deep OpenAPI comparison consumes a reviewed, exact 18-entry approval registry for GET-only metadata differences (Minimal API parameter/request-body metadata and generated schema ownership); POST/PUT operations must remain byte-equivalent in the consumed projection. A shared authorization matrix (including retained FE canary and exact/implied/wildcard/normalized/resource cases), rebuilt Workbench/fresh-DB Runtime E2E, and final solution-wide gates remain root integration gates. These are deliberately left visible rather than represented as passed.
