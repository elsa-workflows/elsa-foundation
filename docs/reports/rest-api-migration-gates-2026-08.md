# REST API migration gates evidence report

- **Date:** 2026-08-15
- **Program:** First-party REST API Consolidation
- **Work unit:** [#1346](https://github.com/elsa-workflows/elsa-foundation/issues/1346)

## Outcome

The migration safety gates are ready for the canary migration in #1347. They establish a reusable, framework-neutral way to compare externally consumed HTTP and supplied OpenAPI JSON behavior, inventory runtime endpoints, validate authorization ownership, bound the remaining FastEndpoints surface, and diagnose collectible-context retention.

This work does not claim that a production module has migrated. It proves the shared mechanism with equivalent before/after authoring fixtures and mutation tests; #1347 must apply the mechanism to the first production module.

## Acceptance evidence

| Issue criterion | Evidence | Result |
|---|---|---|
| Deterministic endpoint manifest | The representative host records eight routes with normalized methods, typed owner and authoring metadata, one permission disposition, content types, response metadata, and diagnostic source identity. Ten consecutive captures are byte-identical, including tied semantic sort keys. | Pass |
| HTTP and consumed OpenAPI compatibility | The shared test package captures request binding inputs, full ordinary JSON bodies, status, headers, media types, ProblemDetails, paging/filtering observations, bounded streams and terminal state. Supplied OpenAPI JSON projection includes parameters, request bodies, responses, media types, and transitively referenced schemas. Mutation tests exercise every named facet. | Pass |
| Before/after authoring comparison and exact approvals | An equivalent FastEndpoints/Minimal-API evidence fixture compares through the same reusable gate. Exact endpoint/method/case/facet approvals accept only the named delta; unrelated mutations and unused approvals fail. The committed approval registry is empty. | Pass; production application remains #1347 |
| Security and legacy-authoring architecture guard | Standard ASP.NET Core metadata is emitted by all Elsa FastEndpoints bases. A Roslyn syntax gate checks every `Configure()` method under eight current management API roots for exactly one canonical permission and no anonymous access. The transition registry contains 106 reviewed registrations: 101 exact route/method surfaces and five genuinely runtime-computed routes protected by aggregate owner-source fingerprints. Fingerprints include repository source only and exclude generated `bin`/`obj` files, so normal build order cannot change the evidence. New, expanded, stale, duplicate, ambiguous, owner-mismatched, fingerprint-mismatched, or dynamically unloadable registrations fail. | Pass |
| Active permission-catalog ownership | Eight feature-owned contributors cover the current management permission vocabulary. Runtime inventory validation rejects missing or conflicting owners and rejects `*` as an endpoint permission while permitting a route owner to consume a uniquely owned permission from another module. The representative capability endpoint is verified to require its permission policy and to disallow anonymous verbs. | Pass |
| Collectible-context evidence | Repeated Roslyn-compiled collectible endpoint cycles return only weak handles. Clean cycles collect; deliberate route, DI, serializer, and harness retention are classified separately. Route publication and service-provider release are asserted independently. Serializer retention is classified without falsely promising that runtime serializer caches can be released by the harness. | Pass |

## Verification results

- `dotnet build Elsa.Server.slnx --no-restore --verbosity:minimal`: passed with 0 errors. Existing warnings include the tracked SSH.NET package advisory and pre-existing analyzer findings.
- `dotnet test tests/Elsa/Api/Compatibility/Testing/Tests/Elsa.Api.Compatibility.Testing.Tests.csproj --no-restore`: 45 passed, 0 failed.
- `dotnet test tests/Elsa/Architecture/Elsa.Architecture.Tests.csproj --no-restore`: 384 passed, 0 failed.
- `dotnet run --project tools/maps/Elsa.Maps.Generator -- check`: passed; all generated maps describe the tree.

## Boundaries and follow-up

- The reviewed runtime manifest is intentionally representative rather than a claim that every possible host composition is active at once. The source and active-catalog gates cover the eight current management API roots; each migration must add its own runtime before/after evidence.
- The five unresolved transition entries are the option-driven OpenTelemetry and Structured Logs routes. Any edit anywhere in their owning source set invalidates the reviewed fingerprint and requires reconciliation.
- Foundation source projects are statically loaded in this repository. The reusable transition scanner accepts an explicit dynamically-unloadable classification and rejects FastEndpoints in that case; host-specific dynamic module inventories must supply that classification.
- Atomic dynamic route publication and collision handling remain owned by #1345.
- Production endpoint migrations remain owned by #1347, #1348, and #1349.

## Recommendation

Proceed with #1347 as the canary. Require its PR to capture the real module before migration, replace only the authoring model, compare HTTP and supplied OpenAPI evidence, remove the module's exact transition entries, and keep any intentional contract change as a separately reviewed exact approval.
