# Implementation Plan: Elsa essentials images

**Branch**: `agent/claude/3052-spec-essentials-images` | **Date**: 2026-11-24 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/1236-essentials-images/spec.md`

## Summary

Add one small library that holds the essentials feature list, the assemblies behind it and the settings merge; a new
engine host that composes exactly that list and reads one user `appsettings.json`; a new Studio app with only the
workflow designer; two Dockerfiles, their CI image jobs and a compose file. Then prove the engine with the S-15 JTest
acceptance set, using the migration CLI's `migrated/` folder as the mounted folder. The Workbench does not
change.

## Technical Context

**Language/Version**: C# / .NET 10; JSON for the feature list and settings.

**Primary Dependencies**: CShells (shell composition), Nuplane (user packages), JSON workflow reconciliation; Docker
for the images; `jtest` for acceptance (as in spec 1235).

**Storage**: SQLite under `/app/data` by default (R-9).

**Testing**: xunit, one new PR-tier test project for the guard and the merge (R-6); existing
`FeatureCompositionTests` and `Elsa3MigrationBoundaryTests`; Studio UX acceptance for the Studio journey (R-8); the
JTest acceptance run by hand (FR-019–FR-023).

**Target Platform**: Linux containers (amd64, arm64), as the Workbench image.

**Project Type**: two host applications, one composition library, images and a compose file.

**Performance Goals**: none beyond the Workbench image's; the engine composes fewer features.

**Constraints**: no Elsa 3 code in the engine image (§E2.7); composition baked and not user-visible (FR-004); the
Workbench unchanged (FR-017); no JTest CI tier (FR-023).

**Scale/Scope**: about 70 engine features (R-7) and 8 Studio features (R-8); the S-15 acceptance set.

## Constitution Check

*Re-checked after design: no change.*

| Gate | Status | Notes |
|---|---|---|
| §E2.7 Elsa 3 behind the import boundary | PASS | The engine host references no Elsa 3 project; the guard (R-6) adds a closure check. The CLI's Elsa 3 entry point references the essentials library, not the reverse. |
| §E2.2 Runtime takes no Design reference | PASS | No runtime project changes; the host composes existing features. |
| §E5 packaging, reserved `Elsa.Server` name | PASS | New names do not take the reserved name (R-1). The library and hosts have no hand-written version. |
| §E7 CI test tiers | PASS | The new test project goes into `tools/ci/test-tiers.json` as PR tier; no child host of 2 minutes or more. |
| §2.2 project paths follow the domain tree | PASS (to verify) | Library next to the hosts in `apps/` (a host grouping, not a domain, R-2); hosts under `apps/` and `Studio/src/apps/` like their siblings; tests under `essentials/tests/Essentials/` like the Workbench's. T001 runs the guard. |
| §2.5, §2.23.1 every feature composes | PASS | No feature added; the library carries none. |
| §2.20 EF Core admission | PASS | The library references EF feature projects only through project references; it resolves no EF package itself. If the guard counts transitive resolution, the library is admitted like the Workbench. |
| §2.23 risk-based tests | PASS | Tests cover drift, Elsa 3 leakage, merge order and secret refusal (R-6). |
| §2.22 feature documentation | PASS | No feature or extension point; `docs/docker.md` gains an essentials section. |
| ADR 0080 (internal by default, plain names) | PASS | Library types are internal except the two entry points the hosts and CLI call. |
| Accepted initiative ADRs | PASS | No ADR point is applied or changed. |
| Studio work in this repository | PASS | The Studio app and its UX acceptance are tasks here (FR-012–FR-014). |
| xunit only | PASS | No other framework. |

## Decisions

| # | Decision | Detail |
|---|---|---|
| P-1 | Hosts | R-1: `apps/Elsa.Essentials.Server`, `Studio/src/apps/Elsa.Studio.Essentials`. |
| P-2 | List | R-2: `essentials.features.json` in `Elsa.Essentials.Composition`, every composed feature with a reason, placed in the image's composition folder (R-12). |
| P-3 | Merge | R-3: list → baked `appsettings.json` → `/app/config/appsettings.json` → environment → command line, one extension method. |
| P-4 | Migrated folder | R-4: `/app/migrated/workflows` (flat, published on reconcile) and `/app/migrated/activities/activities.json`, both shipped empty. |
| P-5 | Secrets | R-5: no baked values; existing refusals checked to name the setting. |
| P-6 | Guard | R-6. |
| P-7 | Feature list | R-7 candidates, each confirmed with a reason or dropped. |
| P-8 | Studio | R-8. |
| P-9 | Persistence | R-9: one SQLite database under `/app/data`, `AutoMigrate`. |
| P-10 | Images | R-10: CI jobs next to the existing images; local builds on the initiative branch. |
| P-11 | Acceptance | R-11: S-15 templates, CLI `migrated/`, results in the S-15 ledger. |

## Project Structure

### Documentation (this feature)

```text
specs/1236-essentials-images/
├── spec.md
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   ├── feature-list.md
│   └── engine-image.md
├── checklists/requirements.md
└── tasks.md
```

### Source Code (repository root)

```text
apps/Elsa.Essentials.Composition/
├── Elsa.Essentials.Composition.csproj      # project references of every listed feature
├── essentials.features.json               # the list (copied to output and to /app/composition)
├── EssentialsFeatureList.cs               # reads the list
├── EssentialsFeatureAssemblies.cs         # assemblies for feature discovery
└── EssentialsConfigurationExtensions.cs   # the merge (P-3)

essentials/tests/Essentials/Tests/
└── Elsa.Essentials.Tests.csproj   # guard and merge tests (R-6)

apps/Elsa.Essentials.Server/
├── Elsa.Essentials.Server.csproj
├── Program.cs                             # CShells + Nuplane host, list-driven
├── appsettings.json                       # baked host defaults (paths, persistence resource, feed)
├── Dockerfile
└── packages.lock.json

Studio/src/apps/Elsa.Studio.Essentials/
├── Elsa.Studio.Essentials.csproj
├── Program.cs
├── shells.json                            # baked, designer-only (not mounted)
├── appsettings.json
└── Dockerfile

docker/compose/
├── docker-compose.essentials.yml
└── essentials/
    ├── appsettings.example.json
    └── workflows/hello.json

.github/workflows/docker.yml               # + elsa-server-essentials
.github/workflows/studio-docker.yml        # + elsa-studio-essentials
docs/docker.md                             # + essentials section
tools/ci/test-tiers.json                   # + the new test project
Elsa.Server.slnx, solution filters         # + new projects (regenerated)
extensions/Elsa3/tests/JTest/ledger/runs.md   # essentials run rows (after S-15 merges)
```

**Structure Decision**: The list and the merge live in a library both the engine host and the migration CLI can
reference; each host is a thin composition over existing features. Nothing in `essentials/src` other than the new
library changes.

## Complexity Tracking

No constitution violations to justify.
