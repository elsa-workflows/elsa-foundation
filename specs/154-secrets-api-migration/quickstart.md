# Quickstart: Secrets API Minimal API Migration

## Verify the focused migration

```bash
dotnet test tests/Elsa/Secrets/Tests/Elsa.Secrets.Tests.csproj --no-restore
dotnet test tests/Elsa/Api/Compatibility/Testing/Tests/Elsa.Api.Compatibility.Testing.Tests.csproj --no-restore
dotnet test tests/Elsa/Architecture/Elsa.Architecture.Tests.csproj --no-restore
dotnet run --project tools/maps/Elsa.Maps.Generator -- check
```

Before merge, run the complete affected solution build and repository gates in the feature-delivery loop.

## Inspect the representative migration

1. Confirm the module exposes exactly the ten reviewed method/route pairs under `/secrets`.
2. Confirm every endpoint carries `Elsa.Secrets.Api` ownership, Minimal API authoring metadata, and one Foundation wildcard-or-action permission disposition.
3. Confirm all eight stable permission names have one catalog owner and only write implies read.
4. Run the authorization matrix for anonymous, missing, adjacent, exact, implied, wildcard, untrusted, and resource-denied callers.
5. Run two-tenant same-name cases across every data operation and verify descriptors remain tenant-independent.
6. Submit unique sensitive markers through create/rotate/error paths and verify no body, header, OpenAPI response, or audit observation discloses them.
7. Compare replacement HTTP/OpenAPI evidence with immutable FastEndpoints-before baselines; no unapproved delta may remain.
8. Confirm one unrelated FastEndpoints route still works in the mixed host.
9. Confirm materialized/exercised route, service, and documentation owners release under repeated collectible-context evidence.
10. Confirm no production FastEndpoints dependency or Secrets transition-exception entry remains.

## Diagnose failures

- **HTTP delta**: inspect the named endpoint/method/facet and match the legacy observation unless a separate review approved the change.
- **Binding delta**: verify route name and normalized tenant remain authoritative and web JSON/query conventions match the before host.
- **Authorization delta**: inspect the canonical `Any(*, action)` policy, catalog owner, and sole write-to-read implication; do not add handler claim checks.
- **Disclosure delta**: trace the marker through mapper, exception, serializer, header, and audit paths; never normalize a sensitive value out of evidence.
- **Tenant delta**: ensure the service receives only `IdentityClaimTypes.TenantId`; keep descriptors as the explicit exception.
- **Duplicate route**: remove the legacy registration rather than relying on discovery order.
- **OpenAPI delta**: compare actual operation projections before adding manual metadata.
- **Collectibility delta**: release the diagnosed route, service, serializer, documentation, or harness owner and rerun; do not infer unloadability from process memory.
