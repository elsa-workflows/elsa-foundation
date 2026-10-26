# Implementation Plan: Studio auth and management bridge

**Branch**: `agent/codex/3358-studio-auth-bridge` · **Date**: 2026-10-26

**Spec**: [spec.md](spec.md) · **Issue**: [#3358](https://github.com/elsa-workflows/elsa-foundation/issues/3358)

## Summary and technical context

Move Web's existing447-line bearer/permission/cache implementation and137-line backend bridge into
`Studio/src/essentials/Elsa.Studio.Hosting`. Common Add/Map calls compose them. Reuse Api authorization,
backend options/status/read-client contracts and ASP.NET Core authentication/HttpClientFactory. No new
package, persistence model, identity implementation or Nuplane dependency is required. .NET10/C#, existing
xUnit/TestServer/recording backend/fake clock tests; no frontend change or new test project.

## Constitution check

Pre/post-design checks against both constitutions and ADR0037 pass:

- Framework §§2.1/2.6/2.8: use public Api/CShells contracts, no backend implementation/cluster dependency.
- §§2.2/2.23.3: retain private handlers/cache/client/DTOs; no production friend. Extend the existing public
  authorization owner with stable coarse/read/manage names and permission keys. Track public Api surface.
- §§2.21/2.23: preserve existing HTTP auth/bridge cases through public composition, add ownership/reload and
  installed-posture tests, use causal controls for security/lifetime guards. No mandatory cadence is declared.
- §2.25: remove both Web implementation files and copied Program/adapter policy wiring. Collapse redundant
  private registration glue into its owner instead of introducing a public service facade.
- Elsa §E5: existing Studio version/pack metadata; no release/publisher changes.
- No provisional pattern or new exception is needed.

## Design

AddElsaStudio registers bridge services from the installed settings snapshot after loading host shell
configuration. Use the same frozen AuthEnabled and copied trusted prefixes as runtime JS. Existing Studio
configuration keys retain their meanings; configure them before Add. Caller HTTP handlers and TimeProvider
remain customizable through ordinary public DI/HttpClientFactory APIs before Build.

MapElsaStudio maps the existing status/registry bridge routes, retaining JSON and permission semantics. Web
removes manual auth/client/map calls. Its distribution-owned Console Stream path remains an explicit hosting
option and its root hub uses the public Api policy constant. Optional Web adapters likewise reference Api
policy/permission constants, never Hosting's internals.

Keep named scheme, disabled-demo posture, header/query precedence, segment-aware query restrictions,
401/403, manage/read implication, cache TTL/cap/hash/definitive-outcome behavior and backend mappings.
Privileged key attachment stays in the reused Api read-client path; session introspection never receives it.
An unavailable key/URL makes zero privileged calls, distinct from ordinary session validation. Do not add
single-flight/revocation machinery or change backend endpoint ownership.

Use CShells ShareSingletonWithShells for the private session cache, TimeProvider, backend options,
a private StudioSessionTransport holding the root HTTP factory. Preserve normal authorization-handler enumeration;
its immutable permission handler remains the existing instance registration. Borrow root ownership rather
than cloning factories or retaining request providers.

Tests replace private named-client/type dependencies with public IHttpMessageHandlerBuilderFilter and
public authorization/HTTP composition. The missing-permission-handler test can select the public generic
AuthorizationHandler<StudioBridgePermissionRequirement> base of its instance or restore default authorization
handlers after removing custom handlers; do not expose the implementation merely for that setup. Internal
registry DTO assertions become JSON/wire assertions. The mandatory Api feature restores AddAuthenticationCore/AddAuthorization locally, respecting CShells
provider exclusions. Root/shell ownership coverage uses a tracked clock,
public IAuthenticationService calls inside a shell scope and recorded introspection counts across reload.

## Structure and validation

- `Studio/src/essentials/Elsa.Studio.Hosting/{StudioBridgeAuth,StudioBackendManagementBridge}.cs`:
  private moved behavior, coherent registration and root borrowing.
- `Studio/src/essentials/Elsa.Studio.Hosting/StudioHostingExtensions.cs`: common registration/mapping.
- `Studio/src/essentials/Elsa.Studio.Api/Authorization/StudioBridgeAuthorization.cs`: public stable constants;
  public tracking files/analyzer configuration follow the package's actual surface.
- `Studio/src/apps/Elsa.Studio.Web/`: remove moved files, simplify Program, redirect policy users to Api.
- `Studio/tests/Elsa.Studio.Tests/`: migrate fixtures/cases to public APIs; add lifecycle/security controls.
- README and canonical hosting contract: actual ownership/configuration/override boundary, no duplicated domain glossary.

Run whole Studio tests and reconcile discovery/theory counts; preserve all existing risk cases. Qualify root
cache/clock/factory ownership through shell reload and demonstrate causal controls. Run Api packed dependency
consumer, actual isolated Web smoke, architecture guards/maps, package inventory and affected locked restores.
Post exact-head evidence, independent review, required CI and resulting-main proof. Final browser/optional/
full package-only journeys remain their existing downstream leaves.
