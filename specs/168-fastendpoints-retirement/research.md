# Phase 0 Research: Final FastEndpoints Retirement

## R-001: What evidence may justify each removal?

**Decision**: Every removal is justified by a *removal-then-gate* result, never by the text scan that found the candidate. For each unit of removal: delete it, then build the solution and run the affected suites. The passing build and suite is the evidence. Where the removed thing is itself a guard, the evidence is instead an explicit record naming the gate that replaced it, or an explicit record that none did.

**Rationale**: Framework constitution §2.25.3 is unambiguous: "A static census, a text search, or a similarity judgement is not sufficient evidence to remove anything." The ~46-file figure in the spec came from `grep`. Under §2.25.3 that figure may direct attention, and may not authorize a single deletion. This is the governing constraint of the whole unit, and it is the reason removals are batched by category with a gate run per batch rather than performed in one sweep.

**Alternatives considered**:
- *One big removal commit, one gate run at the end.* Rejected: a red gate would not say which of several dozen deletions caused it, so the evidence would not attach to any individual removal.
- *Trusting the compiler alone.* Rejected: it catches code references but not reflection, configuration, or package-consumer reachability, which is precisely the failure mode §2.25.3 describes. Configuration reconciliation (FR-007) exists because `docker/compose/elsa-workbench.shells.json` references a feature by *string name*, invisible to the compiler.

## R-002: Does this unit have standing to retire artifacts?

**Decision**: Yes, and it must therefore meet §2.25's reporting bar in full. The completion report must record both what was retired and **what was examined and deliberately kept**, not just the removals.

**Rationale**: §2.25.1 states a consolidation review "is the only unit with standing to *retire* artifacts". This unit retires artifacts, so it is exercising that standing and inherits the obligations attached to it. §2.25.4 requires both lists. The spec's FR-010 asked only for removal evidence; the kept list is an additional obligation the constitution imposes, and it is the more valuable half, because it is what stops a later review re-deriving the same conclusions about the preserved authorization guards.

**Alternatives considered**:
- *Treating this as an ordinary feature unit with no §2.25 obligations.* Rejected: the unit deletes projects, guard tests, and a public shell feature. That is retirement regardless of the issue's title.

## R-003: Deleting the coexistence oracles against §2.25.2

**Decision**: Proceed with deletion as the maintainer decided, and record it in the completion report as a removal **not** covered by §2.25.2's standing clause, with the guarantee thereby left unguarded named explicitly.

**Rationale**: §2.25.2 grants standing to "delete a guard test whose gate has been superseded, **provided the report names the gate that replaced it**". For the four coexistence oracles no gate replaces them: after this unit nothing asserts that a third party can mount FastEndpoints beside first-party Minimal APIs. So the clause's precondition is not met. The maintainer decided to delete them anyway, which is their call to make, and the honest handling is to record the gap rather than to imply the clause covers it. This is carried into the plan's Complexity Tracking table.

**Alternatives considered**:
- *Re-anchoring the oracles onto a third-party endpoint.* This was recommended and declined; it would have satisfied §2.25.2 by keeping the gate rather than needing a replacement.
- *Claiming the architecture suite supersedes them.* Rejected as untrue: the architecture suite asserts the first-party surface is **empty**, which is the opposite assertion. It proves Elsa does not use FastEndpoints; it does not prove a third party still can.

## R-004: Disposition of the retirement guard itself

**Decision**: `FastEndpointsTransitionTests` and its supporting `TransitionExceptionValidator` are classified **Preserve**, and the unit must leave a passing guard proving the first-party registration surface is empty.

**Rationale**: This is the mechanism that proves the retirement succeeded. Retiring it in the same unit that performs the retirement would remove the evidence for the unit's own headline claim, and would let a future regression reintroduce a first-party FastEndpoints endpoint undetected. Note the validator scans for registrations; it does not require Elsa to *use* FastEndpoints, so it survives removal of the shared infrastructure. Whether it can survive removal of the *package reference* from `Elsa.Architecture.Tests` is an open question resolved by R-005.

**Alternatives considered**:
- *Retiring the guard because "the code is gone so it cannot regress".* Rejected: this is the reasoning §2.25.3 warns against, and it is falsified by the config drift this unit already found, where a removed surface was still referenced by name.

## R-005: Can `CShells.FastEndpoints` package references be removed everywhere?

**Decision**: Open, and deliberately left to the classification pass rather than guessed here. The plan treats each surviving package reference as its own classification entry, and reconciles references against dispositions after code removal, verified by build.

**Rationale**: The retirement guard must still be able to *discover* FastEndpoints registrations in order to prove there are none, which may require the package (or its abstractions) to remain referenced by the architecture test project even after all first-party usage is gone. Asserting either answer now would be a similarity judgement of exactly the kind §2.25.3 excludes. The build is the instrument that answers it.

**Alternatives considered**:
- *Declaring "remove all package references" up front.* Rejected: it would likely break the guard from R-004, and would be an inference rather than evidence.

## R-006: Archival approach for the frozen before evidence

**Decision**: Retain the frozen baseline JSON files as committed evidence; classify the *capture projects and tools* that regenerate them separately, since those carry the compile-time dependency on FastEndpoints. Record the disposition of each in the completion report with a reason.

**Rationale**: The baselines are inert data with continuing investigative value and no dependency cost. The capture projects are live code that must compile against FastEndpoints and the historical endpoints, so they are what actually holds the dependency alive. Separating the two lets the evidence survive even where its regenerator does not, which is the substance of "archived": the record remains readable, but is no longer reproducible.

**Alternatives considered**:
- *Deleting baselines with their capture tools.* Rejected: it would destroy the historical wire evidence the whole program produced.
- *Keeping both indefinitely.* Rejected: the capture tools are the last first-party compile-time consumers of FastEndpoints, so keeping them keeps the dependency this unit exists to remove. If they must be kept, that is a finding for the report, not a silent outcome.

## R-007: Configuration reconciliation

**Decision**: Remove the `FastEndpoints` entry from `docker/compose/elsa-workbench.shells.json`, and reconcile the `CShells.FastEndpoints.Abstractions` entry in `src/Apps/Elsa.Foundation.Host/appsettings.json` against what the host actually loads. Verify by activating the composition, not by reading the file.

**Rationale**: The source Workbench `shells.json` and `shells.baseline.json` no longer enable the feature; only the Docker composition still does, so the two have drifted. String-keyed configuration is invisible to the compiler, which makes it exactly the class of reference §2.25.3 says a census cannot clear. Activation is the instrument.

**Alternatives considered**:
- *Leaving the Docker entry as harmless.* Rejected: it becomes a reference to a non-existent feature once the package goes, and shipping a composition that names a removed feature is a defect for operators.

## R-008: Stale prose sweep

**Decision**: Treat comment and document references to removed types as a required, separately verified task, covering at minimum the `PermissionNames` mention in `IdentitySeeder`.

**Rationale**: Wave 8 shipped this defect class twice and no gate caught either instance; both were found by reading the diff. The compiler is silent on prose, so the only instrument is a search performed *after* removal, followed by human reading. Note the tension with §2.25.3: a text search is not sufficient evidence to *remove code*, but it is the appropriate instrument for *finding stale prose*, because prose has no build-time reachability to check. The two uses are distinct and the plan keeps them distinct.

## Resolved unknowns

No `NEEDS CLARIFICATION` markers remain. R-005 is recorded as deliberately open, to be resolved by evidence during execution rather than by assumption at planning time; that is a decision, not an unresolved clarification.
