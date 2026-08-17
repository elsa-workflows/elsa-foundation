# Publishing Compatibility Evidence Contract

## Baseline provenance

- The before source is the exact green main commit immediately preceding Wave 8 production changes.
- The checked-in capture script, runner, project graph, root build inputs, and fixture hashes are recorded in a receipt and verified from committed blobs.
- The runner fails if its executing content differs from the pinned committed content.
- Two detached captures must produce byte-identical HTTP, projected OpenAPI, raw OpenAPI, approval, and receipt data.
- The frozen baseline contains exactly 23 registrations and 23 OpenAPI operations.

## Required HTTP coverage

Every route has an anonymous challenge and an authenticated case. Every endpoint family additionally has successful and representative binding/domain failures. The corpus must include:

- missing, zero-length, JSON `null`, malformed, wrong-content-type, and absent-content-type bodies where applicable;
- route/body conflicts and reserved `drafts` route selection;
- 200/201/202 and exact `Location` behavior;
- 400/403/404/409/422/500/501/503 ProblemDetails families;
- preflight token, review snapshot, policy revision, publication ID, idempotency key, and request fingerprint outcomes;
- slot unpublish/restore and compensation;
- activity receipt replay and fingerprint mismatch;
- workflow/activity test-run creation, lookup, expiry, cancellation, and non-invocation on invalid or denied requests;
- cancellation rethrow/identity where the existing endpoint promise exposes it.

## OpenAPI comparison

For each operation compare operation ID, tag, method/path, parameters, request body, response statuses, headers, content types, schemas, and security. Compare unchanged common response/schema facets deeply; removing an approved facet must not remove the surrounding operation from comparison.

## Approval registry

Each entry identifies one endpoint/case/operation facet and exact before/after values, reason, owner, review reference, and optional follow-up. Validation must fail for duplicate keys, unknown properties, unused entries, no-op values, one-sided values, stale keys, overly broad matches, or values absent from the real before/after artifacts. Dedicated mutation tests bite every rule and assert typed validation errors with exact keys/messages.

## Stable contract evidence

- Every API-visible request/response/error type is owned by `Elsa.Workflows.Publishing.Api.Core` or an existing stable shared Core assembly.
- Public namespaces and member signatures remain compatible; former implementation types resolve through forwarding where required.
- The stable Core dependency graph contains no ASP.NET Core, FastEndpoints, owner implementation, provider, store, handler, or serializer-context dependency.
- Effective HTTP JSON resolver order covers every top-level accepts/produces type through generated metadata before reflection fallback.

## Report truth

The final report records exact commands, counts, hashes, commits or durable Git object identities, fixture approvals, E2E source/build provenance, warnings, residual risks, rollback, and the handoff to #1376. Claims are updated after the final review and final reachable commit.
