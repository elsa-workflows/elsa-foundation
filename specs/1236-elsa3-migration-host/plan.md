# Implementation Plan: Elsa 3 migration host

**Branch**: `agent/claude/3052-spec-essentials-images` | **Date**: 2026-11-25 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/1236-elsa3-migration-host/spec.md`

## Summary

Add one ASP.NET Core project, `Elsa.Migration.Host`, in `apps/` with the other hosts. It composes, by project
reference, the Elsa 4 features the JTest acceptance set and the Studio designer need, plus JSON workflow and activity
reconciliation of a `migrated/` folder. A reasons file records why each feature is there, and a test project guards the
composition. Then the S-15 JTest acceptance set runs against the host, with the migration CLI's `migrated/` output as
its folder. There are no images, no new Studio app, and no Workbench change.

## Technical Context

**Language/Version**: C# / .NET 10; JSON for the composition and settings.

**Primary Dependencies**: CShells (shell composition), JSON workflow and
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
| §E2.7 Elsa 3 behind the import boundary | PASS | The host references no `Elsa3.*` project or package; a closure test checks it (R-5). It lives in `apps/`, not in the `Elsa3` domain (R-1). |
| §E2.2 Runtime takes no Design reference | PASS | No runtime project changes; the host composes existing features. |
| §E5 packaging, reserved `Elsa.Server` name | PASS | The host isn't packed or published, and doesn't take the reserved name. |
| §E7 CI test tiers | PASS | The new test project goes into `tools/ci/test-tiers.json` as PR tier, with no child host of 2 minutes or more. |
| §2.2 project paths follow the domain tree; §E2.1 groupings | PASS | `ArchitectureGuardTests.ExpectedProjectPath` and `ExpectedSolutionFolder` name each `apps/` host; T002 adds `Elsa.Migration.Host`. The test project's path is name-derived. §E2.1 is unchanged, as for main's other `apps/` hosts (R-1, R-11). |
| §2.5, §2.23.1 every feature composes | PASS | No feature is added. |
| Framework gate §2.20 "Only admitted projects resolve EF Core packages"; ADR 0073 | PASS | The host resolves EF through the composed features, so it is admitted in `EfCoreDependencyGuardTests`: its name in `AllowedEfConsumers`, a reviewed package array, and an arm in `ReviewedEfPackages` (R-9, T004). |
| §2.23 risk-based tests | PASS | Tests cover composition drift, reasons, Elsa 3 leakage and the folder settings (R-5). |
| §2.22 feature documentation | PASS | No feature or extension point. The host's README documents it. |
| ADR 0073 (vendor-owned EF boundary) | PASS | The host uses main's store library (R-7) and references no EF vendor package directly. |
| Accepted initiative ADRs | PASS | No ADR point is applied or changed. |
| Studio work in this repository | PASS | No Studio change. US4 uses an existing Studio host (R-8). |
| xunit only | PASS | No other framework. |

## Decisions

| # | Decision | Detail |
|---|---|---|
| P-1 | Host | R-1: `Elsa.Migration.Host` at `apps/Elsa.Migration.Host/`, with explicit guard entries. |
| P-2 | Wiring | R-2: main's hosting model (`AddFoundationShellHosting`, `MapFoundationHealth`), starting from main's essentials host. |
| P-3 | Composition | R-3: `shells.json`, with every composed feature listed; reasons in `composition-reasons.json`, shown by the README. |
| P-4 | Feature list | R-4: candidates, each confirmed with a reason or dropped. |
| P-5 | Tests | R-5. |
| P-6 | Settings | R-6: no runtime packages, `data/` SQLite with `AutoMigrate`, development identity, and the sample `migrated/` folder. |
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
apps/Elsa.Migration.Host/
├── Elsa.Migration.Host.csproj     # project references of every composed feature; not packed
├── Program.cs                      # host wiring (R-2)
├── MigrationHostEfToolingShellDefaults.cs
├── appsettings.json                # persistence, logging
├── appsettings.Development.json    # development overrides (read by the host-services guard)
├── Properties/launchSettings.json  # Development, fixed local URL (R-2)
├── shells.json                     # the composition (R-3, R-6)
├── composition-reasons.json        # feature → reason (R-3)
├── MigrationHost.cs                # CreateBuilder(args) and Build(builder), used by Program.cs and the tests (R-2)
├── README.md                       # local tool only; folder settings; shows the reasons
├── migrated/
│   ├── workflows/hello.json
│   └── activities/activities.json  # []
├── .gitignore                      # data/, shells.Local.json
└── packages.lock.json

essentials/tests/Migration/Host/Tests/
└── Elsa.Migration.Host.Tests.csproj   # R-5

essentials/tests/Architecture/Tests/EfCoreDependencyGuardTests.cs   # host admission (R-9)
essentials/tests/Architecture/Tests/ArchitectureGuardTests.cs      # host path entries (R-1)
essentials/tests/Modularity/Tests/HostOwnedServicesAreSharedWithShellsTests.cs # + the host in every host list (R-2)
essentials/tests/Modularity/Tests/Elsa.Modularity.Tests.csproj      # + project reference to the host (R-2)
tools/ci/test-tiers.json                                            # + the test project
Elsa.Server.slnx, solution filters, dependency map                  # + the two projects (regenerated)
extensions/Elsa3/tests/JTest/ledger/runs.md                         # migration-host run rows (after S-15 merges)
```

**Structure Decision**: one thin host over existing features, in `apps/` with the other hosts. Nothing in
`essentials/src` changes. Outside the two new projects, only the guard tests and the test project file named above, and
the generated solution, lock and map files change.

## Complexity Tracking

No constitution violations to justify.
