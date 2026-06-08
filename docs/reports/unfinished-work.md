# Unfinished Work

Status: refreshed during Constitution Thinning v1 from current docs, constitution drafts, extracted draft history, specs, maps, and source comments.

## Program Goal State

Current re-ranking state: [Elsa Brain Operating Model](../program-goals/elsa-brain-operating-model.md).

This state is explicit for this review; it does not make the Elsa Brain Operating Model the default lens for all future work. Future "what next" reviews should first identify whether the current program goal state is a named bucket, `none/free-flow`, or temporarily `unknown/not-assessed`.

## Priority View

This view ranks unfinished work by the current program goal state. The detailed inventory below remains grouped by constitution, code/domain, and knowledge/workspace categories.

| Priority | Candidate | Milestone or state lens | Why now | Next action |
|---:|---|---|---|---|
| 1 | Unfinished work triage maintenance | Elsa Brain Operating Model: reports / next-step selection | This report is the primary "what next" surface, so it should preserve program-goal-state-aware ranking instead of local-topic momentum. | Keep this priority view current during future drift or "what next" reviews; do not rank code-change candidates in this bucket while the current intent is no code changes. |
| 2 | Map freshness / testing maturity map | Elsa Brain Operating Model: generated maps and codebase navigation | Existing maps are useful, but richer test maturity navigation is still planned and map freshness should be checked before maps drive work. | Defer while another agent is handling map-generator work; resume only for map/report planning or verification, not code changes. |
| 3 | Configuration and feature dependency classification | Elsa Brain Operating Model: feature composition readiness | CShells evidence is useful but intentionally provisional; classification is needed before generator work. | Use [CShells composition evidence](cshells-composition-evidence.md) to review and refine the provisional classification vocabulary only; do not implement generator, map-generator, or source-code changes from this bucket. |
| - | Codebase reality / test maturity follow-up | Recorded finding outside current no-code bucket | Code placeholders, weak implementations, and test gaps remain documented, but the current Elsa Brain Operating Model bucket is not selecting code-change work units. | Keep [NotImplemented classification](notimplemented-classification.md) as the safeguard. Re-rank code fixes only when the user explicitly chooses a code-change or implementation bucket. |
| 5 | Constitution ratification / provisional gate review | Elsa Brain Operating Model: constitution governance | Several draft/provisional gates remain open, but broad ratification is premature before code reality and runtime seams are clearer. | Use Critical Constitution Review only for a targeted gate needed by an approved work unit. |

## Constitution and architecture decisions

| Item | Evidence | Next action |
|---|---|---|
| Framework and Elsa constitutions are draft | Both constitutions state version `3.0.0 (draft)` and ratification TODOs | Run Critical Constitution Review before ratification |
| Configuration/settings classification is deferred | Framework `§2.12`; Elsa `§E4`; [CShells composition evidence](cshells-composition-evidence.md) | Review and refine the provisional dependency/settings classification before generator work; then create architecture work unit for configuration and infrastructure covering appsettings schema conventions for feature-bound options, secrets resolution from Key Vault / managed identity / per-tenant, per-feature vs application-wide implementations of the same contract, assembly scanning/loading conventions, and Helm chart conventions for `Elsa.Server` |
| Workflow execution seam is deferred | Elsa `§E2.2`, `§E2.2.2`, `§E2.6`, `§E2.9` references; [runtime execution pre-spec handoff](runtime-execution-pre-spec-handoff.md) | Use the pre-spec handoff as input, then create the architect-owned Speckit work unit when Runtime refactor starts |
| Activity reconciliation Model X remains provisional | Elsa `§E2.8` and extracted draft history mark pending architecture review | Run Critical Constitution Review before ratification |
| WorkflowDefinitionState scope policy remains provisional | Elsa `§E2.9` and `§E2.9.7` mark pending architecture-review ratification | Review with lifecycle command topology and static analyser scope |
| Entity design follow-up overlaps workflow execution | Extracted constitution draft history references entity-design and workflow execution follow-ups | Scope with Runtime/workflow executable work |
| Branching/package strategy is still open | Extracted Elsa draft history references branching strategy and packaging | Use Speckit Flow Guide now; plan package/version meeting separately |
| Integration testing policy is open | Framework unit-test section marks integration testing out of scope | Define TestContainers-based integration testing work unit |
| Event dispatcher failure policy and subscriber failure classification are not implemented | Framework `§2.6.1` / `§2.6.6` now distinguish publisher-owned delivery strategy, publisher-owned dispatcher failure policy, and subscriber-owned failure classification; the current codebase models delivery strategy only | Create implementation work unit for event publish options, event-level defaults, dispatcher failure policies (`Throw immediately`, `Run all then throw aggregate`, `Log/handle gracefully and continue`), and handler/subscriber failure classification metadata/registration; update event pipeline tests and catalogs |
| Pattern catalog has pending/candidate entries | Framework sanctioned-patterns catalog contains pending/candidate language | Review before ratification |

## Code/domain implementation gaps

| Item | Evidence | Next action |
|---|---|---|
| Workflow runtime is minimal/stub-like | Elsa constitution labels `Elsa.Workflows.Runtime.Core` and storage drivers as stubs | Verify current code, then plan runtime domain work |
| Runtime JavaScript design-reference shortcut is deferred | `Elsa.Workflows.Runtime.JavaScript` directly references `Elsa.Workflows.Design.Core` so JavaScript function declarations can be contributed across designer and runtime surfaces while ownership is unstable | Do not refactor until Elsa brain / workspace ownership is stable; then split design-time JavaScript declarations from runtime bindings and keep only neutral shared shape records in stable `.Core` or primitives packages |
| Runtime execution pre-spec risks are open | [Runtime execution pre-spec handoff](runtime-execution-pre-spec-handoff.md) records shared activity/workflow I/O model risk, `ActivityNode` vs executable graph naming risk, execution-context DI scope risk, incomplete variables/expressions substrate, and Runtime JavaScript shortcut risk | Next architect should use the handoff to author the Runtime execution seam Speckit spec before implementation |
| Test maturity and weak implementation risks need work-unit triage | `docs/reports/test-maturity-and-weak-implementation-report.md` finds uneven direct test references, runtime placeholders, the documented Runtime JavaScript design-reference shortcut, and deferred shared event/mediator pipeline coverage | Review the report, then select a focused runtime or test-maturity work unit |
| NotImplemented/code placeholder classification is complete | [NotImplemented classification](notimplemented-classification.md) separates intentional runtime deferrals, resolved expression descriptor drift, incomplete design context, unregistered HTTP placeholder, JavaScript endpoint fake context, and test-only stubs | Keep as recorded codebase evidence only. Do not rank the HTTP placeholder, JavaScript demo endpoint/context, or Workflows Design JavaScript/context as current Elsa Brain Operating Model work while the current intent is no code changes |
| WorkflowDefinitionActivity execution is deferred | Source comment in `WorkflowDefinitionActivity.cs`; spec 006 construct-only scope | Plan consumer/pinning/runtime execution unit |
| Required input/output validation has a known skip | `RequiredInputOutputValidator` comment flags missing data shape | Plan validation-data-shape unit |
| Code analyser enforcement is deferred | Elsa `§E2.9` and model comments | Plan code analyser epic after rules stabilize |
| Lifecycle command topology remains partially open | Spec 003 research/quickstart mention lingering creation path and promote unit | Create lifecycle command shell work unit |
| Workflow-as-activity spec is superseded by 006 | Spec 005 states producer goals retained and re-expressed in 006 | Keep as intent archive; do not implement directly from 005 |

## Knowledge/workspace gaps

| Item | Evidence | Next action |
|---|---|---|
| Future sessions can drift into local cleanup loops | The Elsa-brain program spans operating model, knowledge surfaces, executable workflows, codebase verification, feature composition, and workspace split readiness; active buckets now live in [docs/program-goals](../program-goals/) | Use the `AGENTS.md` program goals and drift guard when a drift trigger is present, and update the relevant program-goal file instead of `AGENTS.md` when goals change |
| Constitution still contains some non-gate material | Constitution Thinning v1, Examples Thinning v2, and Constitution Thinning v3 moved draft history, glossary tables, the Elsa case study, large worked examples, and selected rationale/provenance out of the gate files | Continue thinning only where remaining examples/explanations can move without changing gate meaning |
| AI-provider-neutral skill wrappers need future mirroring | `docs/skills/catalog.md` and [skills stabilization audit](skills-stabilization-audit.md) identify ready, hidden, and planned skills; the Claude wrapper batch under `.claude/skills/elsa-*` is covered by `tools/skills/validate-claude-wrapper-drift.ps1` | Mirror accepted wrappers into other AI-provider adapter surfaces when those surfaces exist; keep running the Claude drift validator after catalog, wrapper, or manifest changes |
| Event/contribution implementation skills have a code-support gap | [skills stabilization audit](skills-stabilization-audit.md) promotes event contribution and independent subscriber workflows; constitution now separates publisher-owned delivery strategy, publisher-owned dispatcher failure policy, and subscriber-owned failure classification, but code support for the latter two is still missing | Use the skills with the documented rule, and record dispatcher failure policy / subscriber failure classification needs against the event failure-strategy implementation work unit until code support lands |
| Constitution draft history is curated but still provisional | `docs/reports/constitution-draft-history.md` now indexes raw framework/Elsa history and `docs/reports/constitution-amendment-index.md` summarizes major amendments | Keep future extracted history in the split report family; do not promote provenance into gates |
| Maps v1/v2 direct facts are generated, but richer maps are still planned | `docs/maps/README.md` planned maps list; [feature dependency map](../maps/feature-dependency-map.md) now covers CShells feature/dependency evidence | Plan test maturity map or approved CShells composition-generation map as separate work units |
| Feature composition JSON generation is not implemented | Skill catalog describes workflow only; [skills stabilization audit](skills-stabilization-audit.md) flags feature identifiers and appsettings schema as blockers; [CShells composition evidence](cshells-composition-evidence.md) records the current evidence and provisional classification language | Build CShells generator only after architects approve or revise feature dependency kinds and configuration/appsettings conventions |

## Refresh procedure

Search these markers before each "What's Next" review:

```powershell
rg -n "TODO|DEFERRED|deferred|pending|ratification|stub|placeholder|not implemented|future|follow-up" .specify/memory docs specs src -g "*.md" -g "*.cs" -g "*.csproj"
```

Classify new findings as constitution, code/domain, tests, docs, maps, or tool/skill work.

## Re-Ranking Rule

When this report is used for "what next" planning, first identify the current program goal state. The state may be a named program-goal bucket, `none/free-flow`, or temporarily `unknown/not-assessed`.

If a named bucket is active, rank candidates by how directly they advance that bucket, then by local severity and unblock value. If `none/free-flow` is active, rank by the user's stated intent, nearby evidence, local severity, and unblock value.

Do not invent a program-goal bucket just because one is missing. Only propose creating or selecting a bucket when the work is forming a mid-term coordination surface that would help future agents, architects, or engineers.
