# Dashboard research and proposed direction

**Date:** 2026-11-06
**Status:** Draft; provider/access and product review remain open.
**Baseline:** `e0ba87ecfc3b1e774c509ed274b19cd3f34a1124`

## Current-source inventory

| Area | Present behavior | Needed for #3604 |
|---|---|---|
| Home | Registered module widgets, host framing, bounded refresh and saved per-user preferences/defaults | Separate durable named dashboards/questions; preserve Home's preference schema and one-widget-per-contribution behavior |
| Data | Fixed Workflow Portfolio/Run Health snapshots | Authorized extensible dataset declarations and newly composed validated queries |
| Metric history | Retained current runtime state with started-cohort summaries | Honest current-outcome interpretation and explicit unknown/incomplete coverage; no implied event-history reconstruction |
| Business fields | No generic declared dashboard business-dimension contract found | Independent module owns explicit fields, data lifecycle and metric semantics |
| Sharing | Workflow saved views have private/tenant-shared behavior | Selected user/role audiences and separate owner/editor/viewer rights for dashboard/question resources |
| Weaver | Existing resource-agnostic context/tool/result/control contracts | Module-owned query discovery/preview contribution and explicit native Save, without a second conversation engine |
| Provider | Existing Copilot/Anthropic harnesses; specialist Connections integration has a different purpose | Selected Weaver provider integrated with Connections/Secrets and actual model evidence |

The [data inventory](research-data.md), [Weaver inventory](research-weaver.md) and [pre-code UX acceptance record](ux-acceptance.md) contain the source links and precise gaps. Existing tests and protocol witnesses do not qualify the new product.

## Interaction patterns from primary sources

Metabase treats saved questions as query/visualization units that can be reused on dashboards when saved to a collection. Its documentation distinguishes these from dashboard-local questions, and shows metadata and history. We take the useful question/dashboard distinction and explicit reuse; Elsa's proposed initial model uses reusable question resources consistently. [Metabase questions](https://www.metabase.com/docs/latest/questions/introduction).

Metabot supports creating new charts through its query builder, keeping conversational context, following up and taking over in the ordinary editor. We take the transition from conversation to an inspectable, editable artifact; arbitrary SQL generation is outside Elsa's selected query boundary. [Metabot documentation](https://www.metabase.com/docs/latest/ai/metabot).

Power BI's report authoring uses a conversational pane for generating and editing visuals, with ordinary report saving and reopening in reading view. We take explicit authoring versus saved consumption and reviewable visual changes. This does not select Power BI, its semantic engine or its provider. [Power BI conversational report authoring](https://learn.microsoft.com/en-us/power-bi/create-reports/copilot-create-reports).

## Proposed decisions

| Decision | Rationale | Alternatives considered |
|---|---|---|
| Keep Home and add named Dashboards with Mine/Shared with me (owner confirmed, 6 November) | Preserve existing preferences/defaults and provide clear owned/shared resource navigation | User-selected named Home; replacing Home |
| Dashboard/question resources under the existing Studio domain; Workflow satellite owns its data | Fits existing ownership and avoids making domain runtime depend on Studio or Design | Arbitrary preference documents; one central query implementation; new top-level analytics domain |
| One dataset per first-profile question; only declared source-owned enrichments | Supports current-folder/business fields without an ad hoc join engine | Arbitrary cross-dataset joins and generated expressions, deferred |
| Native validated query/visualization as the persisted artifact | Reusable and editable without AI; model cannot grant scope or choose executable code | Persisting model prose or executable generated widgets |
| Named additive discovery plus separate authorized execution | Preserve module ownership, declaration/execution lifecycle and current permissions | Generic service locator, dataset-specific switches, browser aggregation |
| No server result cache initially | Avoid sharing a privileged result and simplify revocation/current-access guarantees | Partitioned shared cache adds invalidation and authorization-provenance obligations |
| Current-folder grouping explicitly named | Runs do not pin historical folder membership; Design owns current organization | Invented historical attribution; adding Design dependencies to Runtime |
| Failure rate measures current Faulted outcomes in the started cohort | Matches existing source meaning; cannot claim historical incident/failure events | Reconstruct historical state without stored history; silently reuse effective-history time filters |
| A proposed week comparison uses equal elapsed durations from declared local-week starts | Avoid comparing an unfinished week with an entire prior week without explanation; show resolved intervals and refuse overflow across DST week-length differences | Full prior week or local-clock alignment are distinct meanings, requiring an explicit supported choice |
| Owner controls sharing/delete, editors change content, viewers may copy when allowed to create | Separates collaboration from ownership and data grants | Any editor may reshare; tenant-wide implicit sharing |
| Reused tiles follow the current question revision, with impact warning and override validation | Simple consistent reuse; exact execution binds a revision and stale results are explicit | Pinning every tile by default; silently updating incompatible overrides |
| Copy dashboard privately while retaining identified question references | Predictable shallow copy without silently duplicating others' question ownership | Deep-copy all questions; copy audiences/results/conversation. Question copy remains separate |
| Weaver read-only preview then explicit native Save | Uses current generic extension path without waiting for unmerged M4 effect categories | Dashboard-specific conversation engine; generated prose that reports persistence |

These are reviewable recommendations within the selected feature. Only choices already recorded in #3604 and its owner-authorization comment are confirmed. The owner selected the existing Copilot SDK through backend configuration. Explicit configured live access passed on owned qualification hosts; exact source/model/limits are recorded in the [evidence ledger](evidence/2026-11-13/README.md).

## Agreement and ownership boundaries

#3604 has no parent Initiative and does not use `Part of` to join one. The initiative agreement mechanism is not inferred merely from needing design. Both constitutions and existing ADRs still apply. No constitutional amendment or new pattern is proposed; draft framework §2.24 is supporting guidance, not a ratified new gate. Domain-sensitive implementation requires the normal specification/plan review and current-head code review. If proving the design reveals an ADR conflict, raise that concrete conflict before implementing it.

Weaver #3428/#3443 retains its current Home/module-support work. Dashboard uses delivered M1–M3 extension contracts; #3831/#3845 is not assumed merged or stable. The separate business module is a reference integrator witness, not evidence that customer-category data already exists in core runtime.

## Current review and evidence

The first independent draft review found missing explicit renderer types, native query facets, comparison-result predicates, question revision/copy semantics and ambiguous provider removal. The specification now names them. A fresh review of the final design remains required. This is document review, not actual UX or provider acceptance.

No source implementation, runtime/provider test, merge or resulting-main qualification has occurred. Current-base queued/cancelled CI is not a pass.
