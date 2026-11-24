# Contract: JTest template set

The suites call templates by name. Each engine has one implementation of every template, with the **same name, the
same parameters and the same outputs with the same meaning**. The Elsa 3 set is the corpus's `templates/` folder,
unchanged except for recorded fixes. The Elsa 4 set lives in `templates-elsa4/`.

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
| Version numbers | An integer version `n` matches Elsa 4 version `n.0.0`; templates output the integer. |
| Workflow status | Elsa 4 status → Elsa 3 status/sub-status: Completed → Finished/Finished; Faulted → Finished/Faulted; Cancelled → Finished/Cancelled; Suspended → Running/Suspended; Running → Running/Executing; Pending → Running/Pending. |
| Activity status | Elsa 4 `Completed`, `Faulted` and `Cancelled` output as Elsa 3 `Completed`, `Faulted` and `Canceled`; `Running`, `Waiting` and `Suspended` output as `Running`. Other Elsa 4 statuses fail the template with the value named. |
| Counts | Counts the Elsa 3 journal returned (`totalCount` for started or completed events) are computed from the activity execution list, which requires Durable persistence for the run. |

## Templates

| Template | Parameters (unchanged) | Outputs (unchanged) | Elsa 4 source | Known limit |
|---|---|---|---|---|
| `elsa-authenticate` | `baseUrl`, `username`, `password` | `token`, `authHeader`, `refreshToken` | login, then token | Elsa 3's `isAuthenticated` check becomes `status == "authenticated"` |
| `elsa-workflow-definition-import` | `baseUrl`, `authHeader`, file | `workflowDefinitionId`, `workflowDefinitionVersionId`, `workflowDefinitionVersion`, `workflowDefinition` | `GET /design/workflows/definitions/{id}`; nothing is imported (reconciliation did it) | `workflowDefinition` has the Elsa 4 shape; reported if a suite reads inside it |
| `elsa-workflow-definition-publish` | as Elsa 3 | none | asserts `publishedVersionId` is set | no publish call |
| `elsa-workflow-definition-delete` | `baseUrl`, `authHeader`, definition id | none | `DELETE /design/workflows/definitions/{id}`, then list by `id` expecting no items | the original's `totalCount == 0` becomes an empty `items` list |
| `elsa-composite-import-and-publish-workflow` | as Elsa 3 | `authHeader`, `workflowDefinitionId`, `workflowDefinitionVersionId`, `workflowDefinitionVersion` | authenticate, then import lookup | as above |
| `elsa-get-workflow-instance` | `baseUrl`, `authHeader`, `workflowInstanceId` | `workflowStatus`, `workflowSubStatus`, `instance` | `GET /runtime/workflows/instances/{id}` | `instance` has the Elsa 4 shape |
| `elsa-search-workflow-instances` | `definitionId`, `correlationId`, `version`, paging | `instances` | `GET /runtime/workflows/instances/page`, version filtered in the template | no server-side version, `isSystem` or order filter |
| `elsa-assert-workflow-instance-not-exists` | as search | none | as search, expecting zero | as search |
| `elsa-get-variable-value-from-instance` | `workflowInstanceId`, variable id | `variableValue` | `GET .../instances/{id}/variables`, then the write payload | needs variable payload capture on for the run |
| `elsa-activity-instance-get` | `activityInstanceId` | `activityState`, `startedAt`, `completedAt`, `status`, `id`, `outputs` | `GET .../instances/{wf}/activity-executions/{id}` | needs the workflow instance id; `activityState`/`outputs` come from value snapshots and need capture on |
| `elsa-activity-instance-find` | `workflowInstanceId`, `activityId` | as get, plus `instanceId` | activity execution list filtered by node, then get | as get |
| `elsa-activity-instance-get-journal-item` | `workflowInstanceId`, `activityId` | `activityInstanceId`, `startedAt`, `completedAt` | activity execution list filtered by node | one execution per item, not one journal event |
| `elsa-activity-instance-get-count-started` | `workflowInstanceId`, `activityId` | `count` | executions with `startedAt` set | Durable persistence |
| `elsa-activity-instance-get-count-completed` | `workflowInstanceId`, `activityId` | `count` | executions with status Completed | Durable persistence |
| `elsa-activity-instance-get-journal-items-completed` | `workflowInstanceId`, `activityId` | `journalEntries`, `journalEntriesCount` | completed executions | entry shape differs; reported if a suite reads inside an entry |
| `elsa-activity-instance-assert-all-completed` | `workflowInstanceId`, activity ids | none | instance activity list, all named nodes Completed | page through the activity list |
| `elsa-activity-instance-assert-none-executed` | `workflowInstanceId`, activity ids, event names | none | instance activity list, none of the named nodes present | as above |

Parameters marked "as Elsa 3" are copied from the corpus template; the implementing task checks each one against the
corpus file before writing the Elsa 4 version. If an Elsa 4 template needs a parameter the Elsa 3 one lacks (for
example the workflow instance id for `elsa-activity-instance-get`), it is added to **both** sets during the
engine-neutral refactor and the suites pass it, so the suite files stay identical between engines.
