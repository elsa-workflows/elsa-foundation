# Structured Logs Authorization and Coexistence Contract

## Ownership

Every configured Structured Logs endpoint carries:

- module owner `Elsa.Diagnostics.StructuredLogs`;
- Minimal API authoring disposition;
- permission-protected security disposition; and
- canonical Foundation policy requiring any of `*` or `Diagnostics:StructuredLogs`.

The active permission catalog contains `Diagnostics:StructuredLogs` exactly once, owned and contributed by the Structured Logs module, with no implication.

## Outcomes

| Principal | Expected result |
|---|---|
| Anonymous | Authentication challenge (`401` in the test host) before query/stream work |
| Authenticated, exact permission | Allowed |
| Authenticated, wildcard | Allowed |
| Authenticated, adjacent permission only | Forbidden (`403`) |
| Authenticated, no permission | Forbidden (`403`) |
| Untrusted/un-normalized permission-bearing principal | Forbidden (`403`) |
| Resource evaluator denial | Forbidden (`403`) |

Handlers do not read permission claims. Authentication/provider claim mapping remains outside the module.

## Mixed host

A test host maps all three production Structured Logs Minimal API routes and one unrelated real FastEndpoints route. Both surfaces:

- pass through the same authentication/authorization middleware;
- resolve the same registered Foundation policy provider and instrumented permission evaluator instance;
- retain their own route owner and authoring metadata; and
- appear exactly once in route and security inventories.

Removing Structured Logs legacy endpoints must not remove FastEndpoints support needed by other transitional modules.
