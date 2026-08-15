# Endpoint framework and authorization spike

- **Issue:** [#1329](https://github.com/elsa-workflows/elsa-foundation/issues/1329)
- **Date:** 2026-08-14
- **Program-goal state:** `none/free-flow` for the spike; the approved migration should be planned
  under Feature Composition Readiness or a narrower API/security program
- **Foundation baseline:** `1d64157ddd6a8995f060506be1c88b5b2c323c8c`
- **CShells baseline:** `5a77be18bb2baa8da4d1c8a06308c3bcde12af2b`
- **Runtime:** .NET SDK 10.0.300; FastEndpoints 7.2.0

This is a point-in-time spike report, not a ratified architecture rule. It records the evidence,
negative findings, recommendation, and proposed implementation units. No production code was
changed during the spike; all prototype source was removed after its results were captured.

The normative decision derived from this evidence is
[ADR 0068: First-party REST APIs use ASP.NET Core Minimal APIs](../adr/0068-first-party-rest-apis-use-aspnet-core-minimal-apis.md).

## Decision

Adopt **ASP.NET Core Minimal APIs as the target standard for all first-party REST APIs**, with
FastEndpoints coexistence limited to the staged migration period:

1. Keep the existing Minimal API implementation for root CShells/Nuplane management endpoints and
   use the same ASP.NET Core endpoint model as the target for Elsa module REST APIs.
2. Migrate the existing FastEndpoints feature APIs incrementally. Coexistence is a compatibility
   mechanism that keeps the application deployable between migration waves; it is not the intended
   permanent endpoint-authoring model.
3. Preserve public HTTP behavior during migration: routes, methods, binding, JSON contracts,
   status codes, ProblemDetails, streaming behavior, and OpenAPI output must be pinned by contract
   tests before each module moves.
4. Make Foundation Identity's policy provider, permission evaluator, catalog contributions, and
   resource handlers the single authorization path throughout the transition and in the target
   architecture. Endpoint mappings declare requirements; they do not own permission semantics.
5. Treat dynamically unloadable endpoint assemblies as **not FastEndpoints-compatible**. The spike
   observed FastEndpoints retaining all three collectible contexts; new dynamically unloadable
   REST features should use the Minimal API module-mapping model and carry a repeatable unload test.
6. Replace CShells' remove-then-add route publication with a validated candidate manifest and one
   atomic snapshot swap. Reject collisions before activation and bind routed requests to the exact
   shell generation recorded in endpoint metadata.

The spike directly proves Minimal API suitability for root management and proves coexistence and
shared authorization with a representative FastEndpoints route. It does not directly prove
behavioral parity for all 178 FastEndpoints registrations. The target decision therefore requires
a representative Elsa module migration before broad migration waves begin.

### Target architecture and guardrails

- Each first-party module exposes an explicit `Map...Api(IEndpointRouteBuilder)` composition seam
  and contributes ordinary ASP.NET Core endpoints and metadata. Process-global assembly discovery
  is not part of the target module contract.
- Keep the shared Elsa endpoint layer deliberately small: permission extensions, public/host
  disposition metadata, ProblemDetails and validation conventions, OpenAPI conventions, and route
  ownership metadata. Binding, serialization, routing, filters, results, and policy execution stay
  with ASP.NET Core.
- Do not recreate FastEndpoints as an Elsa-specific framework. Add a shared abstraction only when
  at least three consumers need it and contract tests show that ASP.NET Core has no adequate
  primitive.
- New first-party REST APIs use Minimal APIs by default. A FastEndpoints addition during the
  transition requires a documented compatibility exception and may not be placed in an assembly
  promised to be dynamically unloadable.
- Workflow-authored HTTP endpoints remain a distinct dynamic publication model, but must carry the
  same authorization disposition and route ownership metadata where applicable.

The HTTP/JSON protocol remains unchanged. This report does not reopen REST versus JSON-RPC or gRPC.

## What was tested

The spike combined repository inventory, existing tests, and four disposable executable
experiments:

- a shared-authorization host with one Minimal API route and one FastEndpoints route;
- the actual CShells Minimal API management mapper alongside a dynamically loaded FastEndpoints
  shell feature;
- current CShells collision/replacement behavior alongside an atomic candidate-manifest model;
- three collectible endpoint assemblies mapped through FastEndpoints in one process.

The prototypes were intentionally not retained. Their commands, inputs, and observed outputs are
summarized below so production follow-ups can turn the conclusions into permanent contract tests.

## Minimal API evidence and remaining feature parity

The current CShells management package already maps six handlers under a standard
`RouteGroupBuilder`. Existing tests prove group-level `RequireAuthorization` applies to all six
routes and that endpoint filters execute before handlers.

The executable comparison covered the management surface's actual needs, not the union of every
feature FastEndpoints offers. It establishes that the existing Minimal API management surface is
sound; it also identifies the feature-level behaviors that each later migration wave must preserve:

| Capability | Actual Elsa/CShells use | Minimal API fit for root management | Decision |
|---|---|---|---|
| Binding | Elsa FastEndpoints use typed request DTOs extensively. CShell management uses route parameters, DI services, and ordinary JSON results. | Built-in parameter/body binding is sufficient; custom binding remains available. | No blocker. |
| Validation | No FastEndpoints `Validator<T>` implementation was found in first-party source; domain validation occurs in application services and handlers. | Endpoint filters or ordinary service validation cover the management surface. | No blocker. |
| Serialization | Elsa has centralized FastEndpoints/System.Text.Json configuration and contract tests. CShell management uses standard ASP.NET Core JSON. | Sufficient for existing management DTOs; pin JSON contracts in tests. | No blocker; do not assume settings are identical. |
| Problem details | Elsa uses a FastEndpoints ProblemDetails configurator and several typed problem models. | ASP.NET Core `AddProblemDetails`, `Results.Problem`, and typed results cover root management needs. | No blocker for management; pin feature API behavior before migration. |
| OpenAPI | Twenty-seven first-party source files contain summary, description, Swagger, or OpenAPI declarations. | Route groups support `WithOpenApi`, `Produces`, descriptions, tags, and operation transformers. | No management blocker; migrated feature APIs must preserve their consumed OpenAPI contract. |
| Filters/processors | No first-party FastEndpoints pre/post processor or ASP.NET `IEndpointFilter` implementation was found in the endpoint source inventory. | CShells already proves group filters compose. | No blocker. |
| Discovery | Assembly scanning is central to the current Elsa FastEndpoints modules. Root management routes are six explicit mappings. | Explicit mapping is preferable for this small host-owned surface. | Replace scanning incrementally with explicit module mapping seams. |
| Streaming | Agent, Structured Logs, and OpenTelemetry use SSE/streaming helpers. Root management does not. | Minimal APIs can stream, but parity is not needed for this decision. | Out of the root-management comparison. |
| Authorization | FastEndpoints currently performs direct permission-claim matching; Foundation Identity has the richer policy/evaluator path. | Standard authorization metadata works on route groups and individual routes. | Material cross-framework gap; unify on Foundation policies. |
| CORS/rate limiting | No management-specific custom behavior was found. | Standard route-group conventions apply. | No blocker. |

The coexistence prototype used the real `MapShellManagementApi("/admin")`, the real
`MapShells()` pipeline, and a CShells-loaded FastEndpoints feature. Both requests succeeded in the
same TestServer:

| Request | Expected | Observed |
|---|---:|---:|
| `GET /admin/` | 200 | 200 |
| `GET /app/feature/ping` | 200 | 200 |

This proves the immediate management decision and the transition mechanism: **keep Minimal APIs
for root management, migrate feature adapters in stages, and allow both models to run until the
last migration wave is complete**. Representative feature parity remains a follow-up gate.

## First-party endpoint and authorization inventory

The inventory found 178 statically declared FastEndpoints route registrations, additional root
Minimal API routes, and an unbounded set of workflow-authored HTTP routes. No first-party MVC
surface was found in `src/Elsa`, `src/Elsa3`, or `src/Apps`. The table records the **current state**;
every FastEndpoints row is a pending migration surface under the target decision, not an approved
permanent adapter.

`FE + *` below means the Elsa endpoint base ultimately calls FastEndpoints permission matching
with the endpoint permission plus the administrative `*` grant.

| Owner | Adapter / approximate routes | Current disposition | Permission ownership / finding |
|---|---:|---|---|
| Activities Design API | FastEndpoints / 38 | FE + `activity-design.read/manage` + `*` | Shared `PermissionNames`; included in current guard. |
| Workflows Design API | FastEndpoints / 27 | FE + `workflow-design.read/manage` + `*` | Shared `PermissionNames`; included in current guard. |
| Workflows Publishing API | FastEndpoints / 23 | FE + `workflow-publishing.read/manage` + `*` | Shared `PermissionNames`; included in current guard. |
| Workflows Runtime API | FastEndpoints / 24 | FE + `workflow-runtime.read/execute/manage` + `*` | Shared `PermissionNames`; included in current guard. |
| Expressions API | FastEndpoints / 2 | FE + `expressions.read` + `*` | Shared `PermissionNames`; included in current guard. |
| API Capabilities | FastEndpoints / 1 | FE + `api-capabilities.read` + `*` | Shared `PermissionNames`; included in current guard. |
| Elsa 3 Design Import | FastEndpoints / 5 | FE + `elsa3-import.read/manage` + `*` | Shared `PermissionNames`; included in current guard. |
| BPMN Interchange | FastEndpoints / 3 | FE + `bpmn-interchange.read/manage` + `*` | Shared `PermissionNames`; included in current guard. |
| Agent API | FastEndpoints / 11 | FE + agent-specific permissions + `*` | Module constants; no catalog contributor; omitted from guard. |
| Attention API | FastEndpoints / 1 | FE wildcard only | No module permission; omitted from guard. |
| Diagnostics Structured Logs | FastEndpoints / 3 | FE + `Diagnostics:StructuredLogs` + `*` | Internal constant; no contributor; omitted from guard. |
| Diagnostics OpenTelemetry | FastEndpoints / 10 | Query/stream permission; OTLP ingestion explicitly anonymous then API-key/loopback checked | Internal constant; no contributor; omitted from guard. |
| JavaScript Rendering | FastEndpoints / 1 | FE wildcard only | No module permission; omitted from guard. |
| Foundation Identity API | FastEndpoints / 7 | Six explicit anonymous bootstrap/session/token routes; capabilities permission-protected | Identity owns its default catalog. |
| ASP.NET Core Identity UI/API | FastEndpoints / 2 | Explicit anonymous login routes | Intentional public entry points. |
| Modularity API | FastEndpoints / 2 | FE wildcard only | Contributor exists, but endpoints do not consume `module-management.read/manage`. |
| Modularity Extension Builder | Root Minimal API / 42 | Outer API key; 41 routes also require a configured trusted role | Contributor exists, but mappings do not consume it. |
| Secrets API | FastEndpoints / 10 | FE + granular secrets permissions + `*` | Module constants; no contributor; omitted from guard. |
| Studio Preferences API | FastEndpoints / 2 | FE + read/write + `*` | Module contributor exists; omitted from guard. |
| Workflows Dashboard | FastEndpoints / 2 | FE + read + `*` | Module contributor exists; omitted from guard. |
| Workflow Execution Evidence | FastEndpoints / 3 | FE wildcard only | No module permission; omitted from guard. |
| Runtime JavaScript | FastEndpoints / 1 | FE wildcard only | No module permission; omitted from guard. |
| Workflow HTTP endpoints | CShells middleware / dynamic | `Authorize=false` by default; arbitrary ASP.NET policy when enabled | Workflow-authored disposition; no catalog permission. |
| Foundation Host | Root Minimal API / health + module management | Health public; module management uses a static API-key filter | No shared permission metadata. |
| Workbench root | Root Minimal API + external mappers | Root/health public; module management API key; console stream generic authorization | Mixed host-control paths; no shared permission metadata. |

The current architecture test scans only the first eight rows. Anonymous declarations are usually
explicit at their individual mapping sites, but there is no central allowlist/reason metadata and no
guard spanning Minimal APIs, host credentials, dynamic workflow routes, and all FastEndpoints
modules.

## Framework-neutral authorization proof

As a transition bridge, the prototype registered Foundation Identity's real dynamic policy
provider, permission authorization handler, composite catalog, and a replaceable evaluator. A
Minimal API endpoint used
`RequireAuthorization(permissionPolicy)`; a FastEndpoints endpoint used its ASP.NET policy
integration rather than `Permissions(...)`. Both required `shell-management.read`.

The candidate evaluator preserved the existing administrative wildcard while delegating ordinary
checks to `ClaimsPermissionEvaluator`. A module contributor declared:

- `shell-management.read`;
- `shell-management.manage`, implying `shell-management.read`.

All ten HTTP cases passed:

| Principal | Minimal API | FastEndpoints |
|---|---:|---:|
| Anonymous | 401 | 401 |
| Authenticated, missing permission | 403 | 403 |
| Exact `shell-management.read` | 200 | 200 |
| Implied by `shell-management.manage` | 200 | 200 |
| Administrative `*` | 200 | 200 |

The recording evaluator observed all permission decisions for both `/minimal` and `/fast`.
This proves the shared path is feasible. It does **not** mean production is already unified:
current Elsa endpoint bases still call FastEndpoints `Permissions(...)`, bypassing Foundation
implication, resource handlers, and evaluator replacement.

### Required authorization semantics

The production design should make these rules explicit:

- Authentication normalizes external-provider claims into Foundation claim types before
  authorization. Provider-specific claim mapping stays outside endpoint adapters.
- A missing authenticated principal results in the standard middleware challenge (401); an
  authenticated principal that fails a requirement is forbidden (403).
- Preserve `*` in the shared evaluator during migration because current endpoint bases and
  administrator seeders depend on it. Any later removal requires an explicit migration.
- A single permission policy requires exactly one permission.
- Multiple permissions use named, separate combinators: `RequireAnyPermission(...)` and
  `RequireAllPermissions(...)`. Do not overload a variadic method with adapter-specific defaults.
- Endpoint-owning modules contribute their own permission definitions through
  `IPermissionContributor`; the identity domain owns only identity permissions and composition.
- A resource handler's explicit denial should be a hard veto; `null` means abstain. The current
  handler can allow a general evaluator grant after a resource denial, so this recommendation needs
  a compatibility decision and tests.
- Public routes must carry explicit public metadata with a reason/category, or explicit
  host-credential metadata. `AllowAnonymous` alone is insufficient for the architecture inventory.

MVC is not a current first-party adapter. Standard MVC `AuthorizeAttribute.Policy` can use the
same provider if MVC is introduced; a permanent adapter contract test should be added at that time.

## Dynamic routing and lifecycle

### Current behavior

The current
[dynamic data source](https://github.com/valence-works/cshells/blob/5a77be18bb2baa8da4d1c8a06308c3bcde12af2b/src/CShells.AspNetCore/Routing/DynamicShellEndpointDataSource.cs)
compares raw route text and only the first HTTP method, logs conflicts, and still publishes them.
The registration handler removes all existing shell endpoints before mapping the candidate.
Removal and addition publish separate change tokens.

The executable characterization confirmed:

| Case | Current published endpoints | Result |
|---|---:|---|
| Same method + exact route | 2 | Conflict published |
| `{id}` versus `{name}` | 2 | Equivalent templates published |
| `GET,POST` versus `POST,DELETE` | 2 | Overlapping method sets published |
| Two conflicts in one candidate batch | 2 | Same-batch conflict published |
| Remove generation 1, then add generation 2 | Notification observed count 0 | Transient empty snapshot |

Source review found two further gaps:

- candidate mapping can fail after old endpoints have been removed, leaving an active shell without
  its previous routes;
- `ShellMiddleware` resolves the active shell by ID and does not bind to the generation stored on
  the matched endpoint, so a request routed on generation N can resolve generation N+1 after reload.

Existing CShells tests do prove that shell scopes drain through response completion, old-generation
registry references are released, and generation-aware removals preserve newer entries. Those
mechanisms are necessary but do not make route publication atomic.

### Candidate model proof

A disposable immutable-snapshot model expanded each endpoint's method set, canonicalized parameter
names, validated the whole candidate before publication, and swapped one snapshot with
`Interlocked.Exchange`.

It demonstrated:

- a successful swap made generation 2 visible to new requests without an empty snapshot;
- a request that had captured generation 1 completed as generation 1 after the swap;
- a conflicting candidate was rejected and generation 2 remained callable;
- diagnostics named shell, generation, and both owning features;
- equivalent templates with overlapping multi-method sets produced one deterministic conflict.

Production canonicalization must be stricter than the prototype: preserve constraints, defaults,
optional/catch-all semantics, route order, host metadata, and the ASP.NET routing case rules.
Missing `HttpMethodMetadata` must be treated as all methods. Validation must include host-owned and
all shell-owned endpoint manifests, not only the dynamic data source's private list.

The production transaction should be:

1. map a candidate generation into an isolated builder;
2. attach route ownership metadata;
3. validate the complete host + active + candidate manifest;
4. reject without changing the current snapshot, or atomically publish one replacement snapshot
   and one change token;
5. route new requests to the new snapshot while already matched requests retain their exact
   generation/provider;
6. drain and dispose the old generation, then request assembly-context unload.

## Collectible assembly evidence

The unload prototype compiled three distinct endpoint assemblies, loaded each into its own
collectible `AssemblyLoadContext`, mapped it through FastEndpoints 7.2.0, served a successful
request, disposed the host, called `Unload()`, and forced repeated full collections.

| Cycle | Context collected | FastEndpoints registrations in next host |
|---:|---:|---:|
| 1 | No | 2 |
| 2 | No | 3 |
| 3 | No | N/A |

Summary: **0/3 contexts were collected**. Registrations accumulated across disposed hosts. An
initial same-route variant also failed the second host with a duplicate-route error; unique routes
and endpoint names allowed all three cycles to finish and made the retention measurable.

This isolates a FastEndpoints composition-level retention problem; it does not identify the exact
static root and does not attribute it to CShells. It supports migrating REST endpoint assemblies
away from FastEndpoints and is sufficient to reject the assumption that a transitional
FastEndpoints feature assembly is collectible today. It does **not** prove that Minimal API module
mapping automatically makes an assembly collectible: the representative migration must repeat the
weak-reference test across routing, DI, serialization, and module disposal.

## Acceptance evidence

“Evidence complete” means the spike answered the question. A negative finding can therefore be
evidence-complete while production remains unready.

| # | Criterion | Evidence status | Production status |
|---:|---|---|---|
| 1 | Actual FastEndpoints use compared with Minimal APIs | Complete for the spike inventory and root management | Root management ready; representative feature parity gate pending. |
| 2 | Every first-party endpoint owner/adapter inventoried | Complete | Inventory reveals uncovered surfaces. |
| 3 | Architecture covers management and Elsa modules | Complete design | Not implemented. |
| 4 | Representative adapters use one evaluator | Complete for normative Minimal API + transitional FastEndpoints; MVC N/A | Not implemented in first-party endpoints. |
| 5 | Modules contribute permissions outside identity | Existing contributor tests and prototype pass | Several modules still lack or do not consume contributors. |
| 6 | Public endpoints are explicit/reviewable | Negative finding complete | No central reason metadata/allowlist. |
| 7 | Architecture guard catches omitted disposition | Negative finding complete | Current guard covers only eight roots. |
| 8 | Management Minimal APIs coexist with feature FastEndpoints | Executable proof complete | Supported as a staged-migration mechanism. |
| 9 | 401 versus 403 | Executable proof complete | Shared production path pending. |
| 10 | Exact, implied, wildcard | Executable proof complete | Current Foundation evaluator lacks wildcard; FE direct path lacks implication. |
| 11 | Minimal API and FastEndpoints share evaluator | Executable proof complete | Endpoint-base migration pending. |
| 12 | Atomic successful replacement | Candidate proof; current negative finding | CShells change required. |
| 13 | In-flight old request drains | Candidate proof plus existing scope-drain tests | Exact generation binding required. |
| 14 | Failed candidate preserves previous generation | Candidate proof; current negative finding | CShells change required. |
| 15 | Conflicts fail with both owners | Candidate proof; current negative finding | Ownership metadata and validation required. |
| 16 | Repeated collectible-context evidence | Complete: 0/3 collected | FastEndpoints retention blocks unloadability for remaining transitional modules; Minimal API unload proof pending. |
| 17 | Keep/migrate/coexist recommendation and follow-ups | Complete | Minimal APIs selected as target; staged implementation pending. |

## Verification record

| Command / experiment | Result |
|---|---|
| `dotnet test tests/CShells.Tests/CShells.Tests.csproj --no-restore` | 597 passed |
| CShells routing/lifecycle focused filter | 27 passed |
| CShells management/authorization focused filter | 36 passed |
| `dotnet test tests/Elsa/Api/FastEndpoints/Tests/Elsa.Api.FastEndpoints.Tests.csproj` | 25 passed |
| `dotnet test tests/Elsa/Foundation/Identity/Tests/Elsa.Foundation.Identity.Tests.csproj` | 152 passed |
| Foundation architecture suite before full restore | Correctly refused an incomplete assets scan |
| Foundation architecture suite during prototypes | One expected failure because disposable projects were intentionally outside the solution map |
| Final `dotnet test tests/Elsa/Architecture/Elsa.Architecture.Tests.csproj --no-restore` | 363 passed |
| `dotnet run --project tools/maps/Elsa.Maps.Generator --no-restore -- check` | Generated maps still describe the tree |
| Shared authorization HTTP matrix | 10/10 passed |
| Actual CShell management + dynamic FastEndpoints coexistence | 2/2 requests passed |
| Routing characterization + candidate model | All expected current gaps and target behaviors observed |
| Collectible FastEndpoints contexts | 0/3 collected; retention gap |

The full restore reported an existing
[`SSH.NET` advisory](https://github.com/advisories/GHSA-q939-rpr3-3284) for version 2025.1.0.
That warning predates and is unrelated to this documentation-only spike.

## Proposed follow-up units

Do not combine these into one migration PR:

1. **Architecture decision and Minimal API conventions**
   - Record Minimal APIs as the target first-party REST API authoring model, coexistence as the
     migration mechanism, and FastEndpoints as a transitional dependency.
   - Define the explicit module mapping seam and the small shared convention set for validation,
     ProblemDetails, OpenAPI, endpoint ownership, and public/host dispositions.

2. **Shared permission policy contract**
   - Add framework-neutral permission metadata/extensions, wildcard compatibility, explicit
     any/all requirements, normalized-claim integration, and resource-denial precedence tests.
   - Add permanent Minimal API and FastEndpoints adapter contract tests so authorization remains
     unified while both authoring models coexist.

3. **Representative Elsa module migration**
   - Select a module that exercises typed binding, validation, ProblemDetails, OpenAPI, granular
     permissions, and streaming if practical.
   - Snapshot its HTTP and OpenAPI contracts, migrate it to an explicit Minimal API mapping seam,
     and prove behavior and collectible-context expectations before approving migration waves.
   - Classify SSE, OTLP ingestion, health probes, host-management credentials, and
     workflow-authored routes explicitly rather than assuming one mapping recipe covers them all.

4. **Staged Elsa module migration and FastEndpoints retirement**
   - Migrate endpoint-owning modules in bounded waves without changing public routes or DTOs.
   - Keep each wave independently deployable, remove its FastEndpoints discovery/configuration
     only after contract tests pass, and remove the dependency after the final wave.

5. **Complete permission ownership and endpoint disposition inventory**
   - Add contributors for Agent, Secrets, Diagnostics, and other ad-hoc modules.
   - Make Modularity and Extension Builder consume their existing contributed permissions, or
     formally classify their host-control credential path.
   - Give wildcard-only modules an explicit permission or an approved public/host disposition.

6. **Runtime endpoint authorization and authoring guard**
   - Build representative hosts and enumerate endpoint metadata rather than relying only on source
     regexes.
   - Require each first-party endpoint to carry shared permission metadata, approved public-reason
     metadata, or approved host-credential metadata.
   - Prevent new unapproved FastEndpoints registrations and add a separate publication-time rule
     for workflow-authored HTTP endpoints.

7. **Atomic CShells endpoint publication**
   - Introduce route-owner metadata, complete candidate manifests, conflict canonicalization,
     deterministic rejection, one snapshot swap/change token, and exact generation binding.
   - Convert the spike cases into permanent CShells integration/concurrency tests.

8. **FastEndpoints transition risk and collectible-context evidence**
   - Retain the 0/3 reproducer as evidence for the transition constraint and publish it upstream if
     doing so can improve the safety of the coexistence period.
   - Do not make resolution of FastEndpoints retention a prerequisite for its eventual removal;
     continue to forbid it for assemblies promised to be dynamically unloadable.

The spike itself remains `none/free-flow`. The approved migration is broader durable work: place
units 1–8 under Feature Composition Readiness (or create a narrower API/security program) and track
each migration wave separately rather than expanding issue #1329 into one implementation unit.

## References

- [Issue #1329](https://github.com/elsa-workflows/elsa-foundation/issues/1329)
- [FastEndpoints global-state issue #1199](https://github.com/elsa-workflows/elsa-foundation/issues/1199)
- [Studio management bridge #584](https://github.com/elsa-workflows/elsa-foundation/issues/584)
- [Permission contribution seam #587](https://github.com/elsa-workflows/elsa-foundation/issues/587)
- [Elsa permission composition](../../src/Elsa/Api/FastEndpoints/Abstractions/ElsaEndpointPermissions.cs)
- [Foundation authorization contracts](../../src/Elsa/Foundation/Identity/Abstractions/Authorization/AuthorizationContracts.cs)
- [Current architecture guard](../../tests/Elsa/Architecture/EndpointSecurityTests.cs)
- [ASP.NET Core policy authorization](https://learn.microsoft.com/aspnet/core/security/authorization/policies)
- [FastEndpoints security](https://fast-endpoints.com/docs/security)
