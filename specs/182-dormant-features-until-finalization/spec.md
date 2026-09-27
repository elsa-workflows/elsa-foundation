# Feature Specification: Dormant Features Until Finalization

**Feature Branch**: `claude/2093-rollout-specs`
**Created**: 2026-09-27
**Status**: Draft
**Input**: Workstream B6, [issue #2102](https://github.com/elsa-workflows/elsa-foundation/issues/2102), of the
cluster-safe schema rollout program [#2093](https://github.com/elsa-workflows/elsa-foundation/issues/2093). A feature
that needs data only a new persisted-schema version can hold stays dormant until that version is finalized. A dormant
feature says why: it becomes available once every host can read the new version, rather than being absent. A write
that needs dormant data is refused, never dropped.

Decision of record: [ADR 0078](../../docs/adr/0078-workflow-executions-are-virtual-actors-and-cluster-membership-is-a-foundation-contract.md),
"Features that need the new data wait for finalization": a dormant feature says why, a write that needs dormant data
is refused rather than dropped, and "modules check dormancy through one shared helper over the finalized version". It
also records the rejection of two alternatives: "Early use of new-data features where the data lives only in new
columns" and "Leave dormancy to each module". ADR 0078 is Proposed, and B0
([#2096](https://github.com/elsa-workflows/elsa-foundation/issues/2096)) accepts it.

Companion specs: [spec 180](../180-schema-upcaster-chain/spec.md) (B4,
[#2100](https://github.com/elsa-workflows/elsa-foundation/issues/2100)) defines schema families and the write path
whose FR-016 backstops this spec. [Spec 181](../181-schema-finalization-gate/spec.md) (B5,
[#2101](https://github.com/elsa-workflows/elsa-foundation/issues/2101)) decides when a version is finalized and how
quickly each host observes it.

## Terms

Spec 180's and spec 181's Terms apply. In addition:

- **Dormant feature**: an enabled feature that is composed and running, but whose operations needing new-version data
  are unavailable until a finalization, or a completeness condition (FR-005), is met.
- **Dormancy requirement**: a feature's declaration that it needs schema family F at version V or later.
- **Dormancy refusal**: spec 180's write refusal (Terms), reused for a dormant feature's refused operation and, when
  raised by a feature-level check rather than by a store, extended with the feature id and FR-008's reason.
- **Shared dormancy check**: the one component every module asks "is F at V yet?".

## Current state

- **Feature catalog.** `IFeatureManagementService.GetCatalogAsync` returns `FeatureCatalogItem`s built by
  `IFeatureCatalogContributor`s (`RuntimeFeatureCatalogContributor`, `PackageManifestFeatureCatalogContributor`), and
  the Modularity API serves them. An item carries `Enabled` and `ReadError`, but nothing that says a feature is
  enabled yet unavailable.
- **Attention.** `ModularityAttentionContributor` already turns catalog problems (a manifest read error, a missing or
  disabled dependency) into `AttentionItem`s, which Attention serves at `GET /_elsa/attention/items`.
  `AttentionSeverity` is `Critical`, `Warning` or `Info`. `Elsa.Attention.Core` carries a public API baseline.
- **Capabilities.** `GET /capabilities` advertises declared API capabilities. The
  [API Capabilities README](../../src/essentials/Api/Capabilities/README.md), "Declaration rules", says operationally
  conditional capabilities belong in `IApiCapabilitySource` implementations, which are evaluated per document.
- **Refusal conventions.** Each domain API renders its own problem envelope and maps typed exceptions to statuses.
  `ModularityFaultRenderer` maps `FeatureActivationRefusedException` to 409 ahead of its `InvalidOperationException`
  arm, which answers 400. `WorkflowPublishingFaultRenderer` maps typed conflicts, each carrying a `Code`, to 409. The
  skew exception's test (`EfSchemaVersionTests`) pins that it cannot be caught by any type a store's catch filter
  names. Stores rewrap `JsonException`, `ArgumentException`, `InvalidOperationException` and `NotSupportedException`
  as `InvalidDataException`, which reads as corruption.
- **Reload.** `Elsa.Foundation.Host` reloads shells after a Nuplane reconciliation. `Elsa.Workbench` registers
  `NullShellReloader`, which does nothing (ADR 0077, Decision: "Hot reload stays production-supported"). A behaviour that only
  changes on a shell reload would never change on Workbench without a restart.
- **Nothing is dormant today.** No feature declares a schema requirement, and no shared check exists.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - An operator sees why a feature is not available yet (Priority: P1)

During a rolling upgrade, an operator opens the module list and sees that a new feature is enabled but dormant, with
the reason: available once every host can read version 2 of the family it needs, with two of three hosts ready. If
finalization is held, the reason names the hold. Attention shows the same thing as an informational item.

**Why this priority**: ADR 0078's first rule for dormancy. A feature that is simply missing sends the operator looking
for a composition fault that does not exist.

**Independent Test**: With finalization held, enable a feature that declares a requirement on version 2, and read the
feature catalog and Attention. Release the hold, let finalization happen, and read them again.

**Acceptance Scenarios**:

1. **Given** a feature requiring version 2 and a finalized version of 1, **When** the catalog is read, **Then** the
   feature is listed as enabled and dormant, and the reason names the family, version 2 and the gate's status.
2. **Given** the same state, **When** Attention is read, **Then** one `Info` item names the feature and the reason.
3. **Given** version 2 finalized, **When** the catalog and Attention are read after the host has observed it,
   **Then** the feature is available and the Attention item is gone.

---

### User Story 2 - A write that needs dormant data is refused, never dropped (Priority: P1)

Before finalization, a client sends a request that sets a field only version 2 can store. The API answers 409 with a
stable code and a message saying the field becomes available once every host can read the new version. Nothing is
saved. The same request without that field succeeds. After finalization the full request succeeds and the field
reads back.

**Why this priority**: Quietly discarding the field would be the silent data loss this program exists to prevent
(#2102). The request that omits the field must still work, or dormancy becomes an outage.

**Independent Test**: Run the three requests (with the field before finalization, without it before finalization, with
it after finalization) and compare the database after each.

**Acceptance Scenarios**:

1. **Given** version 1 finalized, **When** a request sets a version-2 field, **Then** the answer is 409 with the
   dormancy code, and the database is unchanged.
2. **Given** version 1 finalized, **When** the same request omits that field, **Then** it succeeds, and the row is
   stamped `1`.
3. **Given** version 2 finalized and observed, **When** the request sets the field, **Then** it succeeds, the row is
   stamped `2`, and reading it returns the field.
4. **Given** a unit of work that writes several rows, one of which needs dormant data, **When** it runs, **Then** no
   row of that unit is written.

---

### User Story 3 - A feature leaves dormancy without a restart (Priority: P1)

Finalization happens while the hosts are serving traffic. On every host, the dormant feature becomes available within
one refresh interval, with no restart and no shell reload, on `Elsa.Foundation.Host` and `Elsa.Workbench` alike.

**Why this priority**: With automatic finalization, dormancy should last only as long as the rollout (ADR 0078). If
leaving it needed a restart, Workbench's no-op reloader would keep the feature dormant indefinitely, and nothing
would report that anything was wrong.

**Independent Test**: On each host kind, hold the version, confirm the feature refuses, release, and confirm the next
request after the refresh is served, with the process id unchanged.

**Acceptance Scenarios**:

1. **Given** a dormant feature on Workbench, **When** its family finalizes, **Then** the feature is served within one
   refresh interval, in the same process.
2. **Given** the same on Foundation.Host, **When** its family finalizes, **Then** the same holds, and no shell reload
   was needed.

---

### User Story 4 - A single host has no dormant window (Priority: P2)

A single host with the in-process membership provider is upgraded to version 2. Its families finalize when the module
activates (spec 181, FR-021), so the new feature serves the first request after startup.

**Why this priority**: On a single host, the window "closes immediately" (ADR 0078). A single-host user should never
see a dormant feature unless they placed a hold.

**Independent Test**: Start a single host at version 2 against a database finalized at 1, and send the new-data
request as the first request.

**Acceptance Scenarios**:

1. **Given** the in-process provider and no hold, **When** the first request needing version 2 arrives, **Then** it is
   served.
2. **Given** the in-process provider and a hold, **When** it arrives, **Then** it is refused, naming the hold.

---

### User Story 5 - Code that bypasses the check fails loudly (Priority: P2)

A developer adds engine code that sets a version-2 member on a runtime state object without asking the shared check.
Before finalization, the store refuses the write with the dormancy refusal, and a test fails. The member is never
silently left out of the stored row.

**Why this priority**: The request-level check covers the paths someone remembered. The store-level backstop (spec
180, FR-016) covers the rest, so a missed check fails loudly instead of losing data.

**Independent Test**: With version 1 finalized, have a test set a version-2 member and save through the store.

**Acceptance Scenarios**:

1. **Given** write version `1`, **When** a value carrying data in a version-2 member is saved, **Then** the dormancy
   refusal is raised and nothing is written.
2. **Given** write version `1`, **When** the same value with that member unset is saved, **Then** it is written at
   version 1.

---

### User Story 6 - A query that depends on new data is refused, not answered empty (Priority: P2)

A new feature lists executions filtered by a projection column introduced at version 2. Before finalization, and
after it until every existing row carries the new column, the query is refused as dormant. It is not answered with
the rows that happen to carry the column.

**Why this priority**: An empty or partial list looks like success. Rows are upgraded only when next written (spec
180, FR-014), so after finalization the new column stays empty on every row not yet rewritten.

**Independent Test**: Seed rows at version 1, finalize version 2, rewrite some rows, and issue the query.

**Acceptance Scenarios**:

1. **Given** rows at version 1 and version 2 finalized, **When** the query runs before completeness is established,
   **Then** it is refused, and the reason says existing records are still being upgraded.
2. **Given** completeness established, **When** the query runs, **Then** it answers from every row.

---

### Edge Cases

- **A feature that needs several families.** It is dormant until every declared requirement is met, and the reason
  lists each unmet one.
- **A feature that is dormant and not enabled.** The catalog shows it disabled. Dormancy is reported only for enabled
  features.
- **A host that has not yet observed a finalization.** Before refusing, it refreshes its observed version (FR-014),
  so a stale view does not refuse a request that is already allowed.
- **A request that mixes old and new data.** It is refused as a whole. Nothing is saved without the new data.
- **A host that cannot read the finalized version.** That is not dormancy. Spec 181 refuses the module (FR-015) or its
  writes (FR-012), and Attention reports it as critical.
- **Background work.** A recurring task that would derive new-version data skips that work while dormant and reports
  it. Nothing is lost, because the data is derived again after finalization. Externally supplied new-version data,
  such as a stimulus payload or an import, is refused to its sender, never discarded (FR-018).

## Requirements *(mandatory)*

### Functional Requirements

**Declaring dormancy**

- **FR-001**: A feature that needs new-version data MUST declare a dormancy requirement for each schema family it
  needs: the family and the minimum version. The declaration is static on the feature class, readable from assembly
  metadata in the style of `[UsesEfModule]`, and a feature may carry several.
- **FR-002**: An operation or request field that needs new-version data inside an otherwise available feature MUST
  ask the shared dormancy check where it accepts that data, before any write or other side effect.
- **FR-003**: There MUST be exactly one shared dormancy check (ADR 0078). It is a replacement contract under
  framework constitution §2.6.2, in a package free of EF Core and of any provider, so API and runtime code can call
  it. It answers from the host's observed finalized version (spec 181, FR-009 and FR-010), with no database round
  trip on the success path. Modules MUST NOT read the finalization record or compare versions themselves.
- **FR-004**: A feature is dormant while the observed finalized version of any family it declares is below the
  declared minimum. A hold keeps it dormant.
- **FR-005**: A feature whose operations need every row of a family to carry new-version data, such as a query or
  lookup over a projection introduced at version V, MUST declare that as well. Such a feature stays dormant after
  finalization until the family's completeness is established: no row below version V remains. How completeness is
  established — the scan or rewrite that proves it — is deferred to spec 180's Open Question 4. Until that question
  is answered, a feature that declares this requirement cannot leave dormancy for it.

**Composition**

- **FR-006**: Dormancy MUST NOT change composition. A dormant feature is composed as an available one is: its services
  are registered and its endpoints mapped. Only its behaviour is gated, at call time.
- **FR-007**: An API capability that exists only while the feature is available MUST be contributed through
  `IApiCapabilitySource` and omitted while dormant, not declared statically.

**Reporting**

- **FR-008**: A dormant feature MUST report a reason, and MUST NOT be shown as simply absent. The reason names each
  unmet requirement (family and version) and why it is unmet. There are three cases: waiting for hosts, stated as
  "available once every host can read the new version"; held by an operator, with the hold's reason; or waiting for
  existing records to be upgraded (FR-005).
- **FR-009**: The Modularity feature catalog MUST carry each enabled feature's availability (available or dormant)
  and reason, through an `IFeatureCatalogContributor`. That adds a member to `FeatureCatalogItem` in
  `Elsa.Modularity.Core` and to the catalog response. It MUST NOT reuse `ReadError`, which means a manifest failure
  and which `ModularityAttentionContributor` reports as critical.
- **FR-010**: An Attention contributor MUST report each dormant feature as `Info` with its reason, and a host refusing
  a family's writes under spec 181's FR-012 as `Critical`. It uses the existing `AttentionItem` shape, so
  `Elsa.Attention.Core`'s public API does not change. It also carries the gate's status from spec 181's FR-022.
- **FR-011**: Operator surfaces (the catalog, Attention and the CLI status) MAY name the members that cannot read a
  version yet. A domain API's dormancy refusal MUST NOT name hosts or otherwise reveal the fleet's topology.

**Refusing**

- **FR-012**: A write that needs data a dormant feature or field would hold MUST be refused before any row changes.
  When the unit of work writes several rows, none of them is written. The data is never dropped, and the request
  never succeeds without it.
- **FR-013**: The dormancy refusal MUST reuse spec 180's write refusal (Terms, FR-016a): the same exception type,
  unassignable to the same six types (`InvalidOperationException`, `ArgumentException`, `FormatException`,
  `NotSupportedException`, `JsonException` and `InvalidDataException`), carrying the same stable code. A
  feature-level dormancy refusal additionally carries the feature id when known and FR-008's reason; the store-level
  backstop (FR-017) carries the family and the two versions alone, as spec 180 defines it. A test pins the
  unassignability, as `EfSchemaVersionTests` does for the skew exception.
- **FR-014**: Before raising a dormancy refusal, the host MUST refresh its observed finalized version if its last
  refresh is older than a short, rate-limited bound. A request that finalization already allows is then not refused
  on a stale view.
- **FR-015**: Every domain API that can raise the refusal MUST map it to HTTP 409 in that API's own problem envelope,
  including the code, as `ModularityFaultRenderer` maps `FeatureActivationRefusedException` and
  `WorkflowPublishingFaultRenderer` maps its typed conflicts. An API that has not mapped it answers 500 through its
  default arm, never 2xx and never 400. Each mapping has a test.
- **FR-016**: While dormant, a read or query whose answer depends on data only the new version holds MUST be refused
  the same way, and not answered from rows that cannot contain that data.
- **FR-017**: Spec 180's FR-016 is the backstop. A store asked to write data in a member newer than its write version
  raises spec 180's write refusal (FR-016a), which catches any path that skipped FR-002.
- **FR-018**: Background work that would derive new-version data MUST ask the shared check each time it runs, skip
  that work while dormant, and report dormancy through FR-009 and FR-010. Work that carries externally supplied
  new-version data MUST be refused to its sender, never discarded.

**Leaving dormancy**

- **FR-019**: A feature MUST leave dormancy once its host observes the finalized version it needs (spec 181, FR-010),
  with no restart and no shell reload, on `Elsa.Foundation.Host` and `Elsa.Workbench` alike. The next call after the
  host observes it is served.
- **FR-020**: The catalog, Attention and `/capabilities` MUST reflect the change on their next request. Each is
  computed per request today.
- **FR-021**: On a single host with the in-process provider, where finalization happens at activation (spec 181,
  FR-021), a feature MUST have no dormant window unless a hold or FR-005 applies.

### Key Entities

- **Dormancy requirement**: a feature, a family and a minimum version, plus whether completeness is needed.
- **Availability**: available or dormant, with a reason per unmet requirement. It is shown in the catalog and in
  Attention.
- **Dormancy refusal**: spec 180's write refusal, extended for the feature-level case with the feature id and
  reason: code, family, required version, observed finalized version, feature, and reason.
- **Shared dormancy check**: the single replacement contract that answers from the observed finalized version.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Before finalization, a request carrying new-version data gets 409 with the dormancy code, and the
  database is unchanged. The same request without that data succeeds. After finalization the full request succeeds
  and the data reads back.
- **SC-002**: While dormant, the catalog lists the feature as enabled and dormant with a reason, and Attention holds
  one `Info` item for it. In no state is a dormant feature missing from the catalog.
- **SC-003**: After finalization, the feature is served within one refresh interval without a restart, on both host
  kinds, with the process unchanged.
- **SC-004**: On a single host with no hold, zero requests are refused as dormant after startup.
- **SC-005**: A test that sets a new-version member through engine code before finalization fails with the dormancy
  refusal, and no row is written with that member dropped.
- **SC-006**: The refusal type is unassignable to each of the six exception types FR-013 names, and every API that
  maps it answers 409.
- **SC-007**: A query that depends on completeness is refused before finalization, and after finalization until
  completeness is established. Until spec 180's Open Question 4 defines how completeness is established, such a
  query stays refused after finalization. It is never answered from part of the rows.

## Assumptions

- Spec 181 provides each host's observed finalized version with a bounded refresh, and its status for reporting.
- Domain APIs keep their own problem envelopes. This spec adds one typed exception for them to map, and no shared
  envelope.
- The feature catalog and Attention are evaluated per request, as they are today.
- Migrations are regenerated until 4.0 ships as a stable release, per
  [spec 180's Assumptions](../180-schema-upcaster-chain/spec.md#assumptions), so no feature is dormant before the
  first schema-changing release after 4.0.

## Dependencies

- **Spec 181** (B5, #2101): the finalized version, holds, the refresh, and the gate's status.
- **Spec 180** (B4, #2100): schema families, write versions, the write refusal this spec reuses (FR-016a), and the
  store-level backstop (FR-016). FR-005 also depends on spec 180's Open Question 4 (the completeness scan), which is
  not answered yet.
- **B1** ([#2097](https://github.com/elsa-workflows/elsa-foundation/issues/2097)) and **B3**
  ([#2099](https://github.com/elsa-workflows/elsa-foundation/issues/2099)), through spec 181. Membership has no spec
  yet.

## Out of Scope

- The finalization gate, holds and the refusal of hosts (spec 181), and the upcaster chain (spec 180).
- Membership (B1 to B3), version-aware placement (B7, [#2103](https://github.com/elsa-workflows/elsa-foundation/issues/2103))
  and the expand-only migration guard (B8, [#2104](https://github.com/elsa-workflows/elsa-foundation/issues/2104)).
- The scan and rewrite that establish completeness for FR-005 (spec 180, Open Question 4).
- Studio's presentation of availability. This spec fixes what the catalog and Attention carry, not how Studio shows
  it.

## Open Questions

1. **`/capabilities` for a dormant feature.** FR-007 omits its capability, because the document is meant to carry no
   "arbitrary module state" (API Capabilities README, "Composition"), and the reason reaches operators through the catalog and
   Attention. Should the document instead advertise the capability with a dormant status and a caller-neutral reason,
   so a client can explain a disabled control?
2. **Completeness.** ADR 0078 has new-data features wait for finalization only. FR-005 adds a second condition for
   features that query new data, because lazy upgrade leaves old rows without it. Should ADR 0078 be amended to say
   so, and which workstream builds the scan or rewrite that proves completeness (spec 180, Open Question 4)? Until
   spec 180's Open Question 4 is answered, a feature that declares FR-005's requirement cannot leave dormancy for it.
3. **Where the shared check lives.** ADR 0078 places membership at the foundation level, outside the workflow runtime.
   Should the shared check share that package, or live in an EF-free contract package of its own? Either satisfies
   FR-003.
4. **The code value.** This spec proposes `schema-version-not-finalized` as the stable code for the refusal. Is a
   repository-wide code vocabulary expected, or does each API own its codes, as Publishing's do?
