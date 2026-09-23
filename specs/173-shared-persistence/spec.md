# Feature Specification: Shared persistence resources

**Feature Branch**: `1305-shared-persistence`

**Created**: 2026-09-23

**Status**: Draft — design reviewed; #1967 publication pending

**Input**: Program [#1959](https://github.com/elsa-workflows/elsa-foundation/issues/1959), specification [#1967](https://github.com/elsa-workflows/elsa-foundation/issues/1967): configure persistence once, retain explicit feature overrides, and make runtime and migration tooling agree. Incorporates reviewed spikes [#1965](https://github.com/elsa-workflows/elsa-foundation/issues/1965#issuecomment-5798240404) and [#1966](https://github.com/elsa-workflows/elsa-foundation/issues/1966#issuecomment-5798450673).

## User Scenarios & Testing

### User Story 1 - Configure one shared database (Priority: P1)

As a host developer, I define one named persistence resource and select it as my shell default so the supported workflow stores use the intended database without repeating provider and connection settings on every feature.

**Why this priority**: Establishes the smallest complete runtime and tooling path behind the program's central configuration promise. Owned by #1968.

**Independent Test**: Start a rebuilt supported host using one shared resource, design and publish a workflow, execute it, restart the host, and inspect persisted design, publication and execution state. Run migration tooling for that same configuration and verify the same target and expected migration histories.

**Acceptance Scenarios**:

1. **Given** one valid resource and enabled supported consumers with no legacy target fields, **When** the shell starts, **Then** every enrolled consumer uses that resource's provider and connection together.
2. **Given** the same explicit configuration context, **When** runtime and migration tooling resolve the composition, **Then** they agree on every enrolled consumer's target; an omitted feature provider cannot introduce an unintended default.
3. **Given** a workflow designed, published and executed on the shared database, **When** the host restarts against that database, **Then** the relevant state remains available and executable.
4. **Given** missing resources, missing connection references or ambiguous legacy target fields, **When** the operator validates or starts the composition, **Then** the operation refuses with affected consumers and source fields before unintended persistence activity.

### User Story 2 - Keep existing configurations working (Priority: P1)

As an existing host developer, I can continue using my configuration unchanged and can migrate selected supported consumers to resources deliberately.

**Why this priority**: The new default must not reinterpret existing configuration. This is a release gate within #1968, independently verified with existing configurations.

**Independent Test**: Run representative legacy configurations and their existing focused checks with resource mode absent; compare providers, connections, schema, pooling, policy and source precedence to the recorded baseline.

**Acceptance Scenarios**:

1. **Given** no applicable resource selection, **When** a consumer is configured, **Then** its legacy defaults and explicit options keep their existing meanings.
2. **Given** a feature with explicit legacy target fields and an applicable resource selection, **When** resolution runs, **Then** it refuses ambiguous ownership instead of guessing intent, even if the values appear equivalent.
3. **Given** a missing explicit connection-name value, **When** configuration resolves, **Then** it refuses instead of using another connection or creating a default file database.
4. **Given** unknown authored settings and explicit false, zero, empty or null values, **When** the configuration is inspected or edited, **Then** those distinctions remain intact unless the operator explicitly removes a setting.

### User Story 3 - Isolate diagnostics explicitly (Priority: P2)

As an operator, I bind the supported diagnostics consumers to a second resource while all other enrolled consumers keep the shell default.

**Why this priority**: Proves useful granular exceptions without assuming arbitrary physical splits are safe. Owned by #1969 after #1968.

**Independent Test**: On the shared-resource foundation, point both supported diagnostics consumers at a second database; run a representative workflow, inspect both databases, restart and verify retained state. Validate each target's module set with migration tooling.

**Acceptance Scenarios**:

1. **Given** a shared default and explicit diagnostics bindings, **When** the host runs, **Then** diagnostic persistence uses the second target and other enrolled stores retain the first.
2. **Given** an explicit diagnostics binding, **When** the binding is removed, **Then** the consumer inherits the shell default again and unrelated settings are preserved.
3. **Given** a binding that violates a known shared-context or transaction constraint, **When** the composition is checked, **Then** the affected consumers and constraint are reported and the composition cannot be called ready.
4. **Given** two database targets, **When** migration tooling operates on one target, **Then** it selects only the associated modules and does not apply the whole composition against the single supplied connection.

### User Story 4 - Understand changes before activation (Priority: P1)

As a developer or operator, I can inspect where each effective persistence choice came from and know whether validation used the same sources as the runtime before saving or reloading configuration.

**Why this priority**: A correct resolver must be usable through actual startup, management and tooling paths. #1968 owns this integration; broader recoverable operations remain #1964.

**Independent Test**: Inspect a composition with a file default, an external override and a masked connection setting; compare effective targets across equivalent contexts, change a file-authored binding, and explicitly reload the shell. For existing management writes, verify either safe acceptance under the current context or explicit refusal before mutation; this does not require a new resource editor.

**Acceptance Scenarios**:

1. **Given** external overrides, **When** a candidate is reviewed, **Then** authored and effective choices are distinguishable and the sources actually checked are identified.
2. **Given** a valid accepted change, **When** the shell reloads, **Then** resolution runs again before consumers register and inherited values are not written into the authored document as explicit feature settings.
3. **Given** changed resource or source context since preview, **When** an existing management path attempts a resource-mode mutation, **Then** it either validates the current effective context before accepting the mutation or explicitly refuses before saving; the old feature revision alone is insufficient.
4. **Given** a resource-mode management write accepted by this slice whose save succeeds followed by refresh or reload failure, **When** the operation reports its result, **Then** it states that saved configuration changed and activation did not complete; it does not claim rollback or successful activation.
5. **Given** a file-only tooling context, **When** its result is compared with a live runtime using external overrides, **Then** unavailable parity evidence is reported rather than inferred.

### Edge Cases

- Resource definitions exist but none is selected; definitions alone do not activate resource mode for a consumer.
- A binding is removed versus explicitly null, empty or unknown; removal restores inheritance, malformed selection refuses.
- A provider has a CLR default but was never authored; that default is not a conflict with a resource.
- A feature is disabled versus an enabled feature setting being false; zero must not mean absent.
- A resource changes while a candidate is being validated; the old Features-only revision does not cover this change.
- Two feature IDs share one underlying context but select incompatible targets or context options.
- Different resource names resolve to the same target; names alone do not establish or disprove transaction compatibility.
- A connection value is supplied through a secret reference or live tooling input; neither its value nor a reversible representation may leak in explanations.
- An old tooling host does not understand resource-mode input; explicit version/capability refusal is required.
- Missing packages, unknown consumers or unproven layouts cannot produce a ready result.

## Requirements

### Functional Requirements

- **FR-001**: A named relational resource MUST define its provider and connection reference together. Selecting a resource MUST apply that pair atomically.
- **FR-002**: An enabled enrolled consumer MUST select its explicit shell feature binding when present, otherwise its effective shell default, otherwise legacy configuration. The effective shell default is an explicit shell default or, when absent, the root default. Resource definitions alone MUST NOT select a resource.
- **FR-003**: The first supported shared-resource layout MUST cover all enabled enrolled Runtime, Workflows Design, Activities Design and Publishing consumers identified by #1965. Enrollment MUST use stable feature identities and explicit ownership metadata, not property-name resemblance.
- **FR-004**: The supported exception layout MUST bind both Structured Logs and OpenTelemetry persistence to a second target without changing other consumers' selected targets.
- **FR-005**: Removing a binding MUST restore normal inheritance while retaining unrelated authored settings. Explicit null/empty/unknown binding values MUST refuse and MUST NOT mean removal.
- **FR-006**: Applicable resource selection combined with any authored legacy Provider, ConnectionString or ConnectionName field MUST refuse as ambiguous. Initialized feature defaults MUST NOT count as authored fields.
- **FR-007**: Without applicable resource selection, existing provider, connection, schema, pooling, migration policy and source precedence MUST retain their baseline behavior. Host-owned/private stores MUST NOT inherit automatically.
- **FR-008**: Missing selected resources, incomplete resources, missing explicit connection values and unsupported selections MUST refuse with consumer/source diagnostics before unintended database activity. They MUST NOT silently choose SQLite or another resource.
- **FR-009**: Authored presence and source precedence MUST be retained before feature defaults are applied. Explicit false, zero, empty, null and absence MUST remain distinguishable until the owning setting's rules are applied.
- **FR-010**: Runtime startup, shell reload and migration tooling MUST use the same resolution semantics. Identical explicit input contexts MUST yield identical effective targets before provider-agreement and context registration checks.
- **FR-011**: Results MUST state the configuration context and sources actually checked. Tool-process environment MUST NOT silently stand in for the target host's environment. Unavailable checks MUST remain visibly unverified.
- **FR-012**: Resource selection MUST NOT change schema/pooling ownership or grant migration permission. Existing migration policy and provider-specific schema rules MUST remain in force.
- **FR-013**: EF-owned validation MUST enforce known shared-context agreement and transaction-affinity constraints for enrolled layouts. Unsupported or unknown layouts MUST NOT be called ready based only on names or configuration metadata.
- **FR-014**: Plans and explanations MUST show consumer, chosen resource, provider, source and unresolved prerequisites without exposing connection secrets. Trusted execution may resolve values; exported plans and command arguments MUST NOT contain them.
- **FR-015**: Existing explicit tooling provider selection MUST remain authoritative. Live tooling MUST retain safe environment/stdin connection input. Each target invocation MUST operate only on its intended module set, and older hosts MUST explicitly refuse unsupported resource-mode contracts.
- **FR-016**: Planning MUST remain separate from package installation, database access, file saving and activation. A plan MUST NOT imply that these side effects or live readiness checks have occurred.
- **FR-017**: Any management path accepting a resource-mode mutation MUST evaluate the effective candidate after restoring masked secrets and applying the actual source context. If that path cannot establish safe acceptance, it MUST explicitly refuse before save/refresh/reload side effects. A new resource editing API is not required by this slice; legacy management behavior remains supported.
- **FR-018**: For resource-mode management writes this slice accepts, resource/source drift MUST be accounted for before acceptance, authored settings MUST be saved without flattening inheritance, and refresh/reload results MUST truthfully distinguish saved state from activated state. Otherwise the write MUST refuse before mutation. General durable recovery remains #1964.
- **FR-019**: Startup and reload MUST recompute resource-derived settings before feature binding. Changes to source context or loaded participant metadata MUST not retain a stale materialized plan.
- **FR-020**: Unknown authored settings MUST survive a round-trip. Unknown feature identities and unproven custom persistence ownership MUST be preserved with unresolved status, not silently enrolled or reported ready.
- **FR-021**: Documentation MUST include the supported layouts, migration from legacy fields, binding removal, checked-source limitations, host-owned exclusions and executable verification steps.

### Normative supported participants and constraints

Stable feature IDs below define first-slice enrollment. Context/module ownership comes from reviewed #1965; the plan must preserve these constraints rather than infer that every selectable feature has its own database.

| Participant set | Stable feature IDs | Context / module |
|---|---|---|
| Runtime | `WorkflowsRuntimeEntityFrameworkCore`; `WorkflowsRuntimeWorkflowExecutionEntityFrameworkCorePersistence`; `WorkflowsRuntimeActivityExecutionEntityFrameworkCorePersistence`; `WorkflowsRuntimeOperationalStateEntityFrameworkCorePersistence`; `WorkflowsRuntimeAlterationEntityFrameworkCorePersistence`; `WorkflowsRuntimeTestScopeEntityFrameworkCorePersistence`; `WorkflowsRuntimeBookmarksEntityFrameworkCorePersistence`; `WorkflowsRuntimeArtifactsEntityFrameworkCorePersistence` | `RuntimeDbContext` / `Workflows.Runtime` |
| Workflows Design | `WorkflowsDesignEntityFrameworkCore` | `WorkflowsDesignDbContext` / `Workflows.Design` |
| Activities Design | `ActivitiesDesignEntityFrameworkCore` | `ActivitiesDesignDbContext` / `Activities.Design` |
| Publishing | `WorkflowsPublishingEntityFrameworkCore` | `PublishingSnapshotReviewDbContext` / `Workflows.Publishing` |
| Structured Logs | `DiagnosticsStructuredLogsEntityFrameworkCore` | `StructuredLogsDbContext` / `Diagnostics.StructuredLogs` |
| OpenTelemetry | `DiagnosticsOpenTelemetryEntityFrameworkCore` | `EfOpenTelemetryDbContext` / `Diagnostics.OpenTelemetry` |

- All enabled Runtime participants MUST agree on the one context's provider, connection and other shared context options. Selecting individual features does not create independent Runtime stores.
- When activity-upgrade apply is enabled, Activities Design and Workflows Design MUST meet the existing shared-transaction target equality requirement. Resource names alone do not prove this equality.
- Reusable-activity publication's ordered Runtime → Activities Design → Publishing receipt path MUST remain valid; it does not impose one blanket shared transaction or require all three contexts to be colocated by a new generic rule.
- The shared layout and separate-diagnostics layout are the only new-mode physical layouts this slice can report ready after their required live proof. Other proposed layouts remain unresolved. This MUST NOT reject previously supported legacy split publication when resource mode is absent.
- Read-only Dashboard consumption does not introduce a separate persistence resource. Host-owned OpenIddict and unenrolled/private/custom stores do not inherit automatically.
- Code-configured feature defaults or configurator actions MUST NOT silently overwrite a materialized resource target or add an unresolved persistence consumer after validation. The plan must establish a safe integration order or explicitly refuse unsupported resource-mode compositions. Ordinary legacy code configuration remains unchanged.

### Key Entities

- **Persistence resource**: Named provider and connection reference; selection is atomic.
- **Feature binding**: Explicit association of a stable feature identity with a resource.
- **Shell default**: Resource inherited by enabled enrolled consumers without an explicit binding.
- **Authored configuration context**: Values, presence, precedence, shell and external-source identity used for resolution.
- **Effective persistence plan**: Resolved participant choices, provenance, constraints and unresolved prerequisites; public representation is redacted.
- **Persistence participant**: Enrolled consumer with module/context ownership and supported validation rules.
- **Validation evidence**: Configuration, package, target and activation checks actually performed, with their context and outcome.

## Success Criteria

### Measurable Outcomes

- **SC-001**: The shared-layout operator authors the provider and connection reference once; every enrolled consumer uses the selected pair, with zero unintended default targets.
- **SC-002**: All supported consumers agree across runtime and tooling for every equivalent-context acceptance case; unlike or unavailable contexts are always identified.
- **SC-003**: Design, publication, execution and diagnostic state required by the two supported layouts survives a host restart and is found in the intended stores.
- **SC-004**: Every selected negative acceptance case refuses before unintended persistence activity and identifies the affected consumer or source without disclosing a secret.
- **SC-005**: The legacy characterization suite passes unchanged in objective, and removal of an explicit diagnostics binding restores inheritance without loss of unrelated settings.
- **SC-006**: Every tested successful file/shell reload uses the current effective configuration. Every tested management resource-mode write is either safely accepted with truthful saved/activated results or explicitly refused before mutation.

## Assumptions

- The initial new-mode host proof uses rebuilt Elsa Workbench with PostgreSQL for the shared and separate-diagnostics resources. This demonstrates supported composition behavior; Workbench is a development/demo host, not a newly introduced production product.
- Legacy configurations remain supported on their existing providers. Generalizing new-mode host evidence to other providers or arbitrary layouts requires explicit additional acceptance evidence.
- OpenIddict stays host-owned under #1895. IAM, Secrets, distributed stores and third-party consumers are outside automatic first-slice enrollment; they are not silently redirected by a shell default.
- Existing #1902 owns broader composition-guard cleanup. This slice must coordinate any guard guarantee it consumes instead of claiming unresolved checks passed.
- Framework §2.12 and Elsa §E4 settings taxonomy remain deferred. This specification defines a bounded persistence capability and does not ratify a generic settings framework.
- Profiles, groups, builder screens, data relocation and durable cross-system operation orchestration remain later program work. The first slice still must integrate safely with existing management and reload paths.
- The first slice uses file-authored resources and explicit shell reload. The legacy feature editor refuses applicable resource-mode writes before guards/mutation; safe resource-aware editing remains required under #1964.
- Resource editing through the future builder/API and broader recovery remain required program outcomes under #1963/#1964; conditional first-slice management acceptance does not declare those outcomes delivered.
- The reviewed plan/contracts/tasks settle the concrete reload seam, authored format, tooling protocol and bounded management drift check. Complete #1967 publication before activating implementation; runtime/database acceptance remains owned by #1968/#1969.
