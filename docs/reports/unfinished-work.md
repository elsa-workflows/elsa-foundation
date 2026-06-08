# Unfinished Work

Status: refreshed during Constitution Thinning v1 from current docs, constitution drafts, extracted draft history, specs, maps, and source comments.

## Constitution and architecture decisions

| Item | Evidence | Next action |
|---|---|---|
| Framework and Elsa constitutions are draft | Both constitutions state version `3.0.0 (draft)` and ratification TODOs | Run Critical Constitution Review before ratification |
| Configuration/settings classification is deferred | Framework `§2.12`; Elsa `§E4` | Create architecture work unit for configuration and infrastructure covering appsettings schema conventions for feature-bound options, secrets resolution from Key Vault / managed identity / per-tenant, per-feature vs application-wide implementations of the same contract, and Helm chart conventions for `Elsa.Server` |
| Workflow execution seam is deferred | Elsa `§E2.2`, `§E2.2.2`, `§E2.6`, `§E2.9` references | Create work unit when Runtime refactor starts |
| Activity reconciliation Model X remains provisional | Elsa `§E2.8` and extracted draft history mark pending architecture review | Run Critical Constitution Review before ratification |
| WorkflowDefinitionState scope policy remains provisional | Elsa `§E2.9` and `§E2.9.7` mark pending architecture-review ratification | Review with lifecycle command topology and static analyser scope |
| Entity design follow-up overlaps workflow execution | Extracted constitution draft history references entity-design and workflow execution follow-ups | Scope with Runtime/workflow executable work |
| Branching/package strategy is still open | Extracted Elsa draft history references branching strategy and packaging | Use Speckit Flow Guide now; plan package/version meeting separately |
| Integration testing policy is open | Framework unit-test section marks integration testing out of scope | Define TestContainers-based integration testing work unit |
| Pattern catalog has pending/candidate entries | Framework sanctioned-patterns catalog contains pending/candidate language | Review before ratification |

## Code/domain implementation gaps

| Item | Evidence | Next action |
|---|---|---|
| Workflow runtime is minimal/stub-like | Elsa constitution labels `Elsa.Workflows.Runtime.Core` and storage drivers as stubs | Verify current code, then plan runtime domain work |
| Runtime JavaScript design-reference shortcut is deferred | `Elsa.Workflows.Runtime.JavaScript` directly references `Elsa.Workflows.Design.Core` so JavaScript function declarations can be contributed across designer and runtime surfaces while ownership is unstable | Do not refactor until Elsa brain / workspace ownership is stable; then split design-time JavaScript declarations from runtime bindings and keep only neutral shared shape records in stable `.Core` or primitives packages |
| Test maturity and weak implementation risks need work-unit triage | `docs/reports/test-maturity-and-weak-implementation-report.md` finds uneven direct test references, runtime placeholders, the documented Runtime JavaScript design-reference shortcut, and deferred shared event/mediator pipeline coverage | Review the report, then select a focused runtime or test-maturity work unit |
| WorkflowDefinitionActivity execution is deferred | Source comment in `WorkflowDefinitionActivity.cs`; spec 006 construct-only scope | Plan consumer/pinning/runtime execution unit |
| Required input/output validation has a known skip | `RequiredInputOutputValidator` comment flags missing data shape | Plan validation-data-shape unit |
| Code analyser enforcement is deferred | Elsa `§E2.9` and model comments | Plan code analyser epic after rules stabilize |
| Lifecycle command topology remains partially open | Spec 003 research/quickstart mention lingering creation path and promote unit | Create lifecycle command shell work unit |
| Workflow-as-activity spec is superseded by 006 | Spec 005 states producer goals retained and re-expressed in 006 | Keep as intent archive; do not implement directly from 005 |

## Knowledge/workspace gaps

| Item | Evidence | Next action |
|---|---|---|
| Future sessions can drift into local cleanup loops | The Elsa-brain program spans operating model, knowledge surfaces, executable workflows, codebase verification, feature composition, and workspace split readiness | Use `AGENTS.md` program goal and zoom-out rule before selecting each next work unit |
| Constitution still contains some non-gate material | Constitution Thinning v1, Examples Thinning v2, and Constitution Thinning v3 moved draft history, glossary tables, the Elsa case study, large worked examples, and selected rationale/provenance out of the gate files | Continue thinning only where remaining examples/explanations can move without changing gate meaning |
| Provider-neutral skills are cataloged but not executable wrappers | `docs/skills/catalog.md` | Add provider-specific wrappers only after workflow descriptions are accepted |
| Constitution draft history is curated but still provisional | `docs/reports/constitution-draft-history.md` now indexes raw framework/Elsa history and `docs/reports/constitution-amendment-index.md` summarizes major amendments | Keep future extracted history in the split report family; do not promote provenance into gates |
| Maps v1/v2 direct facts are generated, but richer maps are still planned | `docs/maps/README.md` planned maps list | Plan test maturity map or CShells composition map as separate work units |
| Feature composition JSON generation is not implemented | Skill catalog describes workflow only | Build CShells generator after feature identifiers are discoverable |

## Refresh procedure

Search these markers before each "What's Next" review:

```powershell
rg -n "TODO|DEFERRED|deferred|pending|ratification|stub|placeholder|not implemented|future|follow-up" .specify/memory docs specs src -g "*.md" -g "*.cs" -g "*.csproj"
```

Classify new findings as constitution, code/domain, tests, docs, maps, or tool/skill work.
