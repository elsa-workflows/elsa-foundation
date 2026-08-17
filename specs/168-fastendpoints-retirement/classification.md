# Classification: Final FastEndpoints Retirement

**Required by**: FR-001. **Gates**: FR-002 — nothing may be removed unless classified `Remove` or
`Archive`. **Evidence bar**: framework constitution §2.25.3 — the scan below found candidates and
authorizes nothing.

**Scan result**: 1640 hit lines across 360 files (`src/`, `tests/`, `tools/`, `docker/`, `docs/`,
`specs/`, excluding `obj/` and `bin/`).

## The finding that reshaped this unit

The spec estimated ~46 referencing test files and recorded that no `src/` project consumes
`Elsa.Api.FastEndpoints`. Both statements are true and both were misleading, in the exact way
§2.25.3 predicts a census will mislead.

**28 test-only endpoint types across 15 files derive from Elsa's FastEndpoints abstractions.** Only
four of those files are the coexistence oracles the maintainer decided to delete. The rest are
authorization and contract guards, and two of them are load-bearing in ways that make naive removal
unacceptable:

- **`tests/Elsa/Foundation/Identity/Tests/Api/PermissionEndpointAdapterIntegrationTests.cs`** defines
  six endpoints (`FastSinglePermissionEndpoint`, `FastAnyPermissionEndpoint`,
  `FastAllPermissionEndpoint`, `FastImpliedPermissionEndpoint`, `FastWildcardPermissionEndpoint`,
  `FastUnrelatedPolicyEndpoint`) that exercise Foundation Identity permission evaluation. FR-006 and
  the #1376 checklist both require preserving exactly this.
- **`tests/Elsa/Api/Compatibility/Testing/Tests/FastEndpointsTransitionTests.cs`** — the retirement
  guard — declares nested `SharedRouteEndpoint<TRequest> : ElsaEndpoint<TRequest>` fixtures. Deleting
  the abstractions breaks the guard that proves the retirement worked.

The reason these matter is what the abstractions actually are. `ElsaEndpointPermissions` documents
itself as "the single owner of Elsa's endpoint permission composition": it applies the wildcard-plus-
action OR rule through one canonical `Any` policy, because passing separate policy names would make
FastEndpoints compose them as AND. The guards call `ConfigurePermissions()` and assert on the
resulting policy. Re-pointing them at raw FastEndpoints bases and re-implementing the composition in
test code would leave them asserting against a copy of the rule instead of the rule, which is a
weakened assertion dressed as a passing test.

## Dispositions

### Remove

| Reference | Kind | Reason |
|---|---|---|
| `src/Elsa/Api/FastEndpoints/Abstractions/ElsaEndpoint*.cs` (6 base classes) | code | First-party FastEndpoints endpoint bases; no production consumer. The retirement's headline target. |
| `src/Elsa/Api/FastEndpoints/Configurators/*` (4) | code | Configure first-party FastEndpoints hosting; nothing first-party is hosted that way. |
| `src/Elsa/Api/FastEndpoints/{FastEndpointsFeatureBase,ApiSecurityFeature}.cs`, `Filters/`, `Contracts/`, `Options/` | code | First-party feature and filter plumbing for a framework no first-party code uses. |
| `src/Elsa/Api/FastEndpoints/Sse/`, `Extensions/ServerSentEventResponseExtensions.cs` | code | SSE helpers for first-party FastEndpoints endpoints; the OpenTelemetry owner already has its own writer. |
| `tests/Elsa/Api/FastEndpoints/Tests/**` | code | Sole purpose is testing the infrastructure above. |
| `tests/Elsa/Studio/Preferences/Tests/StudioPreferencesApiCoexistenceTests.cs` | code | Coexistence oracle; maintainer decision. §2.25.2 deviation recorded. |
| `tests/Elsa/Secrets/Tests/SecretsApiCoexistenceTests.cs` | code | Coexistence oracle; maintainer decision. |
| `tests/Elsa/Diagnostics/StructuredLogs/Tests/StructuredLogsApiCoexistenceTests.cs` | code | Coexistence oracle; maintainer decision. |
| `tests/Elsa/Architecture/Wave2MixedHostCoexistenceTests.cs` | code | Coexistence oracle; maintainer decision. |
| `docker/compose/elsa-workbench.shells.json` → `FastEndpoints` entry | configuration | Enables a feature the source compositions no longer enable; becomes a broken reference once the package goes. |

### Re-anchor

These keep their assertions and lose their dependency on the removed first-party bases. The
permission-composition rule moves to a FastEndpoints-independent home so the guards keep asserting
the production rule rather than a test-local copy.

| Reference | Kind | Reason |
|---|---|---|
| `src/Elsa/Api/FastEndpoints/Abstractions/ElsaEndpointPermissions.cs` | code | Owns the Foundation Identity permission composition rule (#414). The rule is not FastEndpoints-specific: `ComposePolicy` returns a policy string and `StandardMetadata` returns an ASP.NET Core `Action<RouteHandlerBuilder>`. Relocate rather than delete. |
| `src/Elsa/Api/FastEndpoints/Constants/PermissionNames.cs` | code | Supplies the wildcard permission the rule composes; follows the rule to its new home. |
| `tests/Elsa/Foundation/Identity/Tests/Api/PermissionEndpointAdapterIntegrationTests.cs` (6 endpoints) | code | **Guards Foundation Identity permission evaluation** — single, any, all, implied, wildcard, and unrelated-policy paths. FR-006 requires preserving this. |
| `tests/Elsa/Api/Compatibility/Testing/Tests/FastEndpointsTransitionTests.cs` fixtures | code | **The retirement guard.** Its fixtures exist to prove the validator detects shared-route violations; they are discovery fodder and re-anchor cleanly. |
| `tests/Elsa/Foundation/Identity/Tests/Api/EnabledShellCompositionTests.cs` | code | Guards that a secured endpoint composes correctly in an enabled shell. |
| `tests/Elsa/Foundation/Identity/AspNetCoreIdentity/Groundwork/Tests/AspNetCoreIdentityHighestSeamTests.cs` | code | Guards the highest Identity seam against secured endpoints. |
| `tests/Elsa/Activities/Design/Tests/Api/Support/ActivitiesDesignAuthorizationHost.cs` + its integration test | code | Asserts both authoring models publish standard permission metadata. Uses `Assert.Single` with a predicate, so it fails loudly rather than going vacuous if no FastEndpoints endpoint exists. |
| `tests/Elsa/Workflows/Publishing/Api/Tests/PublishingAuthorizationIntegrationTests.cs` | code | Publishing authorization guard. |
| `tests/Elsa/Workflows/Design/Api/Tests/WorkflowsDesignApiContractTests.cs` | code | Design contract guard with a retained canary. |
| `tests/Elsa/Architecture/Wave1AuthorizationIntegrationTests.cs` | code | Wave 1 authorization guard. |
| `tests/Elsa/Architecture/Wave9RuntimeAuthorizationIntegrationTests.cs` | code | Wave 9 runtime authorization guard. |
| `tests/Elsa/Architecture/Wave4AgentFastEndpointsBaselineTests.cs` | code | Agent baseline guard. |
| `tests/Elsa/Diagnostics/OpenTelemetry/Tests/OpenTelemetryAuthorizationTests.cs` | code | OpenTelemetry authorization guard. |

**Re-anchor target**: a test-local FastEndpoints base in the test support assembly, deriving from the
third-party `EndpointWithoutRequest<T>` / `Endpoint<TReq,TRes>` and delegating to the relocated
permission-composition rule. This keeps the guards asserting production behavior, removes the
*first-party* bases as the checklist requires, and leaves third-party FastEndpoints usage confined to
tests — which is precisely the retained coexistence surface.

**Side effect worth noting**: this partially closes the §2.25.2 gap. After re-anchoring, several
guards still prove that a FastEndpoints endpoint built on the third-party base receives correct
Foundation Identity permissions beside first-party Minimal APIs. The four named oracles still go, but
mixed-host coexistence does not become entirely unguarded.

### Preserve

| Reference | Kind | Reason |
|---|---|---|
| `src/Elsa/Api/AspNetCore/EndpointAuthoringMetadata.cs` incl. `EndpointAuthoringModels.FastEndpoints` | code | The typed authoring/ownership metadata the checklist requires preserving; asserted by ~28 test files. The `FastEndpoints` constant still describes a real authoring model available to third parties. |
| `tests/Elsa/Api/Compatibility/Testing/Transitions/TransitionExceptionValidator.cs` | code | The mechanism proving the first-party registration surface is empty. Retiring it would delete the proof of this unit's own claim (R-004). |
| `tests/Elsa/Architecture/Baselines/fastendpoints-transition-exceptions.json` | data | The `[]` baseline the guard reconciles against. |
| `tests/Elsa/Architecture/{EndpointSecurityTests,PermissionAuthorizationBoundaryTests}.cs` | code | Endpoint security and permission-boundary guards; FR-006. |
| `specs/**` except this unit, `docs/reports/**` | prose | **Historical record.** These describe what happened during the program. They are not stale prose; rewriting them would falsify the record. This blanket rule covers the large majority of the 360 files. |
| `docs/adr/0068-*.md` | prose | The accepted decision this unit completes. |

### Archive

| Reference | Kind | Reason |
|---|---|---|
| `tests/**/Baselines/*fastendpoints*.json` | data | Frozen wire evidence; inert, no dependency cost, retains investigative value. Retained as archived records, no longer regenerated. |
| `tools/compatibility/{Runtime,WorkflowsDesign}FastEndpointsCapture` | code | Last first-party compile-time consumers of FastEndpoints. Decision executed in Phase 6; if retained, that is a reportable finding because it keeps the dependency alive. |
| `tests/**/*BeforeCapture.csproj` | code | Regenerators for the frozen baselines; same reasoning. |

### Prose sweep (stale after removal)

| Reference | Kind | Reason |
|---|---|---|
| 10 files under `src/Elsa/Activities/{ControlFlow,Flowchart,Sequence}/Internal/*StructureHandler.cs` | prose | Each carries "strings from the global FastEndpoints options; nested structure payload reads must match." After retirement the options come from ASP.NET Core configuration, not FastEndpoints. This is the Wave 8 defect class, ten times over. |
| `src/Elsa/Foundation/Identity/.../IdentitySeeder.cs` | prose | Mirrors `Elsa.Api.FastEndpoints.Constants.PermissionNames.All` as a literal and names the type in a comment (FR-009). |
| `src/Elsa/Primitives/.../EntityNotFoundException.cs` | prose | "The FastEndpoints handler base classes..." describes removed bases. |
| `src/Elsa/Diagnostics/OpenTelemetry/Endpoints/OpenTelemetrySseStreamWriter.cs` | prose | "avoids a FastEndpoints runtime dependency" — check whether still accurate. |
| `src/Elsa/Workflows/Runtime/Extensions/RuntimeCoreServiceCollectionExtensions.cs` | prose | "without the FastEndpoints API feature" — stale after Wave 9. |
| `src/Elsa/{Secrets,Studio/Preferences}/Api/README.md`, `src/Elsa/Diagnostics/StructuredLogs/README.md` | prose | Module READMEs describing current state; verify against post-removal reality. |
| `src/Apps/Elsa.Foundation.Host/{appsettings.json,Program.cs,*.csproj}` | configuration | Allowlist and host wiring; reconcile against what the host loads. |

## Open at classification time

`Unresolved`: none as categories. The one deliberately deferred question is R-005 — whether each
surviving `CShells.FastEndpoints` package reference can be dropped — which T030 resolves by build
evidence rather than assumption. Under the re-anchor design the answer is likely "retained in the
test projects that host third-party endpoints", but that is stated as an expectation, not a finding.
