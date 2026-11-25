# Implementation Plan: Elsa 3 migration host

**Branch**: `agent/claude/3052-spec-essentials-images` | **Date**: 2026-11-25 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/1236-elsa3-migration-host/spec.md`

## Summary

Add one ASP.NET Core project, `Elsa3.Migration.Host`, in the Elsa 3 extension bucket. It composes, by project
reference, the Elsa 4 features the JTest acceptance set and the Studio designer need, plus JSON workflow and activity
reconciliation of a `migrated/` folder. Its README records why each feature is there, and a test project guards the
composition. Then the S-15 JTest acceptance set runs against the host, with the migration CLI's `migrated/` output as
its folder. There are no images, no new Studio app, and no Workbench change.

## Technical Context

**Language/Version**: C# / .NET 10; JSON for the composition and settings.

**Primary Dependencies**: CShells (shell composition), Nuplane (the engineer's own packages), JSON workflow and
activity reconciliation, main's OpenIddict store library (R-7), and `jtest` for acceptance (as in spec 1235).

**Storage**: SQLite in the host's git-ignored `data/` folder (R-6).

**Testing**: xunit. One new PR-tier test project with in-process host tests (R-5). The existing
`FeatureCompositionTests` and `Elsa3MigrationBoundaryTests`. The JTest acceptance run is by hand (FR-013–FR-016).

**Target Platform**: wherever the repository builds; run with `dotnet run`.

**Project Type**: one host application and its test project.

**Performance Goals**: none beyond the Workbench's; the host composes fewer features.

**Constraints**:
- no Elsa 3 assembly in the host (FR-003, §E2.7);
- not published (FR-001);
- the Workbench unchanged (FR-017);
- no JTest CI tier (FR-016);
- implementation starts after a main sync, which also renames the Workbench host and changes the guard lists (R-7).

**Scale/Scope**: about 70 features (R-4); the S-15 acceptance set.

## Constitution Check

*Re-checked after design: no change.*

| Gate | Status | Notes |
|---|---|---|
| §E2.7 Elsa 3 behind the import boundary | PASS with an in-unit amendment | The host references no `Elsa3.*` project or package; a closure test checks it (R-5). §E2.7 lists only import adapters and shared support, so the implementing PR names the migration host there and in the domain-tree purpose (R-11, T017). |
| §E2.2 Runtime takes no Design reference | PASS | No runtime project changes; the host composes existing features. |
| §E5 packaging, reserved `Elsa.Server` name | PASS | The host isn't packed or published, and doesn't take the reserved name. |
| §E7 CI test tiers | PASS | The new test project goes into `tools/ci/test-tiers.json` as PR tier, with no child host of 2 minutes or more. |
| §2.2 project paths follow the domain tree; §E2.1 groupings | PASS | `Elsa3.` is a registered extension bucket, so `ArchitectureGuardTests` derives `extensions/Elsa3/src/Migration/Host/` and `extensions/Elsa3/tests/Migration/Host/Tests/` with no new entry (R-1). No grouping is added. |
| §2.5, §2.23.1 every feature composes | PASS | No feature is added. |
| §2.20 EF Core admission | PASS | The host resolves EF through the composed features, so it is admitted in `EfCoreDependencyGuardTests`: its name in `AllowedEfConsumers`, a reviewed package array, and an arm in `ReviewedEfPackages` (R-9, T001). |
| §2.23 risk-based tests | PASS | Tests cover composition drift, reasons, Elsa 3 leakage and the folder settings (R-5). |
| §2.22 feature documentation | PASS | No feature or extension point. The host's README documents it. |
| ADR 0073 (vendor-owned EF boundary) | PASS | The host uses main's store library (R-7) and references no EF vendor package directly. |
| Accepted initiative ADRs | PASS | No ADR point is applied or changed. |
| Studio work in this repository | PASS | No Studio change. US4 uses an existing Studio host (R-8). |
| xunit only | PASS | No other framework. |

## Decisions

| # | Decision | Detail |
|---|---|---|
| P-1 | Host | R-1: `Elsa3.Migration.Host` at `extensions/Elsa3/src/Migration/Host/`. |
| P-2 | Wiring | R-2: the Workbench's host wiring that the composed features need, or main's shared helpers after the sync. |
| P-3 | Composition | R-3: `shells.json`, with every composed feature listed; the reasons table in the README. |
| P-4 | Feature list | R-4: candidates, each confirmed with a reason or dropped. |
| P-5 | Tests | R-5. |
| P-6 | Settings | R-6: `packages/` feed, `data/` SQLite with `AutoMigrate`, development identity, and the sample `migrated/` folder. |
| P-7 | Token store | R-7: main's `Elsa.Foundation.Identity.OpenIddict.EntityFrameworkCore`, after a main sync. |
| P-8 | Studio | R-8: an existing Studio host; no Studio change. |
| P-9 | EF admission | R-9. |
| P-10 | Acceptance | R-10: S-15 templates, the CLI's `migrated/`, results in the S-15 ledger. |

## Project Structure

### Documentation (this feature)

```text
specs/1236-elsa3-migration-host/
├── spec.md
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   └── host-composition.md
├── checklists/requirements.md
└── tasks.md
```

### Source Code (repository root)

```text
extensions/Elsa3/src/Migration/Host/
├── Elsa3.Migration.Host.csproj     # project references of every composed feature; not packed
├── Program.cs                      # host wiring (R-2)
├── MigrationHostEfToolingShellDefaults.cs
├── appsettings.json                # persistence, Nuplane feed, logging
├── shells.json                     # the composition (R-3, R-6)
├── MigrationHost.cs                # Build(args), used by Program.cs and the tests (R-2)
├── README.md                       # local tool only; folder settings; reasons table
├── packages/.gitkeep               # Nuplane directory feed for the engineer's packages
├── migrated/
│   ├── workflows/hello.json
│   └── activities/activities.json  # []
├── .gitignore                      # data/, packages/* except .gitkeep, shells.Local.json
└── packages.lock.json

extensions/Elsa3/tests/Migration/Host/Tests/
└── Elsa3.Migration.Host.Tests.csproj   # R-5

essentials/tests/Architecture/Tests/EfCoreDependencyGuardTests.cs   # host admission (R-9)
essentials/tests/Architecture/Tests/HostProvidedPackagesGuardTests.cs   # SharingHosts (R-6)
essentials/tests/Architecture/Tests/Support/NuplaneHostSettings.cs     # host path outside apps/ (R-6)
.specify/memory/constitution.md, docs/reference/elsa-domain-tree.md # §E2.7 and Elsa3 purpose (R-11)
tools/ci/test-tiers.json                                            # + the test project
Elsa.Server.slnx, solution filters, dependency map                  # + the two projects (regenerated)
extensions/Elsa3/tests/JTest/ledger/runs.md                         # migration-host run rows (after S-15 merges)
```

**Structure Decision**: one thin host over existing features, in the Elsa 3 bucket because it exists for the
migration. Nothing in `essentials/src` changes. Outside the two new projects, only the guard tests and their helper named above, the two
constitution and domain-tree lines (R-11), and the generated solution and map files change.

## Complexity Tracking

No constitution violations to justify.
