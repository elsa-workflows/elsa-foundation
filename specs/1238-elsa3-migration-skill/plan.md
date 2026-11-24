# Implementation Plan: Elsa 3 to Elsa 4 migration skill and documentation

**Branch**: `agent/claude/3052-spec-migration-skill` | **Date**: 2026-11-24 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/1238-elsa3-migration-skill/spec.md`

## Summary

Write one self-contained, provider-neutral skill folder: `SKILL.md` (the journey: inventory, prepare, test
migration, handoff), `migration-plan-template.md` and `porting-catalog.md` (19 cases). Add metadata-only adapters
for Claude Code (a repository marketplace and one plugin) and document the `.agents/skills/` copy. Back every *port*
case with a compiled synthetic sample and tests, and add a check that the catalog's "after" examples equal the
sample's marked regions and that the skill holds no diagnostic code. Add the README section and the contributor
triggers. The skill reads spec 1237's `report.json`; this plan only lists what it reads
([contracts/report-consumption.md](contracts/report-consumption.md)).

## Technical Context

**Language/Version**: Markdown for the skill and docs; C# (.NET as pinned by `global.json`) for the sample; Python 3
for the check, like the other documentation guards.

**Primary Dependencies**: The sample references only Elsa 4 activity authoring projects
(`Elsa.Activities.Runtime.Core`, `Elsa.Workflows.Runtime.Core` for the trigger stimulus provider, the JavaScript
host-function contract, the variable-type descriptor contract, CShells
abstractions). No Elsa 3 package anywhere.

**Storage**: Files only. The user's plan lives in their workspace.

**Testing**: xunit for the sample; the Python check has a `--self-test` mode, like `tools/ci/check_evidence_sizes.py`.

**Target Platform**: Any machine with Docker (the tool runs in the Elsa 3 migration image) and the .NET SDK; any AI assistant that reads
markdown, or none.

**Project Type**: Documentation plus one non-packable sample feature project, its test project and one CI check.

**Performance Goals**: None.

**Constraints**: Provider-neutral wording; no diagnostic code literals in the skill folder; no other
customer material (the sample is synthetic, `Acme`); no Elsa 3 reference in any project (§E2.7); the skill never
publishes or deploys.

**Scale/Scope**: 3 skill files, 2 adapter files, 19 catalog cases (16 with a *port* part), 2 projects, 1 check,
3 edited docs (README, Elsa 3 mapping README, `AGENTS.md`).

## Constitution Check

*Re-checked after design: no change.*

| Gate | Status | Notes |
|---|---|---|
| §E2.7 Elsa 3 stays behind the import boundary | PASS | The sample is pure Elsa 4 code; Elsa 3 "before" examples are markdown only. No project references Elsa 3. `Elsa3MigrationBoundaryTests` stays green (T003 confirms the sample's name and folder do not trip it). |
| §E2.2 Runtime takes no Design reference | PASS | The sample references Runtime.Core contracts only; no runtime project changes. |
| §E5 packaging and versioning | PASS | The sample is `IsPackable=false`, has no hand-written version. The plugin carries no version (R-4). |
| §E6 naming | PASS | Sample types follow R1–R8; `Acme` names are illustrative and live only in the sample. |
| §E7 CI test tiers | PASS | The sample test project joins the PR tier in `tools/ci/test-tiers.json` (no Testcontainers, no child host). |
| §2.22 feature documentation | PASS | No product feature or extension point is added; the sample feature is a test asset. The skill itself is the documentation deliverable. |
| §2.23 risk-based tests | PASS | The risk is catalog drift; the sample tests plus the check cover it (SC-003, SC-006). |
| xunit-only test rule | PASS | xunit, no assertion library. |
| ADR 0080 (internal by default) | PASS | No public API added. |
| Accepted initiative ADRs | PASS | JavaScript prefixes follow ADR 0045 §5 D-5; background placement follows ADR 0093; refusals follow refuse-with-guidance. Nothing contradicts an accepted ADR. |
| Studio | PASS | No Studio surface. |

## Decisions

| # | Decision | Detail |
|---|---|---|
| P-1 | Skill home | `plugins/elsa3-migration/skills/elsa3-workflows-migration/` (R-1). |
| P-2 | Adapters | Repository-root `.claude-plugin/marketplace.json` with one plugin, `source: ./plugins/elsa3-migration`; `plugins/elsa3-migration/.claude-plugin/plugin.json`. Skills are found by the plugin's default `skills/` folder ([contracts/adapters.md](contracts/adapters.md)). |
| P-3 | Catalog verdicts | The spec's FR-014 table, decided against the code (R-2). |
| P-4 | Sample | `extensions/Elsa3/tests/Migration/Sample/Elsa3.Migration.Sample.csproj` and `extensions/Elsa3/tests/Migration/Sample.Tests/Elsa3.Migration.Sample.Tests.csproj`; marked regions `// catalog:case-NN begin` / `// catalog:case-NN end` (R-3, [contracts/catalog-entry.md](contracts/catalog-entry.md)). |
| P-5 | Check | `tools/skills/check_migration_skill.py`, run with `--self-test` and then for real next to the catalog link check in `ci.yml`; `plugins/**` is documentation-only in `tools/ci/change_scope.py` (R-5). |
| P-6 | Versions | `SKILL.md` lists the report schema versions it reads; the plan records the target Elsa 4 version, which is the migration image tag; no tool version is stamped anywhere (R-4). |
| P-7 | Report | The skill reads only the fields in [contracts/report-consumption.md](contracts/report-consumption.md); any gap is raised on spec 1237's PR, not solved here. |

## Project Structure

### Documentation (this feature)

```text
specs/1238-elsa3-migration-skill/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   ├── adapters.md
│   ├── catalog-entry.md
│   ├── plan-template.md
│   └── report-consumption.md
├── checklists/requirements.md
└── tasks.md
```

### Source Code (repository root)

```text
.claude-plugin/marketplace.json                      # adapter: one plugin
plugins/elsa3-migration/
├── .claude-plugin/plugin.json                       # adapter: metadata only
└── skills/elsa3-workflows-migration/                # canonical skill (self-contained)
    ├── SKILL.md
    ├── migration-plan-template.md
    └── porting-catalog.md
extensions/Elsa3/tests/Migration/
├── Sample/Elsa3.Migration.Sample.csproj             # synthetic Acme Elsa 4 feature, IsPackable=false
└── Sample.Tests/Elsa3.Migration.Sample.Tests.csproj # xunit, PR tier
tools/skills/check_migration_skill.py                # catalog ⇔ sample regions; no diagnostic codes
README.md                                            # "Migrating from Elsa 3" section
extensions/Elsa3/src/Mapping/README.md               # contributor trigger
AGENTS.md                                            # one-line pointer
docs/skills/catalog.md                               # one line: product skills live under plugins/
tools/ci/test-tiers.json, Elsa.Server.slnx, .github/workflows/ci.yml, tools/ci/change_scope.py   # registration
```

**Structure Decision**: Skill and adapters at the repository root under `plugins/` and `.claude-plugin/`, separate
from contributor skills in `.claude/skills/` and `.agents/skills/`. The sample sits in the Elsa 3 test area because
it exists to keep the migration catalog correct, not as a product feature.

## Complexity Tracking

No gate violation. The compiled sample and its check are the one addition beyond the migration journey itself; why
they are needed is in research R-3.
