# Research: Wave 6 Review Corrections

## Decision: Use a real two-host before/after capture

The existing fixture only proved anonymous responses and did not establish the successful service
paths. The correction uses an actual FastEndpoints-era host built from the pre-migration endpoint
assembly and a Minimal API host built from the current mapper. Both consume one case corpus and one
OpenAPI document, so differences are attributable to authoring rather than hand-authored expected
values.

**Alternatives considered**: retain the anonymous-only fixture (rejected because it cannot detect
binding, service, error, query, or concurrency drift); compare only generated OpenAPI (rejected because
generated but unconsumed fields can be misleading).

## Decision: Exact headers with explicit two-sided approvals

Headers are compared as captured, including content length and content type. If the two host servers
necessarily differ in a header, the approval must identify endpoint, method, case, facet, before,
after, owner, reason, and follow-up; the comparer must reject an approval that is not consumed in both
directions, and a mutated fixture must make the test fail.

**Alternatives considered**: strip date/server/content-length globally (rejected because it hides
real regressions); allow a blanket owner approval (rejected by the compatibility constitution).

## Decision: Catalog actions only in route metadata

`RequirePermission(action)` is the route declaration. Foundation Identity's evaluator remains the
owner of wildcard grants, implication expansion, normalized claims, tenant/resource checks, and
replaceable policy behavior. This keeps endpoint inventories meaningful while retaining administrative
wildcard compatibility.

## Decision: Restore semantic coverage at service and HTTP seams

Expression-tooling tests use the existing provider/context contracts and a mapped TestServer where
headers and JSON are observable. Lifecycle tests continue to call existing request/command handlers
and use mapped HTTP for body binding/status translation. No FastEndpoints base, factory, or endpoint
dependency returns to production.

## Decision: Real collectible cycles

Each cycle maps the API, sends an authorized request, generates OpenAPI, serializes a response through
the owner context, resolves stores/providers, disposes the host scope, and asserts weak references.
Three cycles reduce the chance that a single successful unload masks an accidental static root.

## Decision: Workbench E2E uses documented fresh SQLite setup

The repository's `e2e-tests/README.md` is authoritative for rebuilding Workbench, applying a fresh
schema, starting the HTTP profile, and running a workflow design flow. The command/result is recorded
in the owner report; if local prerequisites prevent execution, that fact is reported rather than
replaced with a synthetic pass.
