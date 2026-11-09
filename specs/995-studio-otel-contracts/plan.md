# Implementation Plan: Studio OpenTelemetry Trace Inspection

**Branch**: `agent/codex/3131-otel-contracts` | **Date**: 2026-10-21 | **Spec**: [spec.md](spec.md)

**Input**: Approved feature specification in `specs/995-studio-otel-contracts/spec.md` for issue #3131.

## Summary

Align the existing Studio OpenTelemetry page with the read routes and nested response shapes already provided by Workbench. Keep the current authenticated backend client and diagnostics-read permission, render only data the API returns, distinguish empty/denied/unavailable/failure states, and prove the normal Workbench engine bridge produces a disposable workflow trace that Studio can find and inspect. No backend, SDK, proxy, provider, ingestion, or permission change is in scope.

## Technical Context

**Language/Version**: TypeScript/TSX in the existing Studio module; the server-side contract is already implemented in C# and is not changed.

**Primary Dependencies**: Existing Studio module API and `moduleApi.backend.http`; existing OpenTelemetry read endpoints and JSON contracts; existing React/Vitest workspace. No new dependency.

**Storage**: Existing host OpenTelemetry stores and their existing storage/resource search endpoints. No schema, provider, or storage change.

**Testing**: First record the untouched module-suite result, then add backend-shaped behavior regressions and confirm the new assertions fail before product edits. Run the focused Vitest suite, Studio workspace typecheck/lint/build and module build, existing OpenTelemetry authorization TestServer suites, and a separately scoped normal-host browser proof. Root owns shared-machine heavy checks and final integration qualification.

**Target Platform**: The existing authenticated Elsa Studio SPA backed by a normal Elsa Workbench host in Development.

**Project Type**: Existing modular web application and client feature module in the Foundation monorepo.

**Performance Goals**: No new performance target. Keep list loading bounded; do not add per-row trace-detail requests.

**Constraints**: Preserve the existing endpoint, DTO, SDK, host composition, sign-in, and `diagnostics/opentelemetry:read` rules. Keep Metrics and OpenTelemetry Logs unsupported. Do not expose the nested detail response's `logs` in UI or export. Do not fabricate trace/error totals, successful zero counts after failures, or resolved service identities for unknown resources. Do not introduce a dependency on issue #3132.

**Scale/Scope**: One existing OpenTelemetry Studio page and its existing module tests, plus focused normal-host proof. No new route, module, package, or data collection path.

## Constitution Check

*Gate: evaluate before research and after design. Both constitutions and the review checklist were read.*

### Before Phase 0

- **Pass — framework §§2.1, 2.2, 2.5, 2.6, 2.7, 2.14; Elsa §E2.2.3**: This is a client-page contract correction in the existing module. It adds no domain type, public API, feature dependency, adapter, module, package, or deployment shape. The page continues to use the host-supplied backend HTTP client.
- **Pass — framework §§2.21, 2.23.1, 2.23.2**: The change is presentation/mapping behavior. Test the actual response and failure boundaries through the module, add tests only for behavior at risk, and avoid source-text or implementation-mirror assertions.
- **Pass — framework §2.23.4**: Tests will return endpoint-shaped fixtures through the existing client seam; they will not reproduce backend behavior in test-only production substitutes.
- **Pass — Elsa §§E2.6, E5, E6**: Runtime execution, workflow state, package surface, and Elsa-owned type names remain unchanged.
- **No violation or complexity exception identified.** Framework §2.24 is draft and Elsa §E2.9 is provisional; neither is relied upon as a ratified gate or design authority for this client correction.

### After Phase 1 design

- **Pass**: The design retains all existing backend routes and auth, introduces no public or persisted model, and limits view projections to the trace/span/resource fields the page supports. The separate TestServer denial proof and rendered 403 state preserve the current boundary.
- **Pass**: A normal-host run uses the already-composed engine bridge and normal supplied modules; it does not introduce external telemetry providers or manual trace injection. The architecture suite/maps remain in root's final shared qualification lane.
- **No violation or complexity exception identified.**

## Project Structure

### Documentation (this feature)

```text
specs/995-studio-otel-contracts/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   └── backend-read-contract.md
└── tasks.md                 # not created during planning
```

### Source Code (repository root)

```text
Extensions/Diagnostics.OpenTelemetry/studio/Elsa.Studio.Diagnostics.OpenTelemetry/Client/
├── src/module.tsx
├── src/styles.css
└── src/__tests__/module.test.ts

Essentials/tests/Diagnostics/OpenTelemetry/Tests/
└── Elsa.Diagnostics.OpenTelemetry.Tests.csproj
```

**Structure Decision**: Keep the page and its behavior tests in the existing OpenTelemetry Studio client module. Use the existing backend contract and current OpenTelemetry authorization test project as-is. No backend source, Studio shared SDK, host composition, or project structure is added or changed.

## Complexity Tracking

No constitution gate is violated; no complexity exception is required.

## Root Planning Review

Root read all five planning artifacts and checked their supported enums, nested response fields, test-project names, package scripts, source paths, and private-host guard assumptions against the controlled checkout. The plan preserves all three issue acceptance criteria, existing trace/span export, and the unsupported Logs boundary. Source references and serial-build commands were corrected, and the canonical private-root prefix/purpose remains compatible with its cleanup guard. Approved for task generation only; product/test edits require a separately reviewed task checkpoint. No execution or runtime acceptance is claimed here.


## Root implementation refinement

Source review retains the approved mapping/state boundary and includes the existing module stylesheet for narrowly related presentation repairs: four page sections receive four grid rows, the two stored-count cards fill two columns, trace name/ID use separate lines, wrapping filters no longer rely on a fixed toolbar height, and detail/source regions can scroll within available height. Long source identifiers wrap. This is the existing page's layout, without a new component pattern, module reference or dependency. Root will inspect actual normal-host 1440px light and 1280px dark screenshots and the supported trace/filter journey before accepting presentation. The same review preserves known resource IDs when service metadata is blank, and the approved all-unknown service fallback includes its supplied resource-ID count.
