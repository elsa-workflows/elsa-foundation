# Feature Specification: Isolated Studio Package Consumer Proof

**Feature Branch**: `agent/codex/3363-studio-package-consumer-proof`
**Created**: 2026-10-27
**Status**: Implemented — staged in PR3391; exact-head delivery and resulting-main verification pending.
**Input**: Program [#3340](https://github.com/elsa-workflows/elsa-foundation/issues/3340), task [#3363](https://github.com/elsa-workflows/elsa-foundation/issues/3363); accepted [hosting contract](../../docs/reports/studio-hosting-contract.md), completed #3361/#3367 and the production browser qualification in [spec1012](../1012-studio-browser-assets/spec.md).

## User Scenarios & Testing

### User Story 1 — Consume the standard Studio from packages (Priority: P1)

A developer can restore, publish and run a small host from genuine candidate packages without copying Web infrastructure, reaching the source checkout or running frontend tools in the consumer.

**Independent Test**: A fresh external consumer starts from published files, serves the actual shell, discovery, runtime configuration and deep links, and renders its selected module.

**Acceptance Scenarios**:

1. **Given** the candidate package set and empty consumer cache, **when** the developer restores and publishes, **then** declared transitive dependencies supply all required libraries and the resulting graph contains package references only.
2. **Given** only the published application, **when** it serves the browser, **then** the shell, selected module, shared imports, discovery and deep links work with no missing resources or frontend-tool invocation.
3. **Given** a supported custom document or frontend opt-out, **when** the application is published, **then** the documented customization and remaining server/module behavior are preserved.

### User Story 2 — Detect broken package delivery (Priority: P1)

A maintainer can show that the qualification rejects a missing shell resource or required dependency declaration, and succeeds after restoring the original inputs. A broken network connection or unrelated build failure cannot stand in for the intended control.

**Independent Test**: Normal, deliberately broken and restored profiles have success, specific failure and success outcomes, each from an isolated consumer/cache.

**Acceptance Scenarios**:

1. **Given** a successful published shell, **when** a required published resource is omitted, **then** the proof fails for that missing resource; restored production bytes pass.
2. **Given** genuine packages that pass together, **when** a required dependency declaration is omitted in a private candidate copy while its dependency artifact remains available, **then** a fresh consumer fails specifically because that declared dependency is absent; the original package passes again.
3. **Given** a failure unrelated to the selected omission, **when** results are examined, **then** it remains an unsuccessful control and is never reported as qualification success.

### Edge Cases

- Multiple versions or missing candidate identities must fail clearly instead of choosing an arbitrary package.
- An already missing dependency declaration cannot be claimed as a newly applied control.
- A control must not modify original artifacts or misrepresent a signed archive after mutation.
- Each run uses its own cache, feed and output so a prior successful restore cannot mask a missing declaration.
- Reports distinguish package provenance, consumer failure, intentional controls and restored success; aborted checks are not passes.

## Requirements

### Functional Requirements

- **FR-001**: Qualification MUST use actual production candidate archives and public package entry points in an external consumer, with no project references or replacement host libraries.
- **FR-002**: Consumer restore and publish MUST use isolated package/HTTP caches, declared feeds and a locked dependency graph.
- **FR-003**: Published execution MUST work without owned consumer source/feed/cache inputs or consumer frontend tools; archive, cache and published library bytes MUST match the supplied candidates.
- **FR-004**: The browser proof MUST cover actual UI/module loading, discovery, runtime configuration, root/deep links, stable resources and their existing cache/range behavior.
- **FR-005**: Both deliberate omission controls MUST produce the intended specific failure, with original artifacts retained and restored positive runs succeeding.
- **FR-006**: Reports MUST retain identities, versions, input changes, hashes, failing stage/diagnostics and runtime boundaries, without calling an expected failure a passing product run.
- **FR-007**: Reuse the production probes from #3361/#3367. Custom-module/addon/auth/live Console Stream combinations remain #3364; CI wiring remains #3365; the developer recipe/exit audit remains #3366.

### Key Entities

- **Candidate artifact**: package identity/version, provenance and archive/library hashes.
- **Consumer profile**: selected packages, isolated inputs, published payload and observed behavior.
- **Control result**: precise omitted input, original/mutated identity, failed stage and restored positive outcome.

## Success Criteria

- **SC-001**: A fresh standard consumer and its supported customization profiles pass with zero consumer frontend-tool invocations and no source/project libraries in the restored graph.
- **SC-002**: Both omission kinds fail specifically and each restored profile succeeds; unrelated failures do not count as valid controls.
- **SC-003**: Original candidate archives remain byte-identical after controls; the dependency-only control leaves library payload bytes unchanged.
- **SC-004**: Maintainers can reproduce these outcomes through documented commands and retained reports without copying product infrastructure.

## Assumptions

- This is follow-through within the owner-authorized Program3340 M3 scope, with no new architecture or product API decision.
- Local/CI candidate feeds are sufficient; public release, deployment and publisher activation are excluded.
- Producer and browser-driver tooling may use the existing frontend toolchain; the consumer may not.
- Risk-based actual artifact controls are the test discipline; no application TDD cadence or performance target is introduced.
