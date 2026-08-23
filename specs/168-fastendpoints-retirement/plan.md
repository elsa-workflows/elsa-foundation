# Implementation Plan: Final FastEndpoints Retirement

**Branch**: `claude/1376-fastendpoints-retirement` | **Date**: 2026-08-18 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/168-fastendpoints-retirement/spec.md`

## Summary

Remove FastEndpoints from the first-party REST path now that every wave and foundation track has
landed, and close program #1342 with a completion report.

The approach is classification-first. A text scan finds roughly 46 referencing test files plus the
shared infrastructure, configuration entries, and prose, but framework constitution §2.25.3 forbids
removing anything on the strength of a scan. So the scan produces a *candidate set*, each candidate
gets a disposition with a reason, and each removal is then justified by a build-and-suite result.
Removals proceed in batches by category so that a red gate attaches to a specific deletion rather
than to a sweep.

Two findings shape the plan beyond the issue's checklist. First, the retirement guard
(`FastEndpointsTransitionTests` with `TransitionExceptionValidator`) is the mechanism that proves the
first-party surface is empty; it is preserved, because retiring it would delete the proof that the
unit succeeded. Second, deleting the four coexistence oracles is not covered by §2.25.2's standing
clause, since no gate replaces them; that gap is recorded rather than papered over.

## Technical Context

**Language/Version**: C# on .NET 10

**Primary Dependencies**: ASP.NET Core Minimal APIs (the destination), CShells feature composition,
`CShells.FastEndpoints` and `CShells.FastEndpoints.Abstractions` (the dependencies being retired)

**Storage**: N/A — this unit removes code, configuration, and prose; it touches no persisted state
and requires no migration

**Testing**: xUnit. Governing suites are `Elsa.Architecture.Tests` (the retirement guard and the
authorization/composition guards) plus the per-module API test projects

**Target Platform**: Linux/macOS/Windows server hosts; Docker Workbench composition

**Project Type**: Modular monolith transitioning to a multi-package framework; subtractive work unit

**Performance Goals**: N/A — no runtime behavior is intended to change

**Constraints**: Constitution §2.25.3 evidence bar; the first-party registration surface must remain
0 throughout; preserved authorization and security guards must keep running with assertions intact

**Scale/Scope**: ~46 candidate test files, one shared infrastructure project (22 files), one test
project dedicated to it, four coexistence oracles, two configuration entries, an unknown number of
prose references, plus capture tools and frozen baselines

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-checked after Phase 1 design.*

| Gate | Applies | Assessment |
|---|---|---|
| §2.25 Consolidation review — subtractive obligation | **Yes, governing** | This unit retires artifacts, so it exercises §2.25.1 standing and inherits its obligations. §2.25.3's evidence bar is the plan's central constraint (R-001). §2.25.4's two-list report is an obligation the spec did not state; it is added here (R-002). |
| §2.25.2 Standing to delete a guard test | **Partially unmet** | Deleting the coexistence oracles requires the report to name the replacing gate. None replaces them. Recorded in Complexity Tracking rather than treated as satisfied. |
| §2.21 / §2.23 Test discipline and guard tests | Yes | Preserved guards must keep executing with assertions unchanged. SC-003 measures by executed test *names*, because a deleted guard and a passing guard both produce green output. |
| §2.16 Refactor-cost test / §2.16.1 exemptions | Yes | Project deletion, not project splitting. NuGet identity concerns are limited to packages ceasing to be produced, which the report must state. |
| §2.11 DependsOn | Yes | Removing the `ApiSecurity` shell feature and the Docker `FastEndpoints` entry changes feature composition; verified by activation, not by reading config. |
| §2.13 Packaging and versioning | Yes | `Elsa.Api.FastEndpoints` stops being produced. Pre-release, so no compatibility shim is owed, but the report must record the surface change. |
| §2.24 Sanctioned patterns — closed catalog | Provisional | Carries its own `Status:` line and is excluded from document-level ratification. Not treated as newly binding by this unit. |
| §E2.9 `WorkflowDefinitionState` scope policy | Provisional | Not touched by this unit. |
| §E6 Type-naming rules | Minimal | No new types are introduced. |
| ADR 0068 | Yes | This unit completes the accepted decision that first-party REST APIs use Minimal APIs. |

**Initial gate result**: PASS with one recorded deviation (§2.25.2), carried in Complexity Tracking.

**Post-Phase-1 re-check**: PASS. Phase 1 strengthened compliance rather than weakening it:
`data-model.md` encodes the §2.25.3 evidence bar as per-kind admissibility rules (V-5), and the
completion report entity now carries the §2.25.4 "examined and deliberately kept" list. The §2.25.2
deviation is unchanged, and remains a maintainer decision rather than a planning choice.

## Project Structure

### Documentation (this feature)

```text
specs/168-fastendpoints-retirement/
├── spec.md              # Feature specification
├── plan.md              # This file
├── research.md          # Phase 0 output — R-001..R-008
├── data-model.md        # Phase 1 output — classification artifact model
├── quickstart.md        # Phase 1 output — execution and verification recipe
├── checklists/
│   └── requirements.md  # Spec quality checklist
└── tasks.md             # Phase 2 output (/speckit-tasks — not created here)
```

### Source code (repository root)

Directories this unit touches. Everything listed is a removal, a reconciliation, or a report; nothing
here is new production code.

```text
src/Elsa/Api/FastEndpoints/                    # shared infrastructure — removal candidate (22 files)
src/Elsa/Foundation/Identity/                  # PermissionNames prose reference — sweep
src/Apps/Elsa.Foundation.Host/appsettings.json # assembly allowlist entry — reconcile
docker/compose/elsa-workbench.shells.json      # FastEndpoints feature entry — reconcile

tests/Elsa/Api/FastEndpoints/Tests/            # tests of the removed infrastructure — removal candidate
tests/Elsa/Api/Compatibility/Testing/          # TransitionExceptionValidator — PRESERVE (retirement guard)
tests/Elsa/Architecture/                       # retirement guard + authorization guards — mixed, mostly PRESERVE
tests/Elsa/{Secrets,Studio/Preferences,Diagnostics/StructuredLogs}/  # coexistence oracles — removal per decision
tests/**/Capture/, tests/**/Baselines/         # frozen evidence — archival decision

tools/compatibility/RuntimeFastEndpointsCapture/          # capture tool — archival decision
tools/compatibility/WorkflowsDesignFastEndpointsCapture/  # capture tool — archival decision

docs/reports/                                  # completion report — new
docs/maps/                                     # regenerated after project removal
```

**Structure Decision**: No new structure. The unit deletes from the existing tree and reconciles
configuration against it. The one artifact it *creates* is the completion report, which lives with
the program's other reports under `docs/reports/`.

## Execution phases

1. **Establish and classify the candidate set.** Scan, then assign a disposition and reason to every
   reference. Ends with zero `Unresolved`. Produces the reviewable checkpoint. No deletions.
2. **Capture the guard before-state.** Record executed test names per affected suite, so SC-003 can
   be measured by diff rather than by a green summary.
3. **Remove by batch, gating each.** Infrastructure and its own test project; then the coexistence
   oracles; then package references. Build plus affected suites after each batch, with the retirement
   guard asserted every time.
4. **Reconcile configuration.** Docker composition and host allowlist, verified by activation.
5. **Decide and execute archival.** Baselines and capture tools classified separately, per R-006.
6. **Sweep stale prose.** After removal, search and read.
7. **Regenerate maps, publish the completion report, close the program.** Both §2.25.4 lists.

## Complexity Tracking

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| §2.25.2 requires that deleting a guard test name the gate that replaced it. The four coexistence oracles are deleted with no replacing gate. | The maintainer decided to delete them as transitional rather than re-anchor them onto a third-party endpoint. That is their decision to make, and it is recorded on #1376 with its consequence. | Re-anchoring the oracles onto a third-party FastEndpoints endpoint was recommended and declined. It would have satisfied §2.25.2 by preserving the gate outright, so no replacement would have been needed. The capability survives by construction; only its automated guard is withdrawn, and the completion report states this so a later regression has a dated record. |
