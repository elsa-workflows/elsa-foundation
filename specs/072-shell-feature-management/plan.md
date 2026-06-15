# Implementation Plan: Shell Feature Management

**Branch**: `codex/shell-feature-management` | **Date**: 2026-06-15 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `specs/072-shell-feature-management/spec.md`

## Summary

Add backend feature-management APIs under `Elsa.Modularity` and a Studio `FeatureManagement` module. The backend owns shell inference, catalog merging, JSON-backed shell configuration mutation, feature catalog refresh, and shell reload. Studio contributes a route and modular setting-editor registry, stages edits locally, then applies the desired state in one request.

## Technical Context

**Language/Version**: C# net10.0 and React/TypeScript.

**Primary Dependencies**: CShells, FastEndpoints, Nuplane admin APIs, Elsa Studio SDK, Vite.

**Storage**: JSON shell configuration (`shells.json`) for v1.

**Testing**: xUnit for backend; Vitest/jsdom for Studio module and SDK tests.

**Target Platform**: Elsa server shell features and browser-based Elsa Studio.

**Project Type**: Paired backend feature modules plus frontend Studio module.

**Constraints**: Studio must not know or send shell IDs; configuration/settings constitution sections remain deferred.

## Constitution Check

| Gate | Status | Notes |
|---|---|---|
| Three-layer separation | PASS | Contracts in `Elsa.Modularity.Core`; Nuplane-specific discovery in `Elsa.Modularity.Nuplane`; HTTP API in `Elsa.Modularity.Api`. |
| Feature identity | PASS | Uses stable CShells feature names and existing manifest metadata. |
| Provider decomposition | PASS | Nuplane/package discovery stays outside Core. |
| Unit tests | PASS | Registration tests and behavior tests are required for new feature classes and services. |
| Configuration classification deferred | PASS | Consumes current manifest metadata; does not ratify new setting taxonomy. |

## Project Structure

```text
src/elsa/Modularity/Core/
src/elsa/Modularity/Nuplane/
src/elsa/Modularity/Api/
tests/Elsa/Modularity/Tests/

/Users/sipke/Projects/Elsa/elsa-foundation-studio/src/Elsa.Studio.FeatureManagement/
/Users/sipke/Projects/Elsa/elsa-foundation-studio/src/Elsa.Studio.Web/Client/src/sdk/
/Users/sipke/Projects/Elsa/elsa-foundation-studio/tests/Elsa.Studio.Tests/
```

**Structure Decision**: Use the Elsa `Modularity` domain named in the constitution and the existing Studio module manifest pattern.

## Implementation Steps

1. Create Speckit artifacts and branch metadata.
2. Add backend Modularity contracts, DTOs, service interfaces, and feature options.
3. Add Nuplane-backed feature catalog discovery and manifest parsing.
4. Add JSON shell configuration store, revision generation, and apply behavior.
5. Add Modularity API endpoints and feature registration.
6. Add backend tests and extension-point documentation.
7. Wire the Elsa server host and refresh generated maps.
8. Add Studio SDK setting-editor registry and built-in editor helpers.
9. Add Studio FeatureManagement module, manifest contribution, client UI, styles, and tests.
10. Validate focused backend and Studio build/test targets.

## Risks

- CShells currently exposes runtime feature catalog refresh through an implementation type; isolate any reflection fallback behind a Modularity service so the API surface remains replaceable.
- JSON shell mutation is v1-specific; keep it behind an interface so future non-file-backed shell stores can replace it.
- Frontend editor extensibility should be generic enough for later modules without overfitting to this page.
