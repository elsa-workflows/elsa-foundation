# Feature Specification: Pinned profile selection planner

**Feature Branch**: `codex/1984-profile-planner-spec`

**Created**: 2026-09-24

**Status**: Draft — specification task #1984; implementation and live-profile acceptance remain separate

**Input**: [Program #1959](https://github.com/elsa-workflows/elsa-foundation/issues/1959), [profiles epic #1961](https://github.com/elsa-workflows/elsa-foundation/issues/1961), [task #1984](https://github.com/elsa-workflows/elsa-foundation/issues/1984), and the [proposed profile-catalog decision record](../../docs/reports/runtime-composition/profile-catalog-contract.md). This specification defines a pure planner contract, not a released profile catalog or runtime activation path.

## User Scenarios & Testing

### User Story 1 - Start from a small, exact selection (Priority: P1)

A developer selects at most one reviewed starting profile, adds flat feature groups, and edits individual feature IDs. The developer sees the exact resulting feature set and where each selection came from before exporting anything.

**Why this priority**: A short starting choice is useful only if it retains the module-level control the current shell already provides.

**Independent Test**: Given a pinned catalog and authored choices, calculate the expanded set twice, with different declaration order, and compare the exact feature IDs and provenance.

**Acceptance Scenarios**:

1. **Given** a pinned profile, two groups with an overlapping feature, and an explicit addition, **when** the developer plans the selection, **then** the result contains each selected stable feature ID once and explains every contributing choice.
2. **Given** a selected feature in the profile or a group, **when** the developer explicitly removes it, **then** the feature is absent from the exact expansion even if another choice includes it, and the removal remains visible in the plan.
3. **Given** the same semantic catalog and choices with different file whitespace, object-key order, or set-member declaration order, **when** they are planned, **then** the pinned digests and expanded set agree.

### User Story 2 - Understand what is unresolved (Priority: P1)

A developer plans against a supplied target-host inventory and sees what that host actually knows, what is missing, and why a selected feature may need another feature. The developer can inspect the plan without installing a package or starting the host.

**Why this priority**: A profile that hides missing dependencies or packages would turn a shorter form into a misleading deployment promise.

**Independent Test**: Supply synthetic inventories with required and optional edges, absent packages, unreadable manifests, incompatible versions, and unknown IDs; assert exact findings and no side effects.

**Acceptance Scenarios**:

1. **Given** a selected feature whose required dependency was explicitly removed, **when** the plan is assessed, **then** it preserves the removal and reports an unresolved required edge with its evidence; it does not add the dependency back.
2. **Given** an optional companion, **when** the plan is assessed, **then** the companion is suggested with a reason and is not silently added.
3. **Given** an authored ID absent from the supplied inventory, **when** the plan is assessed, **then** the ID remains in the exact selection and is marked unresolved; an installed manifest alone does not certify host loadability or remote package availability.
4. **Given** a loaded runtime descriptor that disagrees with a package manifest, **when** the plan is assessed, **then** the runtime dependency evidence governs the loaded feature and the manifest disagreement is shown for review.

### User Story 3 - Review upgrades without silent drift (Priority: P2)

A developer re-resolves an existing composition against a newer catalog or host inventory and reviews the proposed changes before accepting them. The old authored selection and its pins remain usable as the comparison baseline.

**Why this priority**: Existing runtimes must not change just because selection metadata or an installed package changes.

**Independent Test**: Compare two immutable catalog snapshots and two supplied inventories while holding the authored composition fixed; assert a stable old plan and an explicit proposed diff.

**Acceptance Scenarios**:

1. **Given** a published profile or group ID/version whose content digest changes, **when** it is loaded, **then** publication or import refuses the mismatch instead of treating the content as the same version.
2. **Given** a newer reviewed version with changed members or dependency rationale, **when** the developer requests re-resolution, **then** the plan shows added and removed IDs, rationale changes, package drift, and known or unverified settings/resource impact before any new pins are accepted.
3. **Given** a custom profile owned in the developer workspace, **when** its membership changes, **then** a new immutable version/digest is needed and the existing composition continues to refer to its old snapshot.

### User Story 4 - Carry the plan into later tools (Priority: P3)

A developer command and a future web builder receive the same selection plan and can distinguish feature selection from effective settings and persistence choices.

**Why this priority**: Two interfaces should not make different decisions about the same authored composition.

**Independent Test**: Feed identical documents, catalog snapshot, inventory snapshot, and optional effective-persistence evidence through both consumer adapters; compare their shared plan result rather than UI presentation.

**Acceptance Scenarios**:

1. **Given** a selected EF persistence feature, **when** the selection is planned without an effective-persistence result, **then** the feature remains selected while provider, connection, physical layout, and migration safety remain visibly unverified.
2. **Given** an effective-persistence result from the reviewed shared-persistence contract, **when** it is attached to the selection plan, **then** its provenance and unresolved state are carried through without introducing group-level settings inheritance or exposing a connection secret.

### Edge Cases

- No profile and no groups is a valid empty starting selection; explicit additions can still make a custom composition.
- Repeated identical IDs in a definition, duplicate catalog IDs, duplicate ID/version pairs, and a digest mismatch are publication errors, not silently deduplicated catalog input.
- The same feature may be selected by several valid sources; the plan deduplicates the feature while retaining all selection reasons.
- An explicit addition and removal of the same ID resolves to removed, with both authored intents visible.
- A known feature may have no package identity because it is host-bundled; a missing identity for a package feature remains unresolved rather than receiving a fabricated lock.
- An empty dependency list on a loaded runtime descriptor is authoritative; absence of a runtime descriptor is not evidence that the feature has no dependencies.
- Source categories and setting-level `Group` hints do not define membership or dependency requirements.
- Unknown imported settings and resource fields remain untouched by this pure planner. A later editor/exporter owns safe round-trip behavior.
- A host inventory may be old or incomplete. Its identity and observation time must be shown, and unavailable checks cannot become a ready verdict.

## Requirements

### Functional Requirements

- **FR-001**: The v1 selection catalog MUST be Foundation-owned, reviewable, versioned, and distributable with the developer planner. Only its reviewed definitions may set starting-profile or feature-group membership. Package and runtime metadata may enrich host evidence but MUST NOT mutate reviewed membership.
- **FR-002**: The catalog MUST identify each profile and flat group by stable ID, immutable version, and content digest. A stable definition ID MUST denote only one kind, though it may have multiple immutable versions. Published ID/version content MUST never be edited in place; duplicate definition ID/version pairs, duplicate member IDs, and digest mismatches MUST block publication/import with a specific error.
- **FR-003**: A v1 authored composition MUST allow zero or one starting profile, zero or more flat groups, explicit feature additions, and explicit removals. Profiles and groups MUST NOT nest, run scripts, or define a persistent settings-precedence level.
- **FR-004**: Expansion MUST apply profile members, union group members, apply additions, then apply removals, and only then validate dependencies and host evidence. It MUST produce a stable, sorted exact feature-ID set and preserve per-feature selection and removal provenance.
- **FR-005**: Only stable CShells feature IDs may be selection keys. Categories, display labels, setting groups, package project references, and apparent package names MUST NOT be treated as activation dependencies or alternative feature identities.
- **FR-006**: The authored composition MUST pin the catalog, selected profile/group definitions, exact expanded feature IDs, and observed package identity/version/manifest hash for resolved package features or mark a feature host-bundled. Missing package identity MUST remain unresolved. The live shell's feature-only revision MUST NOT stand in for catalog or inventory identity.
- **FR-007**: Definition and catalog digests MUST follow the canonicalization rules in [the v1 contract](contracts/selection-planner-v1.md). Secret values, connection strings, mutable enabled state, and live shell revision MUST NOT enter those digests or exported feature locks.
- **FR-008**: The planner MUST consume an explicit host-inventory snapshot and identify its source and observation time. It MUST distinguish observed loaded features, installed-package manifest evidence, absent or unreadable evidence, and compatibility evidence. It MUST NOT query a feed, load a package, start a host, access a database, save files, or activate features.
- **FR-009**: For a loaded feature, runtime-descriptor dependencies MUST be treated as authoritative required edges, including an empty list. For a feature without a runtime descriptor, manifest dependency optionality MAY be used as lower-confidence evidence. Conflicts and unavailable dependency evidence MUST remain visible.
- **FR-010**: A required edge whose target is absent from the exact authored selection MUST be unresolved with source and reason. The planner MUST NOT silently add it. Optional companions MUST be advisory only. A host's later `DependsOn` Auto-Resolve mode is a separate activation behavior and MUST NOT be mistaken for a planner-accepted edit or an exact runtime set.
- **FR-011**: Unknown feature IDs, absent packages, unreadable manifests, unresolved edges, and incompatible host/package versions MUST remain present in the plan as explicit unresolved findings. The planner MUST NOT infer remote feed availability or host compatibility from an installed manifest alone.
- **FR-012**: Re-resolution MUST be explicit and non-mutating. It MUST compare old and candidate feature sets, definition/dependency rationale, observed package pins, and known settings/resource impact; unavailable impact evidence MUST be labeled unverified before a new version can be accepted.
- **FR-013**: Custom profiles MUST be user-owned, source-controlled workspace documents in v1 and follow the same immutable version/digest rules. This slice MUST NOT use the server feature-management store as a global profile registry.
- **FR-014**: The plan-result contract MUST expose exact selection, provenance, catalog/inventory identities, dependency findings, availability and compatibility evidence, optional advice, package locks, and upgrade differences to a developer command and later UI. It MUST never claim the composition is runtime-ready solely from planning evidence.
- **FR-015**: Persistence selection and resolution MUST remain separate. The plan MAY consume a supplied effective-persistence result under [spec 173](../173-shared-persistence/spec.md) and MUST carry its checked context, provenance, and unresolved state without exposing connection values. Group-wide persistence assignment is an editor action that writes explicit feature bindings, not planner inheritance.
- **FR-016**: The planner MUST leave unknown authored settings/resource fields intact and report when it cannot assess their impact; it MUST NOT flatten inherited settings or rewrite the authored document as a side effect of planning.
- **FR-017**: The Embedded, Authoring, Worker, and Custom evaluation fixtures MUST be used as exact-set contract examples. They MUST be labeled planning fixtures until rebuilt-host dependency closure, API-surface, persistence, and activation evidence exists.

### Contract shapes and examples

The [v1 selection-planner contract](contracts/selection-planner-v1.md) specifies the required catalog, authored-composition, host-inventory, and plan-result fields; digest semantics; and positive/negative example vectors. These are document and result requirements for an implementation plan, not a claim that a parser, catalog, or UI exists today.

### Key Entities

- **Selection catalog**: Foundation-reviewed snapshot containing immutable profile and group definitions and its own version/digest.
- **Starting profile / feature group**: Flat reviewed selection definition with stable ID, version, members, reviewed rationale, and digest. A custom profile has the same semantics but belongs to a developer workspace.
- **Authored composition**: User intent: pinned catalog/definitions, chosen groups, additions/removals, exact previously accepted expansion, observed feature locks, and opaque settings/resource content outside the planner's ownership.
- **Host inventory**: Supplied, time-stamped observations about loaded features, installed manifests, runtime dependencies, package identity, and compatibility; not a remote-feed catalog or proof of activation.
- **Selection plan**: Deterministic exact set, provenance, unresolved/advisory findings, evidence identities, optional effective-persistence context, and a proposed upgrade diff.

## Success Criteria

### Measurable Outcomes

- **SC-001**: The four named evaluation fixtures each produce the exact documented feature-ID set on repeated plans, independent of whitespace, declaration order, or which consumer presents the plan.
- **SC-002**: Every required-removal, optional-companion, unknown-ID, missing-package, unreadable-manifest, incompatible-version, and changed-catalog acceptance example produces the documented unresolved or advisory result with zero silent feature additions.
- **SC-003**: For every test that changes a published definition's semantic content without changing its ID/version, digest validation rejects the mismatch; formatting-only edits leave the digest unchanged.
- **SC-004**: Every re-resolution example displays the old and candidate exact sets plus all evidenced feature, dependency, package, and settings/resource changes before acceptance; no planning example changes the authored document.
- **SC-005**: Every example with unavailable host or persistence evidence states that limitation and makes no runtime-ready claim; no exported plan contains a connection secret.

## Assumptions

- The proposed Foundation-owned catalog contract in the #1982 report is the scope baseline for this specification. Publishing actual reviewed profile memberships requires separate rebuilt-host proof and review.
- The first consumer is a developer planning command; a future builder uses the same plan result. Neither consumer, package delivery, nor server-side draft storage is implemented by this unit.
- A supplied host inventory represents one named target and observation time. A later host check must revalidate drift before activation; #1145/#1951 own package delivery/compatibility, #1159 owns unknown requested-feature runtime behavior, and #1815 owns module layout.
- The [shared-persistence specification](../173-shared-persistence/spec.md) owns effective provider/connection resolution and physical-layout checks. A selected persistence feature alone does not prove the resource layout or authorize migration.
- This specification remains `Draft` after publication until a later work unit approves an implementation boundary. It does not ratify a framework-wide settings taxonomy or alter the constitution.
