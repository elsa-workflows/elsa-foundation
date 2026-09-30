# Feature Specification: Effective persistence preview for accepted runtime candidates

**Feature Branch**: `1307-effective-persistence-preview`

**Created**: 2026-09-30

**Status**: Approved — independently/root-reviewed specification task [#2175](https://github.com/elsa-workflows/elsa-foundation/issues/2175); implementation has not started.

**Input**: Program [#1959](https://github.com/elsa-workflows/elsa-foundation/issues/1959), developer epic [#1962](https://github.com/elsa-workflows/elsa-foundation/issues/1962), and the delivered [persistence evidence investigation](../../docs/reports/runtime-composition/developer-persistence-evidence.md). Preserve granular selection, shared defaults, explicit overrides and legacy compatibility while explaining edited compositions before deployment.

## User Scenarios & Testing

### User Story 1 - Inspect the composition I actually accepted (Priority: P1)

A developer imports local configuration or starts with a versioned workspace profile, edits feature selection, reviews and accepts it, then explicitly inspects the proposed runtime. They see applicable persistence consumers and their logical targets without publishing configuration or starting the runtime.

**Why this priority**: Existing-host inspection can give a convincing but incorrect answer about an edited candidate.

**Independent Test**: Complete import/profile → edit → accept → inspect → plan using an existing installed local host. Add one persistence consumer and remove another. Preview must reflect accepted edits; original files remain byte-identical and no generated directory is required.

**Acceptance Scenarios**:

1. **Given** accepted edits add Structured Logs persistence and remove OpenTelemetry persistence, **When** inspected, **Then** applicable candidate consumers appear and any host-default/dependency conflict is explained.
2. **Given** root and shell persistence defaults plus a diagnostic feature binding, **When** inspected, **Then** feature binding wins for that consumer, shell default for other enrolled consumers, and root default when shell default is absent.
3. **Given** legacy configuration without applicable resource selection, **When** inspected, **Then** legacy resolution stays supported without inventing resources or exposing inline connection values.
4. **Given** edits differ from accepted intent, **When** inspected, **Then** inspection refuses pending reacceptance rather than accepting on the developer's behalf.

### User Story 2 - Understand conflicts and evidence limits (Priority: P1)

A developer sees why an assignment conflicts, which selection/resource scope supplied it, and which deployment checks remain unobserved. They can fix configuration without treating preview as migration or activation permission.

**Why this priority**: Clear refusals and honest evidence limits are as useful as successful previews.

**Independent Test**: Compare inspection with runtime preparation over the same candidate inputs: shared targets, equal/unequal values behind distinct connection references, missing resources, mixed legacy/resource intent, shared-context conflicts and removed required dependencies.

**Acceptance Scenarios**:

1. **Given** shared-context participants use different connection references, **When** configured values agree or disagree, **Then** inspection applies runtime's private agreement check and reports agreement or the existing conflict without revealing values.
2. **Given** a removed feature is required by an accepted feature, **When** inspected, **Then** a conflict refuses; the removed feature is not silently restored.
3. **Given** a removed feature absent from source files is enabled by host defaults, **When** inspected, **Then** its removal remains effective or a selection conflict refuses.
4. **Given** resolved configuration, **When** presented, **Then** available scope provenance is shown; unavailable exact-file provenance, connectivity, physical target identity, migrations and live readiness remain explicit.
5. **Given** unknown local settings or host-private stores, **When** prepared, **Then** unknown data is retained, private stores are not automatically enrolled and unknown ownership is unresolved.

### User Story 3 - Inspect safely and recover from interruption (Priority: P2)

A developer deliberately chooses a trusted installed host. Unsupported capability, changed inputs, malformed requests, interruption and excessive input fail clearly without partial previews or configuration disclosure.

**Why this priority**: Inspection handles private configuration and executes selected host composition code.

**Independent Test**: Exercise the actual inspection process with changed captures, unsupported hosts, malformed/oversized inputs, cancellation, timeout and secret canaries; verify cleanup and no public disclosure, source mutation or database activity.

**Acceptance Scenarios**:

1. **Given** files change after capture, **When** results are about to publish, **Then** stale evidence refuses and requires a fresh invocation rather than silent recapture.
2. **Given** a host lacks candidate-inspection capability, **When** inspected, **Then** it refuses; existing-host inspection is not substituted.
3. **Given** interruption or exceeded limits, **When** inspection terminates, **Then** owned processes/streams are cleaned up, no partial preview appears and errors use fixed value-free explanations.
4. **Given** ordinary file-only plan/import/generate commands, **When** run, **Then** they do not implicitly start an inspection worker.

### Edge Cases

- Mixed capture instances, stale acceptance, duplicate/case-colliding/unknown/unavailable feature IDs, and host dependencies differing from reviewed catalog edges.
- A supported-name nonregular file (including an unused overlay FIFO/device) must refuse before content reads rather than block before inspection starts.
- Explicit removals absent from source files but enabled by host defaults; dependency auto-expansion that returns a removed feature.
- Null/blank bindings, inline connection syntax in reference fields, resources present without selection, and legacy inline values without safe public references.
- Ambient environment/command-line/custom-provider inputs differ from supported files; configuration agreement without physical database evidence.
- Host console output or exceptions contain values, composer never returns, composer declaration is absent, or unselected sibling overlays change.

## Requirements

### Functional Requirements

- **FR-001**: Inspection MUST be an explicit opt-in action for one installed host, shell and environment; existing file-only actions retain their no-worker behavior.
- **FR-002**: Inspection MUST require current accepted intent, pinned catalog/workspace profiles and applicable reviewed settings; it MUST NOT invent acceptance or reinterpret unknown values as safe editable fields.
- **FR-003**: One captured file context MUST supply candidate construction. Host resolution MUST consume its post-edit candidate bytes, correlated with the same accepted exact IDs, shell/environment and capture instance; original pre-edit or independently reread bytes MUST NOT be substituted.
- **FR-004**: Accepted exact IDs, host-composed requested IDs and expanded dependency IDs MUST remain distinguishable. Explicit removals MUST survive defaults. Removed/disabled required dependencies, unknown/unavailable IDs or unexpected extra features prevent successful preview.
- **FR-005**: Host-owned preparation MUST resolve enrolled consumers with existing precedence, legacy behavior, ownership/context constraints and configured-value checks; no second persistence resolver is introduced.
- **FR-006**: After selection admission succeeds, identical supported candidate inputs MUST produce runtime preparation's targets or configuration refusals. Existing offline inspection's weaker affinity behavior remains unchanged.
- **FR-007**: Preview MUST identify checked scope, consumer, logical resource if applicable, provider, safe connection reference if available, selection origin and available selector/resource scope provenance. Exact winning-file provenance MUST be unavailable where not established.
- **FR-008**: Public results, diagnostics, logs and exported artifacts MUST exclude connection values, raw configuration, unknown values, source paths, exception excerpts and fingerprints derived from secret-bearing inputs. Private values MUST NOT travel in process arguments. User-supplied file-location options and required assembly-loader paths identify local inputs; captured values may travel privately within the trusted inspection channel.
- **FR-009**: Producer/consumer MUST validate version, capability, complete shape, logical identity contents, duplicates, size/count limits and candidate correlation. Invalid or unsupported exchanges refuse without partial output or fallback.
- **FR-010**: The full host-file snapshot, accepted composition, any explicitly supplied catalog and setting review, and every supplied workspace-profile file (including unused definitions) MUST be captured once and rechecked before inspection and immediately before publication. Mixed captures/stale acceptance refuse without source writes or automatic recapture.
- **FR-011**: Inspection MUST perform no package acquisition, host startup, service activation, database action, migration, configuration save or candidate publication. Selected host composer execution is explicitly trusted; no general sandbox is promised.
- **FR-012**: Cancellation, timeout and transport failure MUST terminate owned processes, close streams and return value-free outcomes. Raw host console output is never forwarded.
- **FR-013**: Results MUST distinguish resolved configuration, refusal and unavailable evidence; configured agreement does not establish physical identity, network/package reachability, migration permission or readiness.
- **FR-014**: Supported capture MUST be file-only; ambient environment, command-line and custom providers stay unverified, with no deployed-host parity claim.
- **FR-015**: Inspection MUST retain unknown local candidate data privately without dropping or exporting it. Later generation independently captures, preserves and rechecks its own inputs and review; preview neither authorizes generation nor proves cross-invocation continuity. Portable import/export policy stays unchanged; arbitrary unknown portable round-trip remains an open parent outcome.
- **FR-016**: Human and machine previews MUST derive from one content-validated result with deterministic ordering and identical meanings; inspection requires no generated directory.

- **FR-017**: Inspection MUST isolate selected host execution from the planning front end; file-only planning MUST NOT load or compose a host in its own process.

### Key Entities

- **Accepted composition**: Pinned selection intent, exact accepted IDs and applicable reviewed settings/resource choices.
- **Captured candidate context**: Frozen local files and accepted inputs, selected shell/environment, retained unknown data and a privately correlated in-memory candidate.
- **Selected host closure**: Installed host assemblies supplying real descriptors, declared defaults and persistence ownership.
- **Selection reconciliation**: Accepted/requested/implicit/disabled IDs and required-edge conflicts.
- **Configuration resolution**: Per-consumer safe targets and available provenance, separate from live readiness.
- **Inspection outcome**: Complete safe preview or stable refusal with explicit unchecked evidence.

## Success Criteria

### Measurable Outcomes

- **SC-001**: Developers complete import/profile → edit → accept → inspect → plan with zero original-file changes and no candidate directory.
- **SC-002**: Every supported valid fixture reports exactly applicable candidate consumers and agrees with runtime preparation; every invalid fixture yields the corresponding refusal.
- **SC-003**: Every host-default/removal/dependency adverse fixture preserves accepted selection or refuses; zero removed features silently return in successful output.
- **SC-004**: All normal/adverse public-output checks contain zero value canaries, while unknown local fixture data remains retained.
- **SC-005**: Every successful preview states checked scope and unavailable deployment checks; zero previews claim live or physical readiness.
- **SC-006**: Every unsupported/interrupted/malformed/oversized/changed-input fixture yields no partial preview, source writes or surviving owned process.

## Assumptions

- [Specs173](../173-shared-persistence/spec.md), [174](../174-profile-selection-planner/spec.md), [175](../175-offline-composition-plan/spec.md), [176](../176-composition-file-bridge/spec.md) and subsequent acceptance/workspace-profile contracts remain authoritative in their own scopes.
- Developers supply installed host closures and explicitly trust declared composition code. Inspection does not acquire packages or sandbox arbitrary host code.
- File-only capture is first. External-provider capture remains #1962 work, revisited before deployed-configuration parity claims.
- Framework configuration classification and Elsa constitution §E4 remain deferred; this bounded contract does not ratify them.
- Finished builder UI, real-user evaluation #2064, arbitrary unknown portable export and operational recovery #1964 remain separate required program outcomes.
