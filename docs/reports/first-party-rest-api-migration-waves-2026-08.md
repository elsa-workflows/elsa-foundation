# First-party REST API migration waves

Status: reviewed delivery plan for [program #1342](https://github.com/elsa-workflows/elsa-foundation/issues/1342)
and planning issue [#1350](https://github.com/elsa-workflows/elsa-foundation/issues/1350).

This report turns the accepted Minimal API architecture into bounded executable work. It does not
authorize a repository-wide migration pull request. Each wave must preserve a deployable mixed host,
freeze its own FastEndpoints-before evidence, and land through a separate issue and pull request.

## Inventory rule and ratchet gap

A **concrete registration** is a concrete first-party endpoint class that either overrides
`Configure()` or inherits route configuration from a first-party abstract FastEndpoints base. The
count is by registration type, not by source file or HTTP method. Abstract bases are not registrations.

Current `main` contains **164 concrete FastEndpoints registrations across 18 owner assemblies**:

- 162 production `Configure()` overrides;
- minus the abstract `OtlpIngestionEndpointBase`;
- plus its three concrete traces, metrics, and logs endpoints.

Source searches must include ignored directories (`rg -uuu`), because the OpenTelemetry `Logs/`
directory is otherwise skipped by default ignore rules.

The committed transition baseline contains only **91** entries. Its scanner recognizes direct base
names such as `Endpoint*` and `ElsaEndpoint*`, but does not resolve inheritance through local endpoint
bases. It also records the abstract OTLP base instead of all three concrete signals. The resulting
net undercount is **73 registrations**:

| Owner | Concrete | Current baseline | Gap |
|---|---:|---:|---:|
| `Elsa.Activities.Design.Api` | 38 | 2 | 36 |
| `Elsa.Workflows.Design.Api` | 27 | 11 | 16 |
| `Elsa.Workflows.Runtime.Api` | 24 | 9 | 15 |
| `Elsa.Workflows.Publishing.Api` | 23 | 19 | 4 |
| `Elsa.Diagnostics.OpenTelemetry` | 11 | 9 | 2 |
| All other owners | 41 | 41 | 0 |
| **Total** | **164** | **91** | **73** |

The baseline is useful as a transitional allowlist, but it cannot be the zero-registration retirement
gate until Wave 0 makes discovery semantic/transitive, rejects abstract types, expands inherited
route configuration, and ratchets all 164 registrations.

## Complete owner assignment

| Wave | Owner assembly | Registrations | Contract or risk emphasis |
|---:|---|---:|---|
| 1 | `Elsa.Api.Capabilities` | 1 | preserve `ApiCapabilitiesRead`, response/OpenAPI parity |
| 1 | `Elsa.Attention.Api` | 1 | authenticated query and paging |
| 1 | `Elsa.Expressions.Api` | 2 | descriptor JSON and permission catalog |
| 1 | `Elsa.Expressions.JavaScript.Rendering` | 1 | request body and rendered response |
| 1 | `Elsa.Workflows.Runtime.JavaScript` | 1 | activity execution input/output |
| 1 | `Elsa.Workflows.Dashboard` | 2 | aggregate read projections |
| 2 | `Elsa.Activities.Bpmn.Interchange` | 3 | XML upload/download and diagnostics |
| 2 | `Elsa.Modularity.Api` | 2 | list/apply and wildcard replacement |
| 2 | `Elsa.Workflows.ExecutionEvidence` | 3 | read/delete, correlation, tenant isolation |
| 2 | `Elsa3.Activities.Design.Import` | 5 | analyze/expand/apply import workflow |
| 3 | `Elsa.Foundation.Identity.Api` | 7 | token/session/challenge/logout protocol behavior |
| 3 | `Elsa.Foundation.Identity.AspNetCoreIdentity` | 2 | login page/form redirects and cookies |
| 4 | `Elsa.Agent.Api` | 11 | session lifecycle, proposal commands, SSE |
| 5 | `Elsa.Diagnostics.OpenTelemetry` | 11 | query APIs, SSE, OTLP protobuf/authentication |
| 6 | `Elsa.Workflows.Design.Api` | 27 | workflow authoring, drafts, versions, schema/tooling |
| 7 | `Elsa.Activities.Design.Api` | 38 | reusable authoring, upgrades, lifecycle, catalog |
| 8 | `Elsa.Workflows.Publishing.Api` | 23 | preflight, policy, publication slots, test runs |
| 9 | `Elsa.Workflows.Runtime.Api` | 24 | execution, instances, incidents, diagnostics, alterations |
|  | **Total** | **164** |  |

All 18 owner assemblies have a **required** unloadability exit gate. The transition registry's
current `dynamicallyUnloadable: false` values document the temporary FastEndpoints exception, not
the migration target. Each child issue must replace that exception with repeated collectible-
`AssemblyLoadContext` evidence for its exact owners across routing, DI, serialization, and disposal.
An owner may leave that gate only through a separately reviewed, owner-specific exception; a
wave-level generic caveat is not sufficient.

## Approved non-FastEndpoints dispositions

These surfaces count toward the program inventory but are not migration work:

| Surface | Owner / count | Security and metadata disposition | Work unit / unloadability |
|---|---|---|---|
| Studio Preferences, Secrets, Structured Logs | Three Elsa modules / 2 + 10 + 3 routes | Retain the explicit Minimal API mappers and their reviewed permission/public metadata. | Already proven; unloadability required and covered by their compatibility/collectibility evidence. |
| OpenTelemetry OTLP Minimal API mapper | `Elsa.Diagnostics.OpenTelemetry` / 3 signal routes | Retain for plain/root ASP.NET Core hosts with OTLP authentication and ownership metadata. | Wave 5 removes the parallel shell FastEndpoints registrations, prevents double mapping, and supplies owner unload evidence. |
| Extension Builder | `Elsa.Modularity.ExtensionBuilder` hosted by Workbench / 42 routes | Retain Minimal APIs. Preserve the management-key filter and trusted-caller boundary; add inspectable host ownership and host-credential/named-policy disposition metadata. | Retained-host metadata work unit; root-hosted, so unloadability is not applicable. |
| Workbench module management | `Elsa.Workbench` / 9 routes | Retain Minimal APIs and the server-side management-key boundary; add host ownership and host-credential disposition metadata. | Retained-host metadata work unit; root-hosted, so unloadability is not applicable. |
| Foundation Host module management | `Elsa.Foundation.Host` / 2 routes | Retain Minimal APIs and the constant-time API-key filter; add host ownership and host-credential disposition metadata. | Retained-host metadata work unit; root-hosted, so unloadability is not applicable. |
| Workbench root and readiness | `Elsa.Workbench` / 1 root + 2 health routes | Retain intentionally public routes with typed host ownership and public category/reason metadata. | Retained-host metadata work unit; root-hosted, so unloadability is not applicable. |
| Foundation Host health | `Elsa.Foundation.Host` / 2 routes | Retain intentionally public health routes with typed host ownership and public category/reason metadata. | Retained-host metadata work unit; root-hosted, so unloadability is not applicable. |
| Workbench console-log streaming | `Elsa.Workbench` plus `ConsoleLogStreaming.AspNetCore` / 2 HTTP routes + 1 SignalR hub | Retain root mapping and default-policy authorization; add inspectable host ownership, named-policy disposition, streaming content metadata, and contract fixtures. | Retained-host metadata work unit; root-hosted external mapper, so unloadability is not applicable. |
| CShells management API | `CShells.Management.Api` hosted by Workbench / 6 routes | Retain Minimal APIs. The current Workbench call does not chain authorization; the retained-host work unit must restore the ADR 0037 server-side host-credential boundary and add typed host ownership/disposition metadata. | Retained-host metadata work unit; root-hosted external package, so unloadability is not applicable. |
| CShells generation-owned module endpoints | CShells dynamic endpoint source / runtime count | Retain the standard ASP.NET Core endpoint publication model with shell/generation/feature ownership, collision rejection, exact-generation binding, atomic replacement, and drain evidence delivered by #1345. | Complete in #1345; collectible-context evidence required and present there. |
| Workflow-authored dynamic HTTP routes | `Elsa.Http` route table / unbounded runtime-authored count | Retain the distinct runtime publication model. Every published route must carry typed dynamic-shell ownership (`OwnerId`, `ShellId`, `Generation`) and one explicit security disposition before collision-validated atomic publication. The current `HttpRouteData` does not carry that contract. | Dedicated dynamic-HTTP metadata work unit; unloadability required for the owning shell generation. |
| MVC | Repository-wide / 0 routes | No first-party MVC surface exists. | Approved not-applicable disposition; no work unit. |

The two retained-surface work units are program obligations, not FastEndpoints migrations. The
host-owned issue closes the concrete root security/metadata gaps above. The workflow-authored issue
closes the distinct dynamic-publication gap without forcing runtime-authored routes into a static
module mapper.

## Grouping and sequencing rationale

- Inventory hardening is a universal prerequisite because the current allowlist cannot prove either
  completeness or zero registrations.
- Small/read and bounded CRUD/import owners come first to exercise repeatable multi-owner migration
  without putting the four largest domain APIs in the first broad wave.
- Identity, Agent, and OpenTelemetry remain separate because protocol redirects/cookies, SSE, and
  OTLP authentication/protobuf behavior need different compatibility fixtures.
- Workflows Design precedes Activities Design because reusable activity authoring consumes workflow
  design contracts; Publishing follows both because it integrates their draft and authoring models.
- Runtime has no hard dependency on the Design/Publishing replacements, but is preferred last because
  it owns the largest operational and backend-E2E risk surface.
- Retained-host track H and workflow-dynamic track D may start alongside inventory hardening. After
  Wave 0, two or three non-overlapping migration sessions may proceed concurrently. Preferred
  landing order is a risk-control policy, not an invented technical dependency.

The canonical active backlog, blockers, and issue links belong in the
[program goal](../program-goals/first-party-rest-api-consolidation.md) and the self-contained child
issues. This report remains the audit evidence and rationale for those work units.

The planning issue produced [Wave 0 through Final as linked sub-issues](https://github.com/elsa-workflows/elsa-foundation/issues/1350),
including separate retained-host and workflow-dynamic tracks. The program goal is the canonical
registry for their live issue links, blockers, and unloadability dispositions.

## Retirement evidence

Final retirement requires the semantic source scanner and runtime endpoint manifest to report
**zero first-party FastEndpoints registrations**, including entries previously covered by a reviewed
transition exception. It must also prove green full build, architecture, maps, HTTP/OpenAPI/security,
backend E2E, and collectible-context gates, then remove the framework package/base/discovery/SSE and
transitional-test infrastructure that has no third-party compatibility purpose.

## Decision record

The program recommendation remains **migrate with bounded coexistence**. Minimal APIs are the
consistent first-party REST authoring model; Foundation Identity owns permission evaluation; and
FastEndpoints remains supported only as temporary migration infrastructure until the final issue
lands. No new ADR is required because [ADR 0068](../adr/0068-first-party-rest-apis-use-aspnet-core-minimal-apis.md)
already records that architectural default.
