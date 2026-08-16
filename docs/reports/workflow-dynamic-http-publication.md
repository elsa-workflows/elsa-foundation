# Workflow-authored dynamic HTTP publication

Status: implemented on `codex/1366-dynamic-http-metadata` for issue #1366 (Track D).

## Disposition

Workflow-authored routes are published through the existing `IRouteTable` boundary. Each published route is stamped
with a `DynamicShell` owner (`Elsa.Http`, shell discriminator, and generation) and exactly one security disposition:
public compatibility, permission, authenticated principal, host credential, or named policy. This metadata describes
the route and does not replace request authentication, claim normalization, or Foundation Identity policy evaluation;
`IHttpEndpointAuthorizationHandler` remains the runtime enforcement authority.

Static host/module endpoints are projected by `Elsa.Http`'s `HttpFeature` from the endpoint data sources visible in the
activated shell. The generic endpoint ownership/security metadata remains in `Elsa.Api.AspNetCore`; the integration
adapter does not introduce an API-to-workflow-HTTP dependency in the host.

## Evidence matrix

| Concern | Evidence | Result |
|---|---|---|
| Dynamic metadata and legacy compatibility | `DynamicHttpRoutePublicationTests`; resolver tests | Legacy inputs receive explicit metadata; authored static-owner spoofing is rejected. |
| Collision safety | Equivalent-template, method-overlap, host/module, duplicate-metadata, and rollback tests | Conflicts identify both owners and preserve the live generation. |
| Static composition | `DynamicHttpRouteCompositionTests.Root_module_endpoint_manifest_is_promoted_into_http_shell_and_rejects_workflow_collision` | A real CShells root endpoint is visible through `HttpFeature` in the activated Elsa.Http shell. |
| Atomic publication | AddRange/RemoveRange, replacement-race, and no-empty-snapshot tests | Candidates validate before one cache swap; readers see complete generations only. |
| Exact-generation drain | `Replacement_IsAtomicAndOldGenerationDrainsAfterLeaseRelease`; middleware suite | A held lease keeps the old snapshot available until request completion. |
| Lifecycle retention | `Repeated_http_shell_reload_releases_collectible_route_generation_roots` | Four independent real host/shell cycles release shell, child DI, provider, route table, route, owner, delegate, and collectible metadata roots. |
| Serializer disposition | Real lifecycle fixture | The route-publication-only `HttpFeature` shell has no `IPayloadSerializer`; absence is asserted rather than treated as untested. |

## Compatibility and rollback

`IRouteTable` remains source-compatible. `IRouteTableSnapshotProvider` is additive, and middleware falls back to the
existing enumerable route-table behavior for custom implementations. Missing method metadata retains wildcard
collision semantics; legacy routes without security metadata receive an explicit public compatibility disposition.

Refresh, AddRange, and RemoveRange construct and validate a candidate while the current snapshot remains untouched.
Any invalid route, ownership violation, duplicate metadata record, or collision throws before publication. The prior
generation therefore remains available for readers and in-flight leases. Production migration of other API frameworks
and changes to public route contracts remain outside this slice.

## Verification commands

```text
dotnet test tests/Elsa/Http/Tests/Elsa.Http.Tests.csproj --no-restore
dotnet test tests/Elsa/Workflows/Runtime/Http/Tests/Elsa.Workflows.Runtime.Http.Tests.csproj --no-restore
dotnet test tests/Elsa/Activities/Http/Tests/Elsa.Activities.Http.Tests.csproj --no-restore
dotnet test tests/Elsa/Activities/Http/IntegrationTests/Elsa.Activities.Http.IntegrationTests.csproj --no-restore
dotnet test tests/Elsa/Architecture/Elsa.Architecture.Tests.csproj --no-restore
dotnet run --project tools/maps/Elsa.Maps.Generator -- check
```
