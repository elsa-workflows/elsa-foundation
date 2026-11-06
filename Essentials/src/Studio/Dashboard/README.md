# Saved questions and dashboards

This domain stores declarative questions and named dashboards alongside Studio Home. Home keeps its widget/layout preferences. Native discovery, editing, validation, Save, reopening, refresh, sharing and drill do not require a model.

## Composition

Add `StudioDashboardApi` and exactly one `StudioDashboardEntityFrameworkCore` store to a shell, selecting its provider/connection through normal EF configuration. The API contributes capability `elsa.api.studio-dashboard` contract1. Workbench includes this composition. See the [API](Api/README.md), [persistence](Persistence/EntityFrameworkCore/README.md) and [query contract](../../../../specs/1217-ai-dashboards/contracts/query.md).

`WorkflowDashboardQueries` contributes the retained workflow dataset; its [source README](../../Workflows/Dashboard/Queries/README.md) defines supported fields, metrics, time and current-folder meanings. Runtime's default authorization still owns every run read. Retention coverage is unknown unless the source can prove otherwise; an increased current outcome rate is not a historical failure-event count.

Optional `StudioDashboardWeaver` comes from `Elsa.Agent.Dashboard`. It registers one model-agnostic `IDashboardAuthoringService` over the generic Weaver session/tool/context contracts. The existing Copilot SDK adapter is configured in the backend through options/appsettings/environment. See [authoring integration](../../../../Extensions/Agent/src/Dashboard/README.md) and [Copilot configuration](../../../../Extensions/Agent/src/GitHubCopilot/README.md). A host omitting this optional feature retains native question/dashboard operation.

## Use

Open Questions, create a question, select an available dataset and compose its supported fields/metrics/filters/groups/time/parameters/presentation. Preview shows actual results under your access, including interpretation, effective periods, coverage and last refresh. Use ordinary controls or ask Weaver where enabled; a proposal is unsaved until you explicitly Use it and Save the native draft.

Create a dashboard from Dashboards → Mine, add saved questions, size/order tiles and Save. A question may appear more than once with independent tile identities/overrides. Shared with me lists definitions granted to the current user or a current role. Sharing a dashboard does not implicitly share its referenced questions, and sharing any definition does not grant source data access.

The owner controls audiences/deletion; editors change content, viewers can make an allowed private copy. Copy saved version copies the exact persisted revision. Save draft as new question preserves dirty/conflicted authored work privately through keyed Create. History is definition history, not historical result data. Restore validates current compatibility and keeps current ownership/audience.

A normal refresh invokes no model. Source/module/field/metric removal is actionable incompatibility; provider unavailability affects conversational authoring. Revoked definition/data access clears protected results. Failed refreshes with unestablished current disclosure withhold prior values while preserving safe native recovery; no failure is converted to zero. Export is unavailable in the initial sources; supported native drill remains bounded/current-authorized.

## Extend

Dataset modules implement the public declaration, validation, execution and drill extension contracts in Dashboard Core. Named events aggregate contributions once; missing/duplicate ownership fails closed. Source code filters records/fields/aggregate permissions before computing or ordering values. Generic query orchestration, Weaver and the provider contain no dataset-specific branches.

The independently packaged [Acme.Processing example](../../../../Extensions/Examples/src/Acme.Processing/README.md) declares its own category dimension, processing duration and metric semantics, ordinary domain record APIs and current actor/tenant/readers. Its reference store is explicitly ephemeral. The [package conformance witness](../../../../tools/studio/Dashboard.Conformance.Probe/README.md) proves package-only consumption separately from live model/composed Studio acceptance.

## Qualification boundary

Core/source/resource/provider/protocol tests are supporting evidence. Full delivery additionally requires actual configured Copilot behavior, committed composed Studio journeys, current-head downloadable visuals, independent UX/code acceptance, merge and resulting-main verification. The [spec evidence index](../../../../specs/1217-ai-dashboards/evidence/README.md) tracks those separately.
