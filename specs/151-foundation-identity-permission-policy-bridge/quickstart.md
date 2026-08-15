# Quickstart — Validate the Foundation Identity permission policy bridge

Run from the repository root after implementation.

## 1. Focused builds

```bash
dotnet build src/Elsa/Foundation/Identity/Abstractions/Elsa.Foundation.Identity.Abstractions.csproj --no-restore
dotnet build src/Elsa/Api/FastEndpoints/Elsa.Api.FastEndpoints.csproj --no-restore
dotnet build src/Elsa/Foundation/Identity/Api/Elsa.Foundation.Identity.Api.csproj --no-restore
dotnet build src/Elsa/Foundation/Identity/AspNetCoreIdentity/Elsa.Foundation.Identity.AspNetCoreIdentity.csproj --no-restore
```

## 2. Focused tests

```bash
dotnet test tests/Elsa/Foundation/Identity/Tests/Elsa.Foundation.Identity.Tests.csproj --no-restore
dotnet test tests/Elsa/Api/FastEndpoints/Tests/Elsa.Api.FastEndpoints.Tests.csproj --no-restore
dotnet test tests/Elsa/Modularity/Tests/Elsa.Modularity.Tests.csproj --no-restore
dotnet test tests/Elsa/Architecture/Elsa.Architecture.Tests.csproj --no-restore
```

The same-host matrix must prove 401/403/success parity for single, any, and all endpoints; exact/implied/wildcard/replacement evaluator; trusted-runtime-type/forged-marker and multiple-trusted-identity boundaries; unrelated-policy result-handler delegation; authentication-before-routing behavior; protected-resource preservation; request cancellation; resource precedence; resource/evaluator timeout propagation; and operational failures. Focused tests assert the external-factory, reconstructed-cookie, and validated-bearer runtime `AuthenticationType` values. Registration tests cover all three tagged replacement contracts, repeated Foundation registration, zero/one/multiple result-handler implementation/factory/instance fallback descriptors, and `AddHttpContextAccessor` in a host without ASP.NET Core Identity.

## 3. Dependency and adapter inspection

```bash
dotnet list src/Elsa/Api/FastEndpoints/Elsa.Api.FastEndpoints.csproj reference
dotnet list src/Elsa/Foundation/Identity/Abstractions/Elsa.Foundation.Identity.Abstractions.csproj reference
dotnet list src/Elsa/Foundation/Identity/Abstractions/Elsa.Foundation.Identity.Abstractions.csproj package --include-transitive
rg -n '\.Permissions(All)?\(' src/Elsa/Api/FastEndpoints/Abstractions
rg -n 'IdentityClaimTypes\.Permission|PermissionsClaimType|Find(All|First).*Permission|\.Claims' src/Elsa --glob '**/Api/**/*.cs' --glob '**/Abstractions/ElsaEndpoint*.cs'
```

Expected: FastEndpoints references Identity Abstractions; Identity has no FastEndpoints/Elsa project reference; no Elsa endpoint base calls direct permission matching. The `rg` result is review reconnaissance only. The architecture test's Roslyn symbol/data-flow analysis and mutation fixtures are authoritative; its allowlist is symbol/path-scoped to non-authorization Identity token/session projection and #1356's two deferred transport contexts.

## 4. Full repository gates

```bash
dotnet build Elsa.Server.slnx -c Release --no-restore -p:WarningsNotAsErrors=NU1603
keep=()
while IFS= read -r p; do
  grep -q "Testcontainers" "$p" || keep+=("$p")
done < <(find tests -name '*.csproj' | sort)
projects=$(printf '"%s",' "${keep[@]}" | sed 's/,$//')
printf '{"solution":{"path":"Elsa.Server.slnx","projects":[%s]}}' "$projects" > Elsa.Server.test.slnf
dotnet test Elsa.Server.test.slnf -c Release --no-build -p:WarningsNotAsErrors=NU1603
dotnet run --project tools/maps/Elsa.Maps.Generator -- check
git diff --check
```

`Elsa.Server.test.slnf` is a generated, uncommitted container-free test filter matching the CI recipe.
Remove it after the local run if it is not already ignored.

If project dependency changes make generated maps stale, run the authorized full refresh and stage every changed map plus `docs/maps/manifest.json` when it changes:

```bash
dotnet run --project tools/maps/Elsa.Maps.Generator -- all
dotnet run --project tools/maps/Elsa.Maps.Generator -- check
```

## 5. Review evidence

Before merge, post a PR comment recording focused counts, full build/test results, architecture and map results, diff review, exact branch/commit, and a negative mutation/revert bite-proof for the direct-claim adapter guard. After merge, verify Maps, CI, and HTTP workflow performance on `main`.
