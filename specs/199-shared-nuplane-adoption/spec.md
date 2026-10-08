# Feature Specification: Shared Nuplane Adapter Adoption

**Feature Branch**: `2340-shared-nuplane-adoption`
**Created**: 2026-10-09
**Status**: Draft
**Input**: Deliver Foundation #2314 under [Program #2500](https://github.com/elsa-workflows/elsa-foundation/issues/2500), preserving host policies while adopting the reusable upstream integration.

## User Scenarios & Testing

### User Story 1 - Deliver package changes consistently (Priority: P1)

An operator installs or replaces a package and expects each host to discover the new features according to its existing refresh and reload policy.

**Why this priority**: Shared upstream behavior removes duplicate coordination while preserving how deployed hosts apply updates.

**Independent Test**: Deliver package changes to both host profiles and observe catalog freshness and which serving generation changes.

**Acceptance Scenarios**:

1. **Given** Foundation's default configuration and an active shell, **when** an eligible reconciliation completes, **then** the catalog refreshes and automatic reload is attempted for active shells.
2. **Given** Workbench's default configuration and an active shell, **when** a package changes, **then** catalog freshness is updated and the serving generation changes only after explicit reload.
3. **Given** a cold host or failed initial activation, **when** an eligible completion records package changes while the integration is enabled and a shell build is later requested, **then** the build observes outstanding package freshness before selecting its feature composition.
4. **Given** either host, **when** its root composition and a shell resolve the package observer, **then** they use the same upstream coordinator, after package autoload and earlier observers.

### User Story 2 - Change reload policy without rebuilding the host (Priority: P1)

An operator changes the existing reload setting and expects subsequent deliveries to use the new policy.

**Why this priority**: Existing runtime configuration behavior is part of the host contract.

**Independent Test**: Reload configuration on one built root provider and deliver changes before and after each toggle.

**Acceptance Scenarios**:

1. **Given** Foundation, **when** the setting is absent or invalid, **then** refresh and automatic reload remain enabled; explicit false disables both for subsequent callbacks.
2. **Given** Workbench, **when** the setting is absent or invalid, **then** refresh remains enabled and automatic reload remains disabled; explicit true enables automatic reload.
3. **Given** pending work, **when** configuration disables delivery or automatic reload, **then** upstream retained work follows its qualified pause/recovery semantics without an additional host scheduler.

### User Story 3 - Understand failed package activation (Priority: P1)

An operator needs each failed shell reload reported without exposing arbitrary exception messages in operator-facing details.

**Why this priority**: A completed reload call may retain the previous generation; reporting success for that shell would be misleading.

**Independent Test**: Return mixed successful and failed reload results, including nested module refusals, and inspect host diagnostics and the surviving serving generation.

**Acceptance Scenarios**:

1. **Given** mixed reload results, **when** the host reports them, **then** successful counts exclude failures and every failed shell is identified.
2. **Given** a nested recognized module refusal, **when** it is reported, **then** warning classification, refusal details and host-directory substitution remain correct.
3. **Given** an ordinary failure containing sensitive text, **when** operator-facing details are produced, **then** arbitrary exception messages remain excluded while diagnostic logs retain their existing behavior.
4. **Given** a refused promotion, **when** a later eligible completion retries, **then** the previous generation serves until a successful promotion.

### Edge Cases

- No active shell, empty loaded-assembly catalog, committed last-package removal, unchanged eligible completion and quiet cycles that deliver no callback.
- Partial reload failure, refresh failure, cancellation, fatal runtime exceptions, and configuration reload during in-flight work.
- Observer resolved before registry and registry before observer; unrelated observers keep their ownership semantics.
- Optional integration omitted; host-provided assemblies and existing independent feature contributors still compose.
- Foundation's feature-free host boundary and computed-package deployment graph remain intact.

## Requirements

### Functional Requirements

- **FR-001**: Both hosts MUST use the upstream optional adapter as the sole package assembly provider, observer and catalog/reload coordinator for this integration.
- **FR-002**: Foundation MUST preserve every-eligible refresh and reload-on defaults; its existing setting gates refresh and reload together, with absent/invalid values defaulting true.
- **FR-003**: Workbench MUST preserve changed-or-pending refresh and reload-off defaults; its existing setting gates reload only, with absent/invalid values defaulting false.
- **FR-004**: Configuration reload MUST affect subsequent deliveries without reconstructing the root provider. In-flight deliveries MUST retain the upstream per-delivery policy snapshot.
- **FR-005**: Autoload and preexisting observers MUST precede the adapter. Root, participant and shell observer resolution MUST reach the same coordinator without transferring its ownership to a shell.
- **FR-006**: Elsa MUST retain refusal interpretation, host-directory substitution, redaction and per-shell diagnostics; it MUST NOT recreate upstream epochs, freshness, retry or reload scheduling.
- **FR-007**: Existing regression objectives MUST be preserved when tests move to the adopted public surface. Any behavior correction MUST be named and justified by existing program decisions and causal evidence.
- **FR-008**: Cold-build freshness, failed eligible-work recovery, cancellation and partial reload behavior MUST remain consistent with the qualified upstream adapter.
- **FR-009**: Foundation's feature-free host, existing feed setup, readability integration and Workbench catalog projection MUST remain valid. This unit MUST NOT implement startup/readiness policy, host-library extraction, assembly unloading or package deletion.
- **FR-010**: Final adoption MUST consume the verified stable Nuplane and complete CShells release families, update reached locks and generated maps together, and pass rebuilt real Host/Workbench and relevant backend E2E proof before closure.

- **FR-011**: The frozen demo's Acts1/2 rehearsal (`bash tools/demo/rehearse.sh`) MUST pass after adoption, alongside the named actual-host and backend E2E gates; preview preparation alone cannot satisfy this acceptance.

### Key Entities

- **Host profile**: Existing host-specific refresh, enablement and reload policy.
- **Reload result**: Outcome for one shell, including the original error chain and retained serving generation.
- **Configuration change**: An operator update observed by subsequent package deliveries through the standard monitored configuration mechanism.

## Success Criteria

### Measurable Outcomes

- **SC-001**: Both hosts expose exactly one package-change coordination path, with zero remaining duplicate host provider/observer implementations for this integration.
- **SC-002**: The complete two-host absent/invalid/true/false policy matrix and runtime toggle scenarios pass without rebuilding a root provider.
- **SC-003**: Every failed shell in mixed results is reported correctly, recognized refusals preserve actionable details, and ordinary operator-facing failures expose no arbitrary exception message.
- **SC-004**: Rebuilt hosts demonstrate package delivery, cold activation, refusal retention and later recovery against the exact stable release family; existing affected regression suites, relevant backend E2E and the frozen Acts1/2 demo rehearsal pass.

## Assumptions

- [Program decisions](../../docs/plans/modular-hosting-upstream/decisions.md) D5, D6, D9, D14 and D15 are authoritative; no new product-policy choice is required for this observer-only unit.
- Upstream live-options prerequisite [CShells #161](https://github.com/valence-works/cshells/issues/161) is delivered and qualified. Stable publication remains outstanding.
- Source/test preparation may be qualified in an isolated copy with audited public previews. That evidence cannot satisfy FR-010 or justify merging against outdated committed pins.
- Shared helper placement is selected by dependency analysis in the plan; it must preserve the host boundary rather than relax architecture guards.
- Safe-pruning admission and custom-registry readiness policy remain separate unresolved decisions and cannot invalidate this observer-only preparation.
