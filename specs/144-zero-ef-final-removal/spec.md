# Feature Specification: Zero-EF Final Removal

**Feature Branch**: `779-zero-ef-final-removal`

**Created**: 2026-07-26

**Status**: Draft

**Input**: User description: "Switch every reference host to Groundwork, delete every remaining direct and transitive EF Core dependency including diagnostics, ASP.NET Core Identity, and OpenIddict only after their prerequisite gates, preserve and rehost behavioral test coverage, replace the shrink-only EF ratchet with an absolute-zero guard that cannot be bypassed by omitted projects, reconcile program documentation, and close issue #647 as the final removal lane for parent #629."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Run a Coherent Groundwork-Only Host (Priority: P1)

As an application host, I can select any supported persistence provider once and have every enabled durable Elsa feature, including dashboard projections and authorization-server storage, use that same Groundwork provider without retaining an EF-backed fallback.

**Why this priority**: The reference host is the product proof that the provider boundary is coherent. Removing packages without a complete runnable host would create a nominal zero-EF result while silently dropping supported behavior.

**Independent Test**: Compose each supported provider shape from the reference-host configuration and prove that every enabled persistence contract resolves to the expected Groundwork implementation, no EF implementation resolves, and dashboard run-health/portfolio remains available.

**Acceptance Scenarios**:

1. **Given** the SQLite reference composition, **When** the host resolves every enabled durable persistence contract, **Then** all contracts resolve through Groundwork and no EF implementation or package is required.
2. **Given** the SQL Server, PostgreSQL, or MongoDB reference composition, **When** the dashboard and all durable features are enabled, **Then** one provider choice backs every lane and run-health/portfolio data remains available.
3. **Given** a provider lacks a required durable feature, **When** host composition is validated, **Then** startup fails with a specific readiness diagnostic rather than omitting the feature or falling back to another persistence family.

---

### User Story 2 - Remove EF Without Losing Behavior (Priority: P1)

As an Elsa maintainer, I can delete the temporary EF implementation and oracle families after their replacement gates pass while preserving every still-valid test objective and recording explicit approval for every genuinely provider-specific test removal.

**Why this priority**: EF is load-bearing until diagnostics, OpenIddict, provider conformance, and performance evidence complete. Test continuity is the primary protection against cleanup hiding missing behavior.

**Independent Test**: Start from a reviewed inventory of EF-owned source and tests, classify every affected test objective, then show that each objective either passes in an EF-independent home or has recorded architect approval for removal before the corresponding EF files are deleted.

**Acceptance Scenarios**:

1. **Given** a test that reaches EF directly or through shared host wiring, **When** its EF dependency is removed, **Then** the same subject and behavioral objective remain covered by a named EF-independent test.
2. **Given** a test that asserts only EF-specific metadata or mechanics, **When** it is proposed for deletion, **Then** the removal ledger records the objective, rationale, replacement evidence if any, architect, decision, and date.
3. **Given** diagnostics, OpenIddict, Identity, or the shared persistence substrate has not passed its required correctness and performance gates, **When** deletion is attempted, **Then** the work unit treats the affected EF oracle as non-removable.
4. **Given** all prerequisite gates have passed, **When** deletion completes in dependency order, **Then** no EF project, context, migration, registration, package, test fixture, benchmark oracle, or host setting remains.

---

### User Story 3 - Prevent EF From Returning (Priority: P2)

As a repository maintainer, I receive an immediate, actionable failure if any current or future project introduces EF directly, transitively, conditionally, through shared build imports, or through a project omitted from the main solution.

**Why this priority**: A one-time cleanup is not durable unless the repository continuously proves the complete graph remains at absolute zero.

**Independent Test**: Introduce one synthetic violation for each guarded category in an isolated repository fixture and show that the guard reports the exact project, file, package, or dependency path; also show that every category is empty in the real repository after a complete restore.

**Acceptance Scenarios**:

1. **Given** any repository project references an EF package directly or transitively, **When** the architecture guard scans the complete restored project graph, **Then** it fails and identifies the consumer and EF package.
2. **Given** an EF-consuming project is omitted from the main solution, **When** the guard runs, **Then** the violation is still detected.
3. **Given** a conditional or imported build reference introduces EF, **When** the complete evaluated restore is inspected, **Then** the violation is detected.
4. **Given** source, configuration, migrations, registrations, or project names reintroduce an EF surface, **When** the guard runs, **Then** it fails with the matching category and location.
5. **Given** one or more projects lack restored dependency assets, **When** the guard runs, **Then** it refuses to certify zero EF rather than treating missing evidence as empty.

---

### User Story 4 - Leave a Truthful Program Record (Priority: P3)

As a program owner, I can audit why the zero-EF program closed, which evidence proved it, which temporary artifacts were removed, and how future contributors must compose and operate Groundwork-only persistence.

**Why this priority**: Parent issue #629 and Project 33 must describe the state on `main`, not intentions or superseded migration-era wording.

**Independent Test**: Follow every completion link from the program goal, decision map, ADR, spec quickstart, issue summaries, and generated maps to immutable merge/evidence records and verify that none describes EF as a shipped implementation or OpenIddict as outside the completion gate.

**Acceptance Scenarios**:

1. **Given** the final merge is present on remote `main`, **When** a maintainer reads the program sources of truth, **Then** they consistently describe Groundwork as the only shipped durable implementation family and OpenIddict as included in the completed gate.
2. **Given** operators need to validate or apply storage schemas, **When** they follow the reference deployment documentation, **Then** they receive the supported Groundwork validation and deployment workflow without EF migration instructions.
3. **Given** all completion criteria pass on remote `main`, **When** issues #647 and #629 and their Project 33 items are closed, **Then** each closure records the verified merge SHA, evidence summary, and final zero-EF guard result.

### Edge Cases

- A project is not listed in `Elsa.Server.slnx` but still imports or resolves an EF package.
- A project gains EF only through conditional build properties, central package management, shared build targets, or another package.
- A source-text scan appears clean while restored dependency assets are stale, missing, or produced before the latest project change.
- A test inventory finds only files containing EF tokens and misses tests that reach EF through a shared fixture, host, or transitive project.
- A test's setup is provider-specific but its behavioral objective remains provider-neutral.
- A reference-host configuration is EF-free but silently omits a feature that was enabled under another provider.
- SQL Server or MongoDB composition enables the dashboard before run-health/portfolio support is available.
- The frozen Identity EF oracle or another benchmark oracle is modified or deleted before its last required comparison completes.
- A package or project name contains `EntityFrameworkCore` but is classified as source rather than a package dependency; the guard must still report it in the appropriate category.
- Generated maps are refreshed from a dirty or incomplete input graph and would otherwise publish stale architectural claims.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The final-removal work MUST NOT begin deleting an EF family until every correctness, provider, and performance gate that uses that family as an oracle has passed and is durably recorded.
- **FR-002**: The reference server and maintained sample/reference compositions MUST select one Groundwork provider for every enabled durable persistence lane.
- **FR-003**: SQLite, SQL Server, PostgreSQL, and transaction-capable MongoDB reference compositions MUST preserve the enabled persistence behavior required by the zero-EF program.
- **FR-004**: SQL Server and MongoDB compositions MUST preserve dashboard run-health and portfolio behavior through issue #932, unless the program owner separately ratifies and records an explicit non-support amendment.
- **FR-005**: Missing provider capability or schema readiness MUST fail explicitly; the host MUST NOT silently omit a required feature, fall back to EF, or substitute an in-memory implementation.
- **FR-006**: The work unit MUST maintain a complete, reviewed inventory of every remaining EF project, package, project reference, transitive consumer, migration, context, registration, host setting, test dependency, tool, and benchmark oracle before deletion.
- **FR-007**: EF families MUST be removed in dependency order: diagnostics and OpenIddict after their lanes pass, Identity after its benchmark oracle obligation passes, the shared EF substrate after all dependents are gone, and central packages last.
- **FR-008**: The OpenIddict EF implementation and dependency surface MUST be inside the completion gate; describing OpenIddict as a separate delivery lane MUST NOT exclude it from final zero-EF completion.
- **FR-009**: Every affected test method MUST appear in a test-retention ledger, including tests that reach EF through shared host or fixture infrastructure rather than direct source tokens.
- **FR-010**: Every still-valid behavioral test objective MUST be preserved in an EF-independent test, even when setup, fixtures, wiring, or test-project location changes.
- **FR-011**: Removing a test whose objective no longer applies or is strictly EF-mechanism-specific MUST have explicit recorded architect approval and durable rationale.
- **FR-012**: The final repository MUST contain no EF projects, contexts, mappings, migrations, design-time factories, initializers, shell features, registrations, aliases, settings, package references, EF-only tests, EF-only tools, or temporary EF benchmark harness code.
- **FR-013**: The final repository's complete project graph MUST contain no direct or transitive package whose identity is `Microsoft.EntityFrameworkCore*`, `Microsoft.AspNetCore.Identity.EntityFrameworkCore`, `OpenIddict.EntityFrameworkCore`, or another provider package that introduces EF.
- **FR-014**: The permanent architecture guard MUST scan every repository project rather than only projects reachable from a maintained solution.
- **FR-015**: The permanent architecture guard MUST evaluate central, shared, imported, conditional, direct, static-transitive, and restored-transitive dependency surfaces.
- **FR-016**: The permanent architecture guard MUST refuse certification when any repository project lacks current restored dependency evidence or when the discovered project set, dependency-affecting inputs, or assets do not match a discovery-driven all-project restore receipt.
- **FR-017**: The permanent architecture guard MUST assert absolute emptiness for every former ratchet category and report exact offending paths or dependency consumers.
- **FR-018**: The temporary shrink-only baseline and its update switch MUST be removed after the real repository reaches absolute zero; no mutable allow-list may remain.
- **FR-019**: The frozen ASP.NET Core Identity EF oracle baseline and its ratchet test MUST be removed only in the same reviewed change that removes the oracle after its final gate.
- **FR-020**: The zero-EF guard MUST include isolated tests proving that project omission, imported dependencies, missing assets, stale-but-present assets/restore receipts, source registrations, contexts, migrations, and host configuration cannot bypass it.
- **FR-021**: Groundwork schema validation and authorized application for reference deployments MUST be documented as the supported operational workflow.
- **FR-022**: Repository-wide build, test, package, startup, provider-conformance, and performance evidence required by issue #647 MUST pass before the work unit is declared complete.
- **FR-023**: The constitution, ADR-linked guidance, program goal, decision map, host documentation, feature documentation, extension-point catalogs, and generated architecture maps MUST describe the final Groundwork-only state without duplicating canonical explanations.
- **FR-024**: Generated maps MUST be refreshed from reviewed current inputs, and their findings MUST be reviewed before merge.
- **FR-025**: Three independent adversarial reviewers MUST inspect the exact final commit range on correctness/mechanism, evidence integrity, and scope/test preservation; confirmed findings MUST be remediated and re-verified before merge.
- **FR-026**: The change MUST land through the repository's organization-branch workflow using a merge commit, and remote `main` MUST be verified to contain the result before issue closure.
- **FR-027**: Issues #647 and #629 and every Project 33 item required by the program MUST remain open or incomplete until every acceptance criterion is proven on remote `main`.
- **FR-028**: Closure records for #647 and #629 MUST identify the merge SHA; link the retained correctness, provider, performance, test-retention, dependency-audit, and review evidence; and include an immutable closure ledger for every required child issue and Project 33 item.

### Key Entities

- **EF Surface Snapshot**: The complete categorized set of projects, dependencies, source artifacts, registrations, configurations, tests, tools, and transitive consumers that must reach zero.
- **Test-Retention Ledger Entry**: A test objective, its original subject and location, its provider-neutral replacement evidence or provider-specific rationale, and its recorded disposition.
- **Provider Composition**: One supported database choice and the complete set of durable features expected to resolve through it in a reference host.
- **Zero-EF Certification**: A fail-closed result produced from a complete evaluated project graph and source/configuration scan, with every guarded category empty.
- **Completion Evidence Record**: Immutable links and identifiers proving prerequisite gates, exact reviews, merge presence, and issue/project closure truth.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Every category in the current EF surface scoreboard reaches exactly zero: EF projects, direct package references, central package versions, shared build references, direct EF project references, static transitive project consumers, restored transitive package consumers, migrations, contexts, registrations, and host configurations.
- **SC-002**: 100% of repository projects have dependency evidence bound to the current discovery-driven all-project restore receipt when zero-EF certification runs; certification fails if even one project, dependency input, or assets file is missing, stale, changed, or unbound.
- **SC-003**: 100% of tests affected by EF removal have a reviewed ledger disposition, and 100% of still-valid behavioral objectives have named passing EF-independent evidence.
- **SC-004**: All four mandatory provider compositions pass the required correctness, tenancy, restart/recovery, diagnostics, Identity, OpenIddict, dashboard-enabled host, and schema-readiness gates.
- **SC-005**: Every coverage-ledger row required by the zero-EF program has an accepted performance verdict or a separately ratified workload-specific amendment with retained evidence.
- **SC-006**: Synthetic guard tests detect 100% of the defined bypass classes: omitted projects, direct packages, transitive packages, imported/conditional dependencies, missing assets, stale-but-present assets/restore receipts, migrations, contexts, registrations, and host configuration.
- **SC-007**: Repository search, complete restore, build, test, package, and maintained reference-host startup audits report zero EF dependencies or runtime registrations.
- **SC-008**: Three independent exact-range reviews report no unresolved blocker, and every confirmed finding has a recorded disposition and successful re-verification.
- **SC-009**: Remote `main` contains the reviewed merge commit before #647 closes, and #629 closes only after all six program completion conditions remain true on that remote head.
- **SC-010**: The program goal, decision map, constitution, operational documentation, generated maps, issue bodies, and Project 33 agree on the same completed Groundwork-only state with no stale exception for OpenIddict.

## Assumptions

- Issues #642, #643, #646, and #932 are mandatory dependencies; this work unit plans their consumption and deletion order but does not falsify completion while any required gate remains open.
- Groundwork is the only concrete durable persistence implementation family shipped from `elsa-foundation`; core persistence contracts remain Groundwork-free.
- The product is greenfield, so production EF-to-Groundwork data migration and a separate EF compatibility repository are out of scope.
- Existing EF implementations are temporary parity/performance oracles and remain frozen until their final consumers complete.
- The current shrink-only EF surface baseline is the authoritative intake inventory, but the final guard has no baseline or allow-list.
- The constitutions are still draft quality-gate documents; this work follows their refactoring and test-preservation rules while the targeted provider-language amendment remains part of the final documentation gate.
