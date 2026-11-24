# Contract: JTest template set

The suites call templates by name. Each engine has one implementation of every template the suites use, with the
**same name, the same parameters and the same outputs with the same meaning**. The Elsa 3 set is the corpus's
`templates/` folder, unchanged except for recorded fixes. The Elsa 4 set lives in `templates-elsa4/`.

A template may change how it obtains a value. It may not change what the value means. Where Elsa 4 cannot produce an
output with the same meaning, the template fails with a message naming the output; it never returns an approximation.

## Shared translations in the Elsa 4 set

| Concern | Rule |
|---|---|
| API root | Workbench serves its API at the host root (`elsaApi:baseUrl`, no `/elsa/api` prefix). |
| Trigger URLs | `elsaExecutorApi:baseUrl` is `{base}/workflows/http`; suites append their endpoint path. |
| Authentication | `POST /_elsa/identity/login` with JSON `{username, password}`, then `GET /_elsa/identity/token` with the session cookie; `authHeader` is `Bearer {accessToken}`. |
| Activity ids | A suite's short Elsa 3 activity id matches the Elsa 4 node whose id equals it or ends with `:{id}`. |
| Definition ids | Unchanged by conversion. Version id equals the Elsa 3 version id at converter revision 0. |
| Version numbers | At converter revision 0, an integer version `n` matches Elsa 4 version `n.0.0`; templates output the integer. A later converter revision changes this rule and the template with it. The Elsa 3 baseline numbers every imported definition 1 whatever its file says, while Elsa 4 keeps the file's version, so a suite's expected version is engine-specific and moves behind an environment value in the engine-neutral refactor (`storage-drivers-variables-root-workflow.json` is version 2). |
| Workflow status | Elsa 4 status → Elsa 3 status/sub-status: Completed → Finished/Finished; Faulted → Finished/Faulted; Cancelled → Finished/Cancelled; Suspended → Running/Suspended; Running → Running/Executing; Pending → Running/Pending. |
| Activity status | Elsa 4 `Completed`, `Faulted` and `Cancelled` output as Elsa 3 `Completed`, `Faulted` and `Canceled`; `Running`, `Waiting` and `Suspended` output as `Running`. Other Elsa 4 statuses fail the template with the value named. |
| Workflow instance items | `instances` items carry the Elsa 3 field names suites read: `id` (from `workflowExecutionId`), `definitionId`, `definitionVersionId`, `correlationId`, `name`, `version` (integer, per the version rule) and `status`/`subStatus` (per the status table). |
| Activity member names | `activityState` and `outputs` keys are the Elsa 3 member names, mapped from the converted Elsa 4 inputs and outputs with the converter's member map (see *Activity state and outputs*). |
| HTTP status codes | An Elsa 4 integer HTTP status code outputs as the Elsa 3 enum name suites compare against (for example `202` → `Accepted`, as .NET's `HttpStatusCode` names it). |
| Counts | Counts the Elsa 3 journal returned (`totalCount` for started or completed events) are computed from the activity execution list, which requires Durable persistence for the run. |

**Activity state and outputs.** `activityState` and `outputs` of the `elsa-activity-instance-*` templates are part
of the contract, keyed as Elsa 3 keyed them: suites read keys such as `activityState.Text`, `activityState.StatusCode`,
`activityState.ResponseHeaders[...]`, `outputs.ParsedContent` and `outputs.Headers[...]`. The Elsa 4 template builds
them from the activity execution's value snapshots, mapping each converted input or output back to its Elsa 3 name
with the converter's member map, and keeps values as the suite compares them (for example `StatusCode` as the Elsa 3
enum name `Accepted`). A key with no Elsa 4 counterpart fails the template with the key named.

**Snapshot limits.** Elsa 4 caps capture at diagnostic snapshots by host policy: bounded values (strings, arrays, objects, depth and
size are limited) marked `Truncated` or `OmittedCount` when cut. Templates read the snapshot's full value fields and
fail with the key named when a value is truncated or only a preview exists; each such value is recorded as an
FR-011 gap, and its suite is marked `blocked: value capture` (spec FR-024). Complete capture belongs to the separate
value-capture initiative.

Other values suites read **inside** an output beyond the fields above (for example
`instance.workflowState.activityExecutionContexts[0].properties.Variables[...]`) are Elsa 3 internals. They are not
projected: the engine-neutral refactor moves those reads behind a template, existing or new, before the Elsa 4 work.

## Templates the suites use

Parameters and outputs are copied from the corpus files.

| Template | Parameters | Outputs | Elsa 4 source | Known limit |
|---|---|---|---|---|
| `elsa-authenticate` | `username`, `password`, `baseUrl` | `token`, `authHeader`, `refreshToken` | login, then token | Elsa 3's `isAuthenticated` check becomes `status == "authenticated"` |
| `elsa-search-workflow-instances` | `baseUrl`, `authHeader`, `workflowDefinitionId`, `workflowDefinitionVersion`, `correlationId`, `pageSize` | `instances` | `GET /runtime/workflows/instances/page`, version filtered in the template, items projected | no server-side version filter |
| `elsa-assert-workflow-instance-not-exists` | `baseUrl`, `authHeader`, `workflowDefinitionId`, `workflowDefinitionVersion`, `correlationId` | none | as search, expecting zero | as search |
| `elsa-get-workflow-instance` | `baseUrl`, `authHeader`, `workflowInstanceId` | `workflowStatus`, `workflowSubStatus`, `instance` | `GET /runtime/workflows/instances/{id}` | `instance` keeps the Elsa 4 shape; reads inside it are refactor scope |
| `elsa-get-variable-value-from-instance` | `baseUrl`, `authHeader`, `workflowInstanceId`, `variableId` | `variableValue` | `GET .../instances/{id}/variables`, then the write payload | needs a `variableWrites` capture override set for the run; snapshot limits apply |
| `elsa-activity-instance-get` | `baseUrl`, `authHeader`, `activityInstanceId` | `activityState`, `status`, `id`, `startedAt`, `completedAt`, `outputs` | `GET .../instances/{wf}/activity-executions/{id}` | needs the workflow instance id (added parameter, below); `activityState`/`outputs` come from value snapshots, set to snapshot level for the run; snapshot limits apply |
| `elsa-activity-instance-find` | `baseUrl`, `authHeader`, `workflowInstanceId`, `activityId` | as get | activity execution list filtered by node, then get | as get |
| `elsa-activity-instance-get-journal-item` | `baseUrl`, `authHeader`, `workflowInstanceId`, `activityId` | `activityInstanceId`, `startedAt`, `completedAt` | activity execution list filtered by node | one execution per item, not one journal event |
| `elsa-activity-instance-get-count-started` | `baseUrl`, `authHeader`, `workflowInstanceId`, `activityId` | `count` | executions with `startedAt` set | Durable persistence |
| `elsa-activity-instance-get-count-completed` | `baseUrl`, `authHeader`, `workflowInstanceId`, `activityId` | `count` | executions with status Completed | Durable persistence |
| `elsa-activity-instance-get-journal-items-completed` | `baseUrl`, `authHeader`, `workflowInstanceId`, `activityId` | `journalEntries`, `journalEntriesCount` | completed executions | entry shape differs; reads inside an entry are refactor scope |
| `elsa-activity-instance-assert-all-completed` | `baseUrl`, `authHeader`, `workflowInstanceId`, `activityIdentifiers`, `activityIdentifiersCount` | none | the number of Completed executions whose node matches any listed id equals `activityIdentifiersCount` (as the Elsa 3 template counts Completed journal events), excluding the workflow and flowchart containers | page through the activity list |
| `elsa-activity-instance-assert-none-executed` | `baseUrl`, `authHeader`, `workflowInstanceId`, `activityIdentifiers`, `eventNames` | none | no execution of a listed node has reached any of the events in `eventNames` (default `Completed` and `Started`) | as above |

`internal-elsa-activity-instance-get-count` (parameters as the count templates plus `eventName`) is called only by
the two count templates. The Elsa 4 set may implement it or inline it; suites never call it.

## Templates not used on Elsa 4

`elsa-workflow-definition-import`, `elsa-workflow-definition-publish`, `elsa-workflow-definition-delete` and
`elsa-composite-import-and-publish-workflow` serve only the Elsa 3 setup suite (or nothing). Elsa 4 loads definitions
through startup reconciliation, so the Elsa 4 set does not implement them.

## Changing the set

During the engine-neutral refactor a template may gain a parameter (for example the workflow instance id for
`elsa-activity-instance-get`), and a new template may be added where a suite calls the engine API directly (the
`events` suite and three `performance` suites call `/elsa/api/workflow-definitions/{id}/execute` and
`/elsa/api/workflow-instances` and read `workflowState.status`). Each such change lands in **both** sets, with
outputs of the same meaning, and the refactored suites pass on Elsa 3 first. Suite files then stay identical between
engines.

**Execute.** The Elsa 3 execute call runs the workflow and returns its final state, which suites assert on at once
(`workflowState.status`/`subStatus`, `workflowState.output.*`, `workflowState.id`, `workflowState.correlationId`).
Elsa 4's `POST /runtime/workflows/executables/{artifactId}/execute` only starts a run and returns its workflow
execution id. The Elsa 4 execute template therefore resolves the definition id to its published artifact, starts
the run, then reads the instance (`GET /runtime/workflows/instances/{id}`) until it reaches a terminal status within a
bounded deadline, and takes outputs from the instance's `outputs`. This polling is how the template keeps the Elsa 3
call's meaning (a finished run); it is not a suite wait, and a run still unfinished at the deadline fails the template.
