# Wave 9 Runtime API migration

Issue: #1375
Owner: `Elsa.Workflows.Runtime.Api`
Scope: 24 first-party Runtime registrations

Implementation spec: [specs/164-runtime-api-minimal-migration](../../specs/164-runtime-api-minimal-migration/spec.md)

## Baseline provenance

The FastEndpoints oracle was captured before production endpoint deletion. The detached runner is committed in the branch-reachable baseline-only history ending at `6fe00ac47` (built on the baseline runner history `334b47d48`), and the frozen HTTP/receipt update is reproduced against migration parent `67ba4b3b9bec3a6c2aac0d6d332099baf723e802`. The receipt records runner commit `5f5e25cd7ebb2819d1c426b2a43326bc402d2207`, 24 registrations, 77 HTTP cases, and 24 OpenAPI operations.

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

The former Runtime unit tests that instantiated deleted FastEndpoints endpoint classes were removed as stale endpoint-only duplicates. Their retained handler coverage remains in the Runtime suite, while the immutable 77-case FE oracle, Minimal API deep comparison, composition/authentication matrix, and targeted Runtime tests own the HTTP contract and security proof. No production endpoint or hidden operation was restored to keep those tests compiling.

The golden-rule inventory for the removed endpoint tests is now directly exercised by `RuntimeMinimalApiBehaviorTests` against the real mapper: execute Accepted/AcceptedButFaulted/Duplicate/Deferred/Rejected statuses; executable-not-found, blank/unknown source-reference, argument, unexpected, and cancellation outcomes; malformed-body sender non-invocation; all four activity inspection not-found/invalid/unexpected paths; exact typed activity ProblemDetails fields and titles; cursor invalid/binding-mismatch/expired states; value-payload denied/unavailable/resolved/cancellation behavior; and automatic-layout success. The list-instance cursor/run-kind validations remain direct handler tests in `WorkflowInstancesRequestHandlerTests`. The route/verb inventory and all-route HTTP replay remain in the composition/oracle suites.

Serialization uses `WorkflowsRuntimeJsonContext`. Route values override conflicting JSON IDs for execution and dispatch redrive. Alteration submission preserves 202/Location, cancellation preserves the old plan body and 202/200 active/terminal disposition, and safe RFC 7807 responses are emitted for Runtime failures.

The documented Runtime request/response contracts now compile into the stable `Elsa.Workflows.Runtime.Api.Core`
assembly. The legacy API assembly forwards the moved public types, including the payload and problem views; the
Runtime.Core move of `WorkflowOutputProjection` preserves the existing `WorkflowOutputView.From` static member and
the Runtime assembly forwards that projection as well. All 24 endpoint descriptions use the stable contract types.
`RequireStableOpenApi` is applied last, and the endpoint scan rejects owner-assembly request/response `Type`,
`MemberInfo`, and handler metadata. The owner JSON resolver is inserted into the effective ASP.NET Core resolver
chain, with source-generated metadata covering the mapped accepts and produces types.

## Evidence currently green

- Runtime owner suite: 93 passed; direct Minimal API behavior regression coverage: 21 passed; complete Runtime API test project: 114 passed.
- Runtime implementation suite: 1,640 passed after removing only endpoint-class tests that no longer have a production owner.
- Runtime composition: 24 published routes, 24 OpenAPI operations, and anonymous 401 for every mapped route.
- Baseline receipt/hash assertion and HTTP status mutation bite pass.
- Deep OpenAPI comparison consumes 18 exact route approvals. Each approval is limited to named, two-sided `mediaTypes`, `requestBody`, `schemas`, or response-schema facets; the validator verifies the recorded before/after values against both documents and rejects duplicate, no-op, one-sided, unknown, stale, or unrecognized approvals. All unapproved operation structure is compared deeply, with mutation bites covering the validator and common response structure.
- Runtime authorization: 16 shared matrix cases pass for Minimal API and retained FastEndpoints, including 401/403, exact/implied/wildcard, normalization, tenant, external identity, and resource dispositions; the catalog actions for execute/manage/publishing-read are asserted.
- Runtime collectibility: one combined alternating three-cycle test executes mapped delegates, generated request/response serialization, native OpenAPI on cycles 0/2, Foundation Identity evaluator, provider seams, DI setup/disposal, and weak-reference collection.
- Runtime API/Core compatibility: the stable Core assembly and legacy type forwarders are asserted, including the preserved `WorkflowOutputView.From` static member; the 24-route owner metadata scan passes.
- Rebuilt Workbench/fresh SQLite E2E: Runtime GET coverage passes 20/20 cases and Runtime write coverage passes 10/10 cases.
- Solution build: `Elsa.Server.slnx` builds with 0 errors; the generated-map check reports that committed maps still describe the tree. Scoped formatter verification passes for the changed Runtime/API/Core source files.

## Remaining gates

The deep OpenAPI comparison consumes a reviewed, exact 18-entry approval registry for GET-only metadata differences (Minimal API parameter/request-body metadata and generated schema ownership); POST/PUT operations remain byte-equivalent in the consumed projection. Full Architecture (498), Runtime API (114), map refresh, Workbench build, solution build, scoped formatter, and rebuilt Runtime E2E gates are green. The repository-wide formatter invocation still reports pre-existing unrelated diagnostics, so it remains an integration follow-up rather than being hidden behind a broad unrelated formatting change. Final diff review and the migration recommendation/follow-up review remain visible until the control-room integration commit.
