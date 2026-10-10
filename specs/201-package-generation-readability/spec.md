# Feature Specification: Protect Package Generations

**Feature Branch**: `2339-package-generation-readability`

**Created**: 2026-10-08

**Status**: Draft — prepared; current integration qualification and stable acceptance remain open

**Input**: Foundation issue [#2164](https://github.com/elsa-workflows/elsa-foundation/issues/2164), which closes the remaining shell-build timing gap in [Spec 183](../183-cluster-membership/spec.md).

**Integration amendment (2026-10-09)**: Spec201 preserves the previously reviewed Spec198 requirements after a numbering collision with unrelated main work. Current preview integration uses qualified PR2529 `.173/.99` pins on the preserved branch and must retain #2522 replacement-history/fresh-only-retirement semantics. Historical `.166/.99` evidence below is not current-port acceptance; final stable requirements remain unchanged. See the dated [plan amendment](plan.md#current-integration-amendment--2026-10-09).

## User Scenarios & Testing

### User Story 1 - Protect the package generation selected by a shell build (Priority: P1)

When the host begins building a shell while an older package release is still loaded, the readability report must keep that release's schema constraints in force until every shell that selected it can no longer run its code. A refresh that commits a newer catalog during the build must not make the report forget the snapshot already selected by that candidate.

**Why this priority**: Prematurely removing an older declaration can allow schema finalization while a shell is still being composed from code that cannot read the finalized schema.

**Independent Test**: Start a candidate before its catalog selection is known, then let it select an older snapshot while a newer snapshot commits. Release the other shell that used the old snapshot. Verify that the old declaration remains counted until the candidate's provider teardown is confirmed, and only then can the report stop counting it if no other requirement in FR-021 holds it.

**Acceptance Scenarios**:

1. **Given** a candidate begins while the catalog selection is unresolved, **When** readability is evaluated before catalog selection, **Then** the candidate conservatively keeps every otherwise-retirable package generation that the unresolved selection could include.
2. **Given** a candidate selects a snapshot naming an older package generation, **When** the catalog commits a newer snapshot before the candidate's first initializer and another old shell drains, **Then** the candidate continues to keep the selected old generation counted.
3. **Given** the candidate's provider has finished disposing and the next-shell catalog no longer names the old generation, **When** the report is reevaluated, **Then** the old declaration stops counting only if all other retirement conditions in Spec 183 FR-021 are also satisfied.

---

### User Story 2 - Keep failed and overlapping candidates conservative (Priority: P1)

As a host operator, I need a candidate that fails during initialization, and each of several overlapping shell generations, to remain part of the readability calculation until its own provider is known to have finished teardown. A failed drain or uncertain disposal must never be treated as proof that no code from that generation can run.

**Why this priority**: Failed activation and concurrent reloads are routine lifecycle paths. Releasing a package generation on a failed transition can make schema finalization unsafe even though the old code remains reachable.

**Independent Test**: Exercise a candidate whose initialization fails after selecting an old snapshot, two candidates with different selected snapshots, and overlapping drains where one drain or provider disposal fails. Verify each generation retains its own pin and an uncertain teardown preserves the conservative readability result.

**Acceptance Scenarios**:

1. **Given** a candidate selected an old snapshot and an initializer fails, **When** the candidate is being torn down, **Then** that snapshot remains counted until the complete provider disposal is confirmed.
2. **Given** two shell generations select different snapshots, **When** one is drained and its provider is disposed, **Then** releasing that generation does not release the other generation's independent package-generation pin.
3. **Given** a drain or provider disposal reports failure or completion cannot be confirmed, **When** readability is evaluated, **Then** the affected package generation remains counted until positive evidence establishes that its provider has finished disposing.
4. **Given** a candidate build fails before creating a provider, **When** the build has unwound and the runtime confirms no candidate-owned provider or other state remains, **Then** that candidate's conservative pin may be released.

---

### User Story 3 - Reevaluate the report when catalog evidence changes (Priority: P1)

As a schema finalization gate, I need the host to publish a fresh readability report whenever a committed catalog selection changes the set of retired package declarations, whether that set grows or shrinks. Custom catalog behavior that cannot confirm a commit must remain conservative, and the host must release any notification subscriptions it owns when its lifetime ends.

**Why this priority**: A safe release that never reaches the report can block finalization indefinitely; an unsupported notification path that is assumed successful can permit unsafe finalization.

**Independent Test**: Commit catalog snapshots that retire and then reintroduce the exact same package assembly, verifying each changed retirement set is reported and an unchanged set is quiet. Repeat with a custom catalog that supplies no commit notification or cannot be read, then verify it does not cause optimistic retirement. End the owning host lifetime and verify its notifications are detached.

**Acceptance Scenarios**:

1. **Given** a supported catalog publishes a committed snapshot that no longer names a retired package generation, **When** affected shell providers have finished disposing, **Then** the host reevaluates and republishes its readability report without waiting for an unrelated publish.
2. **Given** a custom catalog does not expose commit notifications or its current snapshot cannot be read, **When** package generations are evaluated, **Then** the absence of that evidence does not release a generation that might still be selected.
3. **Given** a committed catalog reintroduces an exact assembly that was previously retired, **When** the retirement set shrinks, **Then** the host republishes the changed report; a commit that leaves the set unchanged does not trigger a redundant publication.
4. **Given** the owning host lifetime ends, **When** a catalog commit notification arrives afterward, **Then** it no longer invokes the host's readability tracking or retains its resources.

## Edge Cases

- The catalog has not yet been initialized when a shell build starts, or the selected snapshot cannot be obtained.
- A catalog refresh commits after a candidate starts but before its first initializer; the selected snapshot is older than the catalog's new current snapshot.
- A candidate fails before promotion, including failure while the provider is partially constructed or being disposed.
- A candidate build fails before creating a provider; conservative accounting ends only after the build has unwound and the runtime confirms no candidate-owned provider or state remains to dispose.
- Multiple candidates, active generations, and drains overlap; one teardown failure must not release another generation's pin.
- Nuplane's active package inventory cannot be read or does not provide positive replacement evidence.
- A catalog does not publish commit notifications, emits duplicate notifications, or is disposed while tracking is being cleaned up.
- Provider teardown fails, is interrupted by host shutdown, or otherwise has no confirmed completion.
- A committed catalog change reintroduces a previously retired exact assembly, shrinking the retirement set; duplicate or unchanged committed snapshots leave the retirement set unchanged.

## Requirements

### Functional Requirements

- **FR-001**: This work MUST preserve the readability, replacement, and retirement meanings in Spec 183 FR-021. It changes when a shell generation starts counting and how its selected catalog is known; it MUST NOT redefine the declared families, readable-set intersection, or positive package-replacement evidence.
- **FR-002**: A candidate shell generation MUST count before its catalog selection is read. While selection is unresolved, the host MUST conservatively count every otherwise-retirable package generation that the unresolved selection could name.
- **FR-003**: Once a candidate's exact selected catalog snapshot is known, its readability pin MUST reflect that snapshot, including features that are disabled for the candidate. A later catalog commit MUST NOT replace the snapshot used to account for a candidate already in progress.
- **FR-004**: A candidate that selected an older snapshot MUST continue to hold that snapshot's package generations even after the catalog advances and other generations release their pins.
- **FR-005**: A candidate that fails before a provider is created MUST remain counted until the runtime completes its pre-provider build unwind. If a provider was created, the candidate MUST remain counted through confirmed completion of that provider's full teardown. Failure to activate, drain, or dispose MUST NOT itself be treated as confirmation that the candidate can no longer run code.
- **FR-006**: Overlapping shell generations MUST be accounted for independently. Releasing one generation MUST NOT release another candidate's or shell's pin while that other provider can still run code.
- **FR-007**: If catalog selection, package replacement evidence, generation identity, or provider disposal is unavailable, failed, or ambiguous, the host MUST retain the conservative readability result until sufficient positive evidence is available.
- **FR-008**: A committed catalog change that changes the set of retired declarations MUST cause readability to be reevaluated. When that change retires declarations, the updated report MUST be published after affected provider teardown completes and outside provider disposal. When it reintroduces previously retired declarations, the updated readability constraints MUST be published after commit without waiting for a live candidate or provider to tear down. A committed snapshot that leaves the retirement set unchanged MUST NOT cause redundant publication.
- **FR-009**: Catalog commit notifications MAY prompt reevaluation only after the snapshot is committed. A custom catalog without commit notifications MUST use a conservative fallback; missing or failed notification evidence MUST NOT be interpreted as proof that a package generation is no longer selected.
- **FR-010**: Any catalog notification subscription owned by the host's readability tracking MUST be detached when the owning host lifetime ends, so later notifications do not call inactive tracking or keep it alive. The tracker itself is not required to expose a disposal API.
- **FR-011**: This work MUST make no claim that package assemblies are unloaded, collectible, or safe to delete. Readability retirement remains distinct from Nuplane package unloading and from the separately tracked package-pruning work.
- **FR-012**: Before this work is marked Done or merged, final qualification MUST run the actual Foundation host against published stable CShells and Nuplane packages selected by the live D7 release plan, using upstream NuGet package references rather than upstream source-project substitutions, and exercise generation-readability behavior. Foundation's own source project references remain valid. A source-only upstream build or private preview package does not satisfy this final gate.

### Key Entities

- **Package generation**: A loaded package declaration and the code it contributes, governed by the canonical meaning and replacement evidence in Spec 183 FR-021.
- **Candidate shell generation**: A shell provider being constructed that may run features from its selected catalog snapshot, whether or not activation later succeeds.
- **Selected catalog snapshot**: The exact set of feature assemblies selected for one candidate; it can differ from the catalog's later current snapshot.
- **Readability report**: The host's existing report consumed by schema finalization; this work changes the evidence available to it, not its domain meaning.

## Success Criteria

### Measurable Outcomes

- **SC-001**: A deterministic regression covering the old-selected-candidate race proves that the older declaration remains counted after the catalog advances and other pins release, and stops counting only after the candidate provider's teardown is confirmed and every Spec 183 FR-021 condition holds.
- **SC-002**: Deterministic regressions for failed candidate activation, overlapping generations, drain failure, and provider-disposal failure show no premature readability increase in every case.
- **SC-003**: Deterministic regressions prove that a committed catalog snapshot republishes when the retirement set grows or shrinks, while a committed snapshot that leaves the set unchanged stays quiet; custom catalogs without reliable commit evidence never cause optimistic retirement.
- **SC-004**: Before Done or merge, the actual Foundation host passes qualification against the published stable CShells and Nuplane package versions refreshed from [D7](../../docs/plans/modular-hosting-upstream/decisions.md) and the [modular-hosting release plan](../../docs/reports/modular-hosting-release-plan.md), using upstream NuGet package references and verifying readability before and after generation retirement.

## Assumptions

- Spec 183 FR-021 remains the canonical source for readability semantics; this work makes a narrow timing amendment so a generation counts before catalog selection and then against the exact selected snapshot.
- CShells #146/#147 provide public catalog-commit and build-generation evidence in their preview families. A private preflight copy may use CShells `0.0.30-preview.166` and Nuplane `0.0.11-preview.99` with its own package pins and lock files; those preview pins and locks will not be committed as final Foundation adoption.
- Final Done/merge status uses the published stable CShells and Nuplane versions selected by the live D7 release plan, refreshed immediately before final qualification; preview preflight alone is insufficient.
- The host's existing package-readability tracker and registration are the scope of the implementation work; host and Workbench composition (#2354), shared Nuplane observer/provider adoption (#2314), package assembly unloading (#2362), and package pruning (#108) remain separately owned.

## Scope Boundaries

In scope are package-generation readability tracking, its existing host registration, the catalog/generation evidence needed by that tracker, and deterministic regressions for candidate selection, failure, overlap, teardown, and report reevaluation. Out of scope are changes to host composition or observer ownership, Nuplane store mutation, physical package unload/prune guarantees, Foundation package pins and lock files during preview preflight, and finalization-gate policy changes.
