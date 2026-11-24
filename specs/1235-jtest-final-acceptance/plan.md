# Implementation Plan: Final JTest acceptance for Elsa 3 to Elsa 4 conversion

**Branch**: `agent/codex/3052-jtest-final-acceptance` | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/1235-jtest-final-acceptance/spec.md`

## Summary

Copy the JTest corpus into `extensions/Elsa3/tests/JTest/`, make it green on an Elsa server built from the 3.6.3
release tag, make the suite files engine-neutral, then add an Elsa 4 template set with the same names, parameters and
outputs and run the suites against the Workbench with the Elsa 3 startup reconciliation enabled. Results go into a
Markdown ledger. The only production change is making the reconciliation feature available in the Workbench; it stays
off in the default shell. Suites that JOIN behaviour blocks stay open in this specification.

## Technical Context

**Language/Version**: JTest suite language schema 1.0.0 (JSON); C# / .NET 10 for the Workbench change.

**Primary Dependencies**: `JTest.Cli` (latest, R-1); Docker for the Elsa 3.6.3 baseline (R-2); Elsa 3 startup
reconciliation (spec 235).

**Storage**: Workbench's default SQLite store, fresh per run; Elsa 3 baseline container state, fresh per run.

**Testing**: The JTest suites themselves, run by hand with `jtest run`. The Workbench change keeps the existing
composition tests green.

**Target Platform**: A developer machine with Docker (Windows or Linux).

**Project Type**: Acceptance test corpus plus one host composition change.

**Performance Goals**: None. Performance suites assert completion counts, not timings.

**Constraints**: No CI tier, no xunit wrapper, no secrets committed, no converter workaround (FR-017), suite
expectations never weakened.

**Scale/Scope**: 36 definitions, 42 suites; 13 templates the suites call plus one internal helper, and any templates
the engine-neutral refactor adds.

## Constitution Check

*Re-checked after design: no change.*

| Gate | Status | Notes |
|---|---|---|
| §E2.7 Elsa 3 stays behind the import boundary | PASS | Elsa 3 runs only as a separate baseline container. The Workbench already references four Elsa 3 import projects; adding the reconciliation project to the same host adds no runtime reference (`Elsa3MigrationBoundaryTests`, `Runtime_projects_do_not_reference_elsa3_compatibility_projects` stay green). |
| §E2.2 Runtime takes no Design reference | PASS | No runtime project changes. |
| §E7 CI test tiers | PASS | No new test project. The JTest folder has no csproj and is not part of any tier, by design (FR-002). |
| §2.21.1 refactoring keeps behaviour | PASS | The engine-neutral refactor moves values without changing expectations and is checked by a green Elsa 3 rerun; templates keep output meaning or fail (contract). |
| §2.23 risk-based tests | PASS | The risky change is host composition; existing feature-composition and boundary tests cover it, and the Elsa 4 run itself exercises the feature end to end. No new unit test is needed for a project reference and a catalog line. |
| §2.22 feature documentation | PASS | No feature, contributor interface or extension point is added. The suite README and a migration-guide pointer are plain deliverables (T044, T045). |
| ADR 0080 (internal by default, pre-GA cleanup) | PASS | No public API added. |
| Accepted initiative ADRs | PASS | No requirement contradicts an accepted ADR: dispositions follow the existing refuse-with-guidance rule, and JOIN decisions stay with the JOIN initiative. |
| xunit-only test rule | PASS | No test framework is added to a test project; JTest is an external acceptance tool run by hand (Q6). |
| Studio | PASS | No Studio surface (FR-023). |

## Decisions

| # | Decision | Detail |
|---|---|---|
| P-1 | Layout | R-3: the corpus's own layout under `extensions/Elsa3/tests/JTest/`, plus `templates-elsa4/`, `environments/elsa4.json`, `global-configuration.elsa4.json`, `adjusted/`, `ledger/`, `README.md`. |
| P-2 | Runner | R-1, R-4: latest `jtest run`, by hand. |
| P-3 | Elsa 3 baseline | R-2: image built from elsa-core `3.6.3`; the corpus's setup suite imports the definitions. |
| P-4 | Elsa 4 host | R-5: Workbench plus the reconciliation feature (reference and catalog entry), enabled only for the run together with the import persistence feature, with Durable persistence and capture on. |
| P-5 | Trigger URLs | R-6: environment value only. |
| P-6 | Ids | R-7: definition ids carry over; Elsa 4 templates match short activity ids against node-id suffixes. |
| P-7 | Vocabulary | R-8: templates translate status and version words; table in [contracts/templates.md](contracts/templates.md). |
| P-8 | Observation | R-9: Elsa 4 sources per template; gaps fail loudly. |
| P-9 | Waits | R-10: unchanged; raising one is a recorded change (suite or template). |
| P-10 | Ledger | [data-model.md](data-model.md): Markdown files in `ledger/`. |
| P-11 | Prerequisites | R-11: spec 1207 implemented before the qualifying Elsa 4 run only. |
| P-12 | Order of suites | HTTP suites first, then single-outcome, then the rest; suites whose definitions contain a join run too and are marked blocked only when the join stops them. |

## Project Structure

### Documentation (this feature)

```text
specs/1235-jtest-final-acceptance/
├── spec.md
├── intake.md
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   ├── templates.md
│   └── run-hosts.md
├── checklists/requirements.md
└── tasks.md
```

### Source Code (repository root)

```text
extensions/Elsa3/tests/JTest/
├── README.md                          # run instructions (from quickstart.md, with the verified Elsa 4 override)
├── global-configuration.json          # corpus: Elsa 3 template search path
├── global-configuration.elsa4.json    # Elsa 4 template search path
├── import-and-publish-workflows.json  # corpus: Elsa 3 setup suite only
├── environments/
│   ├── dev.json                       # corpus: Elsa 3 environment
│   └── elsa4.json
├── templates/                         # corpus: Elsa 3 templates
├── templates-elsa4/                   # same names, parameters and outputs
├── test-files/                        # corpus
├── tests/                             # corpus: 42 suites
├── workflow-definitions/              # corpus: 36 originals, never edited
├── adjusted/                          # approved adjusted copies
└── ledger/
    ├── suites.md
    ├── baseline-failures.md
    ├── refactor-scope.md
    ├── suite-changes.md
    ├── dispositions.md
    └── runs.md

apps/Elsa.Workbench/
├── Elsa.Workbench.csproj              # + reference to the Elsa 3 reconciliation project
├── packages.lock.json                 # restored after the new reference
└── Program.cs                         # + its assembly in the feature catalog
```

**Structure Decision**: The suite is data plus templates, kept in the corpus's layout next to the Elsa 3 converter
tests. The one code change is host composition in the Workbench.

## Complexity Tracking

No constitution violations to justify.
