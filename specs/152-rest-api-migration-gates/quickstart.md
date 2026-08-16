# Quickstart: REST API Migration Gates

## Verify the work unit

```bash
dotnet test tests/Elsa/Architecture/Elsa.Architecture.Tests.csproj --no-restore
dotnet test tests/Elsa/Api/Compatibility/Testing/Tests/Elsa.Api.Compatibility.Testing.Tests.csproj --no-restore
dotnet run --project tools/maps/Elsa.Maps.Generator -- check
```

Run the repository build and affected solution gates before merge. Baselines are not updated automatically.

## Add or migrate an endpoint

1. Attach exactly one stable endpoint owner through the standard ASP.NET Core convention.
2. Declare exactly one primary security disposition. For a Foundation permission, use the canonical permission policy/metadata rather than adding a second security marker.
3. Ensure the consumed permission has one active `IPermissionContributor` owner. Do not use `*` as an endpoint permission.
4. Capture before/after HTTP and supplied OpenAPI evidence with the shared compatibility helper.
5. If behavior intentionally changes, add one exact approved-difference record with owner, reason, and follow-up. Do not add a broad ignore.
6. Remove the matching FastEndpoints transition exception as the endpoint leaves the legacy authoring model.

Literal routes, cross-document constants, interpolated constants, and the known route-helper compositions are resolved to exact routes and methods. Only genuinely runtime-computed routes use an exact normalized aggregate fingerprint of their owning source set; any owning-source edit requires deliberate review and registry reconciliation.

## Diagnose failures

- **Manifest mismatch**: inspect the normalized route/method and original runtime identity in the failure.
- **Security failure**: add or correct typed ownership/security metadata; do not infer security from a route prefix.
- **Permission failure**: inspect active catalog provenance for zero or multiple owners and confirm endpoint disposition.
- **Transition failure**: either remove the new FastEndpoints registration or add a deliberately reviewed exact exception tied to a follow-up.
- **Compatibility failure**: compare the named facet; approve only an intentional exact delta.
- **Collectibility failure**: use the reported route, service, serializer, or harness stage to locate the strong reference.
