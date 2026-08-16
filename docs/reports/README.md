# Reports Index

Reports are point-in-time findings. They may become work units, but they are not canonical architecture rules.

## Current reports

- [Wave 4 Agent REST and SSE API migration 2026-08](wave-4-agent-api-migration-2026-08.md) - exact eleven-route FastEndpoints-before HTTP/OpenAPI evidence, shared Agent permissions, SSE framing/cancellation, mixed coexistence, and collectible owner lifecycle.
- [Structured Logs Minimal API migration 2026-08](structured-logs-minimal-api-migration-2026-08.md) - streaming program wave: exact HTTP/SSE/OpenAPI parity, Foundation authorization and FastEndpoints coexistence, production dependency retirement, and repeated clean collection after real OpenAPI generation with no module-owned transformer contexts.
- [Secrets Minimal API migration 2026-08](secrets-minimal-api-migration-2026-08.md) - representative ten-operation CRUD/security migration: immutable HTTP and OpenAPI parity, tenant isolation, granular Foundation authorization, sensitive-data non-disclosure, real FastEndpoints coexistence, clean route/traffic/service release, and an honestly retained collectible context after actual ASP.NET OpenAPI generation.
- [Studio Preferences Minimal API canary 2026-08](studio-preferences-minimal-api-canary-2026-08.md) - first production migration in the REST consolidation program: exact HTTP/OpenAPI parity, shared Foundation authorization and mixed-host coexistence passed; materialized-route testing exposed and mitigated `RequestDelegateFactory` retention of collectible handler types.
- [Endpoint framework and authorization spike 2026-08](endpoint-framework-authorization-spike-2026-08.md) - issue #1329 evidence and recommendation: adopt Minimal APIs as the target for all first-party REST APIs, use FastEndpoints coexistence only for staged migration, unify authorization on Foundation policies, atomically publish validated CShells endpoint manifests, and forbid FastEndpoints in dynamically unloadable endpoint assemblies.
- [Elsa 4 architecture review 2026-07](elsa-4-architecture-review-2026-07.md) - consolidated full-codebase review with verified findings and improvement roadmap (W1-W21). Detail sub-reports and per-work-unit implementation briefs: [elsa-4-architecture-review-2026-07/](elsa-4-architecture-review-2026-07/README.md).
- [Simplification review 2026-07](simplification-review-2026-07.md) - YAGNI/DRY/modernization pass over the tree after the W1-W21 roadmap landed; public-API surface, build-config duplication, missing style enforcement, and the accretion pressure behind the project/LoC/type growth. Static analysis only, not compile-verified.
- [Elsa 4 activity contract parity audit 2026-07](elsa-4-activity-contract-parity-2026-07.md) - member-level diff of every out-of-the-box activity's inputs, outputs and outcomes against Elsa 3, with regenerable evidence. Supersedes [elsa-4-activity-gaps.md](elsa-4-activity-gaps.md).
- [Elsa 4 activity behavioural drive 2026-08](elsa-4-activity-behavioural-drive-2026-08.md) - the behavioural half of that audit: every activity driven through a real workflow engine, with declared outcomes, outputs and required inputs measured against what the engine actually committed. Records the contract-surface snapshot guard, the fixes applied, and the REST e2e coverage still outstanding.
- [Subtractive obligation amendment 2026-08](subtractive-obligation-amendment-2026-08.md) - proposed framework constitution §2.25: a periodic consolidation review with standing to retire specs, superseded guards and stale catalog entries, and an evidence bar forbidding census-driven removal. Also records why the proposed §2.16.1 aggregate-growth trigger was **not** pursued: measured like-for-like, project count grew 2.25× against 6.32× LoC.
- [Simplification review decisions 2026-08](simplification-review-decisions-2026-08.md) - build-verified follow-up to the above. Records the §9 items that are governance decisions rather than refactors, and corrects three findings that did not survive compilation: the `internal sealed` sweep is barred by constitution §2.23.3, and the `*.Unified` provider base measured net +74 lines.
- [Elsa 4 improvement recommendations 2026-08](elsa-4-improvement-recommendations-2026-08.md) - the backlog the execution-model comparison produced, plus complexity, DX and UX items found alongside it. Every entry carries Problem / Do / Start at / Done when / Cost / Confidence so a later session can lift one straight into an agent-ready issue. Leads with the pattern behind several of them: mechanisms built correct and left one wire short of the path that needed them, which is why ADRs 0029 and 0032 exist.
- [Execution model comparison 2026-08](execution-model-comparison-2026-08.md) - Elsa 4's drain/checkpoint machinery and its two token engines compared against Zeebe, Camunda 7 and the BPMN 2.0 semantics. Finds the fail-safe `SideEffectProfile` default, the placement/fencing split, and authored fault escalation to be genuine advantages, and the total absence of admission control (measured congestion collapse at N=128) plus two unbounded lease-renewal loops that make a hung dispatch indistinguishable from healthy work to be the real gaps. Argues against partitioning, against a command log, and against re-expressing Flowchart over the BPMN port. Every "worse" finding was then verified against source: three did not survive, four were confirmed and grew. Two recommendations were implemented and measured: hop fusion now applies per intrinsic kind, and the production shape finally has numbers — an `External`-leaf hot loop costs 56 dispatches and 11 commits per run against the published best case's 5 and 1.
- [Concurrency curve re-measured with production leaf shapes 2026-08](concurrency-curve-production-shapes-2026-08.md) - spec 114's N ∈ {1, 8, 32, 128} curve re-run over three leaf shapes instead of the benchmark-only `ReplaySafe` one. The `External` shape pays 11× the commits and 11.2× the dispatches, and its throughput curve has **no rising region** — it peaks at N=1 on a shared SQLite writer where the `ReplaySafe` curve peaked at N=32. Past a threshold the collapse stops being a throughput problem: at N=128 the `External` shape converts into lease-expiry faults on all three backends, via three different wall-clock deadlines. Sizes RB1 (#1235).
- [Knowledge inventory](knowledge-inventory.md)
- [Unfinished work](unfinished-work.md) - inventory of findings and loose concerns, not the active work queue.
- [Architecture tour review](architecture-tour-review.md)
- [Workspace launch readiness review](workspace-launch-readiness-review.md)
- [Glossary coverage audit](glossary-coverage-audit.md)
- [Unfinished work re-ranking](unfinished-work-reranking.md) - superseded point-in-time review.
- [NotImplemented classification](notimplemented-classification.md)
- [Maps v1 findings](maps-v1-findings.md)
- [Maps v2 findings](maps-v2-findings.md)
- [Test maturity and weak implementation report](test-maturity-and-weak-implementation-report.md)
- [Skills stabilization audit](skills-stabilization-audit.md)
- [Zero-EF constitution review](zero-ef-constitution-review.md) - targeted review of the Elsa persistence-provider boundary and the eventual §E2.5 amendment surface.
- [Diagnostics storage workload](diagnostics-storage-workload.md) - Structured Logs and OpenTelemetry append/query/retention inventory and the specialized Groundwork capability ask.
- [EF Core oracle scoping 2026-08](ef-core-oracle-scoping-2026-08.md) - verification of the six assumptions behind the EF ↔ Groundwork comparison harness, before any harness is built. Finds that EF implements **zero** runtime persistence seams: only diagnostics (OpenTelemetry, Structured Logs) and ASP.NET Core Identity are dual-stack, the oracle is SQLite-only because EF has no PostgreSQL or SQL Server wiring in `src/`, and ten of the thirteen frozen workloads declare "EF baselines" that are hand-written contract descriptions which can never be executed. The behavioural differential the plan treats as pending is already built for all three EF-comparable seams, over the exact six dimensions requested. The one seam with a real EF oracle and a numeric gate is blocked in code, and the identical 1.25×/80%/2× ratio was already ratified away for design persistence on fairness grounds.
- [ASP.NET Core Identity and OpenIddict Groundwork contract inventory](identity-openiddict-groundwork-contract-inventory.md) - exact framework-store, schema, concurrency, tenancy, registration, and conformance requirements for replacing the two EF-backed identity lanes.
- [CShells composition evidence](cshells-composition-evidence.md)
- [Runtime execution pre-spec handoff](runtime-execution-pre-spec-handoff.md)
- [Elsa Core runtime broken windows brainstorm](elsa-core-runtime-broken-windows-brainstorm.md)
- [Elsa Core runtime execution layer analysis](elsa-core-runtime-execution-layer-analysis.md)
- [Elsa 4 runtime serialization brainstorm decisions](elsa-4-runtime-serialization-brainstorm-decisions.md)
- [Elsa 4 runtime execution brainstorm decisions](elsa-4-runtime-execution-brainstorm-decisions.md)
- [Elsa 4 runtime execution action plan](elsa-4-runtime-execution-action-plan.md)
- [Elsa 4 runtime execution addendum topics](elsa-4-runtime-execution-addendum-topics.md)
- [Elsa 4 runtime terminology decisions](elsa-4-runtime-terminology-decisions.md)
- [Constitution draft history](constitution-draft-history.md)
- [Constitution amendment index](constitution-amendment-index.md)
- [Framework constitution draft history](constitution-history-framework.md)
- [Elsa constitution draft history](constitution-history-elsa.md)

## Planned reports

- Constitution compliance report.
- Feature composition report.

## Report rule

Reports may quote current repo state and recommend work. Durable definitions belong in the glossary; enforceable rules belong in the constitution.
