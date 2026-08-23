# Feature Specification: Final FastEndpoints Retirement

**Feature Branch**: `claude/1376-fastendpoints-retirement`

**Created**: 2026-08-18

**Status**: Draft

**Input**: Issue [#1376](https://github.com/elsa-workflows/elsa-foundation/issues/1376), the final work unit of program [#1342](https://github.com/elsa-workflows/elsa-foundation/issues/1342). Remove FastEndpoints from the first-party REST authoring and runtime path now that every migration wave and foundation track has landed.

## Context and preconditions

All hard blockers named by #1376 are closed. Waves 0-7 (#1364, #1367-#1373), Wave 8 (#1374, merged as `abc262aa5`), Wave 9 (#1375), foundation track H (#1365), and foundation track D (#1366) are Done.

Three preconditions were verified against `main` at `06240f95a` before this spec was written:

1. No `src/` project references `Elsa.Api.FastEndpoints.csproj`, and the only `using Elsa.Api.FastEndpoints` lines inside `src/` are within that project itself. The shared infrastructure has no first-party production consumer.
2. `tests/Elsa/Architecture/Baselines/fastendpoints-transition-exceptions.json` is `[]`. The 164 original registrations have fully reconciled and no approved transition exception remains to reinterpret.
3. `FastEndpointsTransitionTests` requires an empty discovered first-party surface and passes on `main`.

**The surface is larger than the issue checklist implies, and that is the central risk of this unit.** A scan of `tests/` finds roughly 46 files referencing FastEndpoints types, not the small enumerated set. They are not interchangeable: some exist to test code being deleted, some are frozen compatibility evidence, and some are exactly the authorization and security guards the checklist requires preserving. Deleting by name match would remove guards this program was built to protect.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Every remaining FastEndpoints reference is classified before anything is deleted (Priority: P1)

A maintainer needs a single, reviewable classification of every remaining FastEndpoints reference, each assigned to exactly one disposition with a stated reason, before any removal happens. Without it, removal is guesswork over a surface too wide to hold in one head.

**Why this priority**: This is the only story that makes the rest safe. Every later story consumes its output, and the failure mode it prevents — silently deleting a preserved guard — is the one failure this unit cannot detect after the fact, because the deleted test cannot fail.

**Independent Test**: Produce the classification with every reference assigned a disposition and reason, and verify the four disposition sets are disjoint and together cover every reference found. Delivers value on its own: it is the reviewable artifact the maintainer approves before code changes.

**Acceptance Scenarios**:

1. **Given** the repository at the unit's base commit, **When** the classification is produced, **Then** every file and project referencing FastEndpoints appears exactly once with a disposition of Remove, Preserve, Archive, or Re-anchor, and a one-line reason.
2. **Given** the classification, **When** a reviewer checks a Preserve entry, **Then** the reason names the guarantee that entry protects, not merely that it compiles.
3. **Given** the classification, **When** any reference cannot be confidently assigned, **Then** it is listed as unresolved and blocks removal of that reference rather than defaulting to Remove.

---

### User Story 2 - The shared first-party FastEndpoints infrastructure is gone (Priority: P1)

An Elsa maintainer reading `src/` finds no first-party FastEndpoints endpoint base classes, configurators, filters, or security feature, because nothing first-party uses them.

**Why this priority**: This is the outcome the program exists to reach, and precondition 1 shows it is already unreferenced by production code, so it is the lowest-risk substantial removal.

**Independent Test**: Delete the infrastructure and its own test project, then build and run the full suite. Delivers the program's headline outcome.

**Acceptance Scenarios**:

1. **Given** the infrastructure is removed, **When** the solution builds, **Then** it builds with no error and no new warning.
2. **Given** the infrastructure is removed, **When** a maintainer searches `src/` for FastEndpoints, **Then** only references classified Preserve remain, each traceable to a classification entry.
3. **Given** the infrastructure is removed, **When** the architecture guard runs, **Then** the first-party registration surface is still empty.

---

### User Story 3 - Preserved guarantees still have working guards (Priority: P1)

A maintainer can still prove that endpoint authorization, Foundation Identity permission evaluation, and typed ownership and security metadata behave correctly, because those guards survived and still run.

**Why this priority**: Equal to P1 because the checklist requires preserving these, and a removal unit is precisely when they are most likely to be lost as collateral. A guard that disappears is indistinguishable from a guard that passes.

**Independent Test**: Compare the set of executed authorization and security tests before and after the change and confirm no named guard was lost without a recorded decision.

**Acceptance Scenarios**:

1. **Given** the unit is complete, **When** the authorization and security suites run, **Then** every guard classified Preserve executes and passes.
2. **Given** a guard classified Preserve depended on a first-party FastEndpoints endpoint, **When** the unit is complete, **Then** it has been re-anchored onto a surviving surface rather than deleted, and its assertion is unchanged in substance.
3. **Given** any preserved guard was retired instead, **When** a reviewer reads the record, **Then** the decision, the reason, and the guarantee thereby left unguarded are stated explicitly.

---

### User Story 4 - Configuration no longer references a removed feature (Priority: P2)

An operator starting a shipped Workbench composition does not enable a feature that no longer exists.

**Why this priority**: Below the removals because it is currently harmless, but it becomes a broken reference the moment the package goes, so it must land in the same unit.

**Independent Test**: Start the Docker Workbench composition and confirm it activates cleanly with no unresolved feature.

**Acceptance Scenarios**:

1. **Given** the Docker Workbench composition, **When** the shell activates, **Then** no enabled feature resolves to removed first-party code.
2. **Given** the Foundation Host assembly allowlist, **When** the host starts, **Then** it lists no entry for a package the repository no longer consumes first-party.
3. **Given** the source and Docker compositions, **When** they are compared, **Then** they no longer disagree about FastEndpoints.

---

### User Story 5 - Frozen compatibility evidence is archived deliberately, not incidentally (Priority: P2)

A maintainer investigating a future compatibility question can still find what the historical FastEndpoints endpoints returned, and can tell whether that evidence is live or archived.

**Why this priority**: The evidence retains investigative value after the code is gone, and the Wave 8 handoff explicitly holds it until this unit decides. It is P2 because it blocks nothing else.

**Independent Test**: Follow the archival record from the completion report to the evidence and confirm its status is unambiguous.

**Acceptance Scenarios**:

1. **Given** the before fixtures and capture tools, **When** this unit completes, **Then** each is either retained with a stated reason or archived with a stated reason, and none is left in an undeclared state.
2. **Given** an archived fixture, **When** a maintainer reads the record, **Then** it states what the fixture proved and why regenerating it is no longer possible or no longer required.

---

### User Story 6 - The program has a completion report (Priority: P3)

A maintainer or contributor arriving after the program can read one document explaining what changed across all waves, what was removed here, and what compatibility boundary remains.

**Why this priority**: Documentation of completed work; valuable but blocking nothing.

**Independent Test**: A reader unfamiliar with the program can answer what happened, what is gone, and what a third party can still rely on, from this document alone.

**Acceptance Scenarios**:

1. **Given** the completion report, **When** it is read, **Then** it states route and owner counts, removal evidence, residual third-party compatibility boundaries, risks, and rollback guidance.
2. **Given** the report, **When** a reader looks for the coexistence-guard decision, **Then** they find it recorded with its rationale and its consequence.

### Edge Cases

- A reference classified Remove turns out to be the only guard on a preserved guarantee. The classification review must catch this; if found during removal, the reference is reclassified and the classification artifact updated rather than the removal proceeding.
- `TransitionExceptionValidator` is the mechanism that discovers first-party registrations and proves the surface is empty. Retiring it would remove the proof that the retirement succeeded. It must be classified explicitly, and if retired, only after the guarantee it provides is either obsolete or relocated.
- A test project's package reference survives while all its FastEndpoints code is deleted, leaving a dependency nothing uses. Package references must be reconciled against the classification, not left behind.
- Removing the `ApiSecurity` shell feature removes a publicly nameable feature. The repository is pre-release and carries no back-compatibility obligation, but the removal is still a surface change and belongs in the report.
- A comment or document names a removed type without referencing it in code, so the compiler stays silent. Wave 8 shipped this defect class twice, so a comment and document sweep is a required task rather than an optional cleanup.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The unit MUST produce a classification assigning every remaining FastEndpoints reference to exactly one of Remove, Preserve, Archive, or Re-anchor, each with a stated reason, and MUST record it in the repository as a reviewable artifact before any removal.
- **FR-002**: The unit MUST NOT remove any reference not classified Remove or Archive.
- **FR-003**: The unit MUST remove the shared first-party FastEndpoints infrastructure and the test project whose sole purpose is testing it.
- **FR-004**: The unit MUST remove the four coexistence oracles as transitional, per the maintainer decision recorded on #1376.
- **FR-005**: The unit MUST leave the first-party FastEndpoints registration surface empty and MUST retain a guard proving it, or record explicitly why no such guard is needed once the code is gone.
- **FR-006**: The unit MUST preserve explicit Minimal API module mapping, Foundation Identity permission evaluation, and typed ownership and security metadata, together with the guards that prove them.
- **FR-007**: The unit MUST reconcile configuration so that no shipped composition or allowlist references removed first-party code or an unconsumed first-party package.
- **FR-008**: The unit MUST decide, for each immutable before fixture and capture tool, whether it is retained or archived, and MUST state the reason.
- **FR-009**: The unit MUST sweep comment and documentation references to removed types, including the `PermissionNames` mention in `IdentitySeeder`, so no surviving prose describes a type that no longer exists.
- **FR-010**: The unit MUST publish a completion report covering route and owner counts, removal evidence, residual third-party compatibility boundaries, risks, and rollback guidance.
- **FR-011**: The unit MUST record that automated coverage of mixed-host coexistence is withdrawn by decision, distinguishing the capability, which is preserved by construction, from the guard, which is not.
- **FR-012**: The unit MUST be a dedicated final-removal change and MUST NOT contain migration implementation for any wave.
- **FR-013**: The unit MUST pass build, architecture, generated maps, packaging, security, HTTP and OpenAPI, and diff-review gates, and MUST verify post-merge main gates on the exact merged commit.

### Key Entities

- **FastEndpoints reference**: Any project reference, package reference, code usage, configuration entry, or prose mention of FastEndpoints. Carries exactly one disposition and one reason.
- **Disposition**: One of Remove (transitional, no third-party compatibility purpose), Preserve (guards a guarantee that outlives the program), Archive (frozen evidence retained for investigation but no longer regenerated), or Re-anchor (guards a preserved guarantee but currently depends on a removed surface).
- **Retirement guard**: The mechanism proving the first-party registration surface is empty. Its own disposition is a decision this unit must make consciously.
- **Completion report**: The program's closing record under `docs/reports/`.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: The first-party FastEndpoints registration surface is 0, unchanged from the state this unit inherits, and no first-party endpoint regresses to FastEndpoints.
- **SC-002**: 100% of remaining FastEndpoints references are classified with a disposition and a reason, with 0 unresolved at merge.
- **SC-003**: 0 guards classified Preserve are absent from the post-change test run, measured by comparing executed test names before and after, not by a green summary line.
- **SC-004**: 0 shipped compositions or allowlists reference removed first-party code or an unconsumed first-party package.
- **SC-005**: 0 surviving comments or documents describe a removed type.
- **SC-006**: A reader unfamiliar with the program can determine, from the completion report alone, what a third-party consumer may still rely on.
- **SC-007**: All required gates are green on the exact merged commit, and no `main is red` issue is filed against it.

## Assumptions

- The maintainer decision to delete the four coexistence oracles as transitional stands. It was offered against a recommendation to re-anchor them, and is recorded on #1376 with its consequence.
- Mixed-host coexistence remains supported as a capability. This unit removes Elsa's own use of FastEndpoints and introduces nothing that prevents a third party from mounting FastEndpoints beside first-party Minimal APIs.
- The repository is pre-release, so removals need no compatibility shim or deprecation period.
- Framework constitution §2.24 and Elsa constitution §E2.9 remain provisional and are not treated as newly ratified by this unit. [ADR 0068](../../docs/adr/0068-first-party-rest-apis-use-aspnet-core-minimal-apis.md) is the accepted decision this unit completes.
- The roughly 46 referencing test files are an approximate starting count from a text scan, not the authoritative set. FR-001's classification establishes the authoritative set.
