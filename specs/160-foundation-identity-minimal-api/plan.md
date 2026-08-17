# Implementation Plan: Foundation Identity Minimal API Migration

**Branch**: `codex/1369-wave3-identity-minimal-apis` | **Date**: 2026-08-16 | **Spec**: [spec.md](spec.md)

## Summary

Replace exactly nine Foundation Identity FastEndpoints registrations with two explicit owner-local Minimal API mappers. Preserve interactive authentication, token/session behavior, HTTP/OpenAPI contracts, and mixed-host coexistence; align capabilities with catalog-owned action metadata; use owner-local source-generated JSON; and prove both owners collectible through real route/auth/provider/DI/serialization/disposal lifecycles.

## Technical Context

**Language/Version**: C# / .NET 10 (`net10.0`)

**Primary Dependencies**: ASP.NET Core Minimal APIs, authentication/authorization, OpenAPI, CShells `IWebShellFeature`, Foundation Identity policy/catalog/normalization, OpenIddict, ASP.NET Core Identity, `Elsa.Api.Compatibility.Testing`.

**Testing**: Immutable before HTTP/OpenAPI fixtures, real-host differential comparison, authorization matrix, bearer-to-token regression, metadata/manifest and coexistence checks, owner-isolated repeated collectibility, identity suite, architecture guard, maps, build, backend E2E.

**Constraints**: Two owners and exactly nine registrations; no shared endpoint DSL; configured interactive schemes only for token exchange; wildcard evaluator-only; no blanket unloadability waiver; Wave 2 registry base 143 → 134.

## Constitution Check

- Mapping remains in the two Foundation Identity owner packages; identity services and framework-neutral contracts remain in their existing layers.
- Foundation Identity remains the only permission authority. Endpoint metadata names the catalog action; normalization, implication, and wildcard behavior stay in the evaluator.
- Existing behavior tests and a frozen real-FastEndpoints oracle remain as required by framework §2.21.1.
- Owner-local source-generated serialization and weak-reference unload evidence protect the dynamic feature boundary.
- The design adds no new cross-domain dependency or persistence model and preserves mixed authoring during the transition.
- Framework §2.24 and Elsa §E2.9 are draft/provisional; this work does not use either as a ratified exception.

## Project Structure

```text
src/Elsa/Foundation/Identity/Api/
├── FoundationIdentityApi.cs
├── FoundationIdentityApiFeature.cs
└── FoundationIdentityApiJsonContext.cs
src/Elsa/Foundation/Identity/AspNetCoreIdentity/
├── AspNetCoreIdentityApi.cs
├── AspNetCoreIdentityFeature.cs
└── AspNetCoreIdentityJsonContext.cs
tests/Elsa/Foundation/Identity/Tests/
├── Api/IdentityCompatibilityComparerTests.cs
├── Api/MinimalIdentityEndpointMetadataTests.cs
├── Api/PermissionEndpointAdapterIntegrationTests.cs
├── Api/TokenEndpointTests.cs
└── Baselines/identity-*.json
tests/Elsa/Architecture/Wave3IdentityMinimalApiCollectibilityTests.cs
docs/reports/foundation-identity-wave3-minimal-api.md
```

## Design

1. Freeze real FastEndpoints HTTP/OpenAPI observations before removing endpoints, then use one exact comparer and approval set against the migrated host.
2. Map the seven Foundation Identity and two ASP.NET Core Identity routes explicitly with stable names/tags, owner and Minimal authoring metadata, response/OpenAPI metadata, and one public-or-policy disposition.
3. Authenticate token exchange only through configured interactive schemes. Do not trust a pre-populated default-scheme bearer principal.
4. Require only `identity.providers.read` on capabilities and exercise the shared evaluator through both Minimal API and retained FastEndpoints routes.
5. Serialize request/response records through owner-local generated contexts, including null/empty binding compatibility.
6. Materialize and release endpoint, authentication/provider, DI, serializer, and disposal surfaces in repeated isolated owner cycles.
7. Remove only the nine FE classes/references/registry entries, refresh maps, update identity docs, and execute all repository gates.

## Risks and Mitigations

- Token re-minting: explicit interactive-scheme authentication plus bearer-to-token 401 regression.
- Cookie/challenge/redirect drift: real HTTP fixtures include those effects.
- OpenAPI operation drift: stable names/tags and consumed before/after projections.
- Wildcard ownership confusion: exact single-action metadata assertion plus evaluator wildcard test.
- Collectible assembly retention: source-generated contexts and repeated real-surface weak-reference evidence.
- Stale migration ratchet after Wave 2: mechanically derive the 134-entry combined baseline and verify exact nine-entry removal.
