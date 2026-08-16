# First-party REST API inventory evidence

Status: reviewed implementation evidence for [issue #1364](https://github.com/elsa-workflows/elsa-foundation/issues/1364)
and the canonical [first-party REST API migration-wave report](first-party-rest-api-migration-waves-2026-08.md)
from merged planning PR #1377.

The architecture transition guard now discovers FastEndpoints registrations from the source graph,
not from direct base-name matches. It follows local and cross-project endpoint bases transitively,
expands effective inherited `Configure` methods (including an explicit `base.Configure()` call),
omits abstract types, and ignores `bin`/`obj` source paths. Directory enumeration intentionally
includes ignored source folders such as OpenTelemetry `Endpoints/OpenTelemetry/Logs`.

## Ratcheted registration count

The current source baseline is exactly **164 concrete first-party FastEndpoints registrations owned by
18 assemblies**. The count is by concrete registration type, not by source file or HTTP method. The
abstract `OtlpIngestionEndpointBase` is excluded while its concrete traces, metrics, and logs classes
are included; unresolved computed OTLP routes retain an owner-source fingerprint for deterministic
review.

| Owner assembly | Concrete registrations | Preferred removal wave |
|---|---:|---|
| `Elsa.Activities.Bpmn.Interchange` | 3 | #1368 |
| `Elsa.Activities.Design.Api` | 38 | #1373 |
| `Elsa.Agent.Api` | 11 | #1370 |
| `Elsa.Api.Capabilities` | 1 | #1367 |
| `Elsa.Attention.Api` | 1 | #1367 |
| `Elsa.Diagnostics.OpenTelemetry` | 11 | #1371 |
| `Elsa.Expressions.Api` | 2 | #1367 |
| `Elsa.Expressions.JavaScript.Rendering` | 1 | #1367 |
| `Elsa.Foundation.Identity.Api` | 7 | #1369 |
| `Elsa.Foundation.Identity.AspNetCoreIdentity` | 2 | #1369 |
| `Elsa.Modularity.Api` | 2 | #1368 |
| `Elsa.Workflows.Dashboard` | 2 | #1367 |
| `Elsa.Workflows.Design.Api` | 27 | #1372 |
| `Elsa.Workflows.ExecutionEvidence` | 3 | #1368 |
| `Elsa.Workflows.Publishing.Api` | 23 | #1374 |
| `Elsa.Workflows.Runtime.Api` | 24 | #1375 |
| `Elsa.Workflows.Runtime.JavaScript` | 1 | #1367 |
| `Elsa3.Activities.Design.Import` | 5 | #1368 |
| **Total** | **164** | **18 owners** |

The reviewed machine-readable registry is
[`fastendpoints-transition-exceptions.json`](../../tests/Elsa/Architecture/Baselines/fastendpoints-transition-exceptions.json).
Each entry records the concrete registration identity, owner, normalized route/method identities when
statically resolvable, owner-source fingerprint for computed routes, removal owner, and the linked
executable removal wave (#1367–#1375). The architecture test pins both the total and every owner
count, and rejects generic or closed planning follow-ups before reconciling the registry with
deterministic diagnostics.

The previous registry contained 91 entries. The scanner therefore closes the **73-registration gap**
caused by indirect inheritance and the abstract/concrete OTLP distinction:

| Owner | Concrete | Previous registry | Gap |
|---|---:|---:|---:|
| `Elsa.Activities.Design.Api` | 38 | 2 | 36 |
| `Elsa.Workflows.Design.Api` | 27 | 11 | 16 |
| `Elsa.Workflows.Runtime.Api` | 24 | 9 | 15 |
| `Elsa.Workflows.Publishing.Api` | 23 | 19 | 4 |
| `Elsa.Diagnostics.OpenTelemetry` | 11 | 9 | 2 |
| All other owners | 41 | 41 | 0 |
| **Total** | **164** | **91** | **73** |

## Per-owner security, catalog, and unloadability disposition

FastEndpoints registrations are transitional module-owned authoring surfaces. Their route/method and
owner evidence is reconciled by the compatibility scanner. The table below preserves the current
source-level security form and the catalog or explicit-wave gap for every owner. `Catalog-owned` means
the endpoint uses a permission key with an active feature/identity contributor; `catalog gap` means the
current explicit key, policy, or wildcard needs a migration-wave disposition. `Required / FE exception`
records the current registry's `dynamicallyUnloadable: false` transition exception; every executable
owner wave must replace it with collectible-context evidence. Runtime endpoint metadata is not inferred
from this static table.

| Owner assembly | Executable wave | Registrations | Unloadability | Current security form | Catalog / explicit-wave disposition |
|---|---:|---:|---|---|---|
| `Elsa.Activities.Bpmn.Interchange` | #1368 | 3 | Required / FE exception | `PermissionNames.BpmnInterchangeRead/Manage` | Catalog-owned |
| `Elsa.Activities.Design.Api` | #1373 | 38 | Required / FE exception | `PermissionNames.ActivityDesignRead/Manage` | Catalog-owned |
| `Elsa.Agent.Api` | #1370 | 11 | Required / FE exception | Explicit `AgentPermissionKeys.Use/Proposals/Audit` | Catalog gap; wave must add contributor/catalog ownership |
| `Elsa.Api.Capabilities` | #1367 | 1 | Required / FE exception | `PermissionNames.ApiCapabilitiesRead` | Catalog-owned |
| `Elsa.Attention.Api` | #1367 | 1 | Required / FE exception | FastEndpoints wildcard via `ConfigurePermissions()` | Wildcard-only; wave must add an owned action permission |
| `Elsa.Diagnostics.OpenTelemetry` | #1371 | 11 | Required / FE exception | `Diagnostics:OpenTelemetry` policy; OTLP ingestion uses API-key/loopback transport auth with anonymous framework metadata | Policy/transport exception; wave must add catalog ownership and retain OTLP auth |
| `Elsa.Expressions.Api` | #1367 | 2 | Required / FE exception | `PermissionNames.ExpressionsRead` | Catalog-owned |
| `Elsa.Expressions.JavaScript.Rendering` | #1367 | 1 | Required / FE exception | FastEndpoints wildcard via `ConfigurePermissions()` | Wildcard-only; wave must add an owned action permission |
| `Elsa.Foundation.Identity.Api` | #1369 | 7 | Required / FE exception | Public auth protocol routes plus `IdentityProvidersRead` | Mixed: public protocol by design; protected capability is identity-catalog-owned |
| `Elsa.Foundation.Identity.AspNetCoreIdentity` | #1369 | 2 | Required / FE exception | Public login page/form protocol routes | Explicit public protocol; no catalog permission by design |
| `Elsa.Modularity.Api` | #1368 | 2 | Required / FE exception | FastEndpoints wildcard via `ConfigurePermissions()` | Module-management contributor exists; wave must bind explicit owned read/manage permissions |
| `Elsa.Workflows.Dashboard` | #1367 | 2 | Required / FE exception | `WorkflowsDashboardPermissions.Read` | Feature-contributor catalog-owned |
| `Elsa.Workflows.Design.Api` | #1372 | 27 | Required / FE exception | `PermissionNames.WorkflowDesignRead/Manage` | Catalog-owned |
| `Elsa.Workflows.ExecutionEvidence` | #1368 | 3 | Required / FE exception | FastEndpoints wildcard via `ConfigurePermissions()` | Wildcard-only; wave must add catalog-owned read/delete/manage permissions |
| `Elsa.Workflows.Publishing.Api` | #1374 | 23 | Required / FE exception | `PermissionNames.WorkflowPublishingRead/Manage` | Catalog-owned |
| `Elsa.Workflows.Runtime.Api` | #1375 | 24 | Required / FE exception | `PermissionNames.WorkflowRuntimeRead/Manage/Execute` plus publishing read | Catalog-owned |
| `Elsa.Workflows.Runtime.JavaScript` | #1367 | 1 | Required / FE exception | FastEndpoints wildcard via `ConfigurePermissions()` | Wildcard-only; wave must add an owned action permission |
| `Elsa3.Activities.Design.Import` | #1368 | 5 | Required / FE exception | `PermissionNames.Elsa3ImportRead/Manage` | Catalog-owned |
| **Total** |  | **164** |  |  | **18 owners; no generic follow-up** |

The reusable gates remain explicit and bounded; they do not claim complete runtime metadata coverage
for all 164 registrations:

- [`EndpointManifestBuilder`](../../tests/Elsa/Api/Compatibility/Testing/Manifests/EndpointManifestBuilder.cs)
  requires one owner, one authoring model, and one security disposition per published endpoint.
- [`EndpointSecurityTests`](../../tests/Elsa/Architecture/EndpointSecurityTests.cs) verifies canonical
  permission declarations and catalog ownership for current first-party management endpoint roots.
- The checked-in [`endpoint-manifest.json`](../../tests/Elsa/Architecture/Baselines/endpoint-manifest.json)
  is an eight-route runtime canary. Each migration wave must add its own before/after manifest and
  security/catalog evidence; this canary is not evidence that all 164 registrations share one active
  host composition.

The canonical planning report records the owner-specific unloadability gates and the retained public,
host-credential, named-policy, CShells, and dynamic-route dispositions. Those retained surfaces are
separate from the 164 FastEndpoints registrations and are listed below for inventory completeness:

The following surfaces are intentionally not FastEndpoints registrations and must not be counted in
the 164-entry migration total:

| Surface | Current route inventory | Disposition | Work unit |
|---|---:|---|---|
| Studio Preferences, Secrets, Structured Logs | 2 + 10 + 3 | Retain explicit Minimal API mappers with reviewed metadata | Completed canaries |
| OpenTelemetry OTLP Minimal API mapper | 3 signal roots | Retain root mapper; remove parallel shell FastEndpoints registrations | #1371 |
| Extension Builder | 42 | Retain host-control routes with server-side credential protection and typed host ownership | #1365 |
| Workbench module management | 9 | Retain host-control routes with server-side credential protection and typed host ownership | #1365 |
| Foundation Host module management | 2 | Retain host-control routes with server-side credential protection and typed host ownership | #1365 |
| Workbench root/readiness and Foundation Host health | 1 root + 4 health | Retain intentionally public host-owned routes with typed public reason metadata | #1365 |
| Workbench console-log HTTP/SignalR surface | 2 HTTP + 1 SignalR | Retain root mapper and named-policy authorization with streaming metadata | #1365 |
| CShells management and generation-owned endpoints | 6 management + dynamic generation-owned | Retain explicit Minimal API publication with host/generation ownership | #1365 / #1345 |
| Workflow-authored dynamic HTTP routes | Dynamic/unbounded | Retain runtime publication model with shell/generation ownership and explicit security disposition | #1366 |
| MVC | 0 | No first-party routes; not applicable | None |

These retained surfaces have separate ownership/security or dynamic-publication obligations. They are
not exceptions to the FastEndpoints count and cannot make a non-zero first-party registration count
acceptable at retirement.

## Retirement gate

`TransitionExceptionValidator.ValidateRetirement` is stricter than the temporary transition registry:
it reports every discovered first-party registration, including entries with a reviewed exception. The
architecture test runs the transitional exact-registry gate by default and supports the explicit
`ELSA_FASTENDPOINTS_RETIREMENT_MODE=1` mode, which passes only when discovery returns zero first-party
registrations. This prevents formerly approved exceptions from silently surviving the final removal
wave.
