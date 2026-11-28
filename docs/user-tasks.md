# User Tasks

Studio's **Operate → User tasks** opens the authorized inbox in the built-in Actor Workbench.
**Task forms** supports drafting, publishing and reopening immutable reusable forms. The inbox
and forms use the shared Workbench database with SQLite or PostgreSQL.

## Actor workflow tasks

Add **Create User Task** to a Native or Flowchart workflow. Enter a title, one assignee or
group, and a fixed list of action names. Optionally select a published form version and supply
a prefill object. The Actor activity uses plain literal fields. Connect the Flowchart outcomes
by action name and use the `TaskId`, `Action` and `Data` outputs to carry the result forward.

Publish and execute the workflow. The assigned person can respond from the inbox. Eligible
group members can claim or release a group task. An accepted response may show **Response
pending** while the workflow resumes. The inbox shows **Completed** after the Actor records
consumption of that response. Recovery can resume a parked workflow with its existing task.
Historical classic task records remain readable in the Actor inbox. The earlier execution and
reassignment journey is available on the host documented in the
[classic Workbench compatibility guide](contributing/workbench-compatibility-tests.md#classic-composed-studio-and-decision-runtime).

### Upgrading a Workbench Actor store

Source Workbench and its PostgreSQL Compose shell select the Actor task protocol. Fresh
stores use it on first install. To keep workflows in an existing Actor store, stop its
owners and let their leases expire. For the next Workbench startup, set
`CShells__Shells__default__Features__WorkflowsRuntimeKernelHosting__UpgradeActorUserTaskWaitStore=true`.
When the host is ready, remove that setting and keep `ActorUserTaskWaits` enabled. An upgrade
checks the exact installed roles, publisher scope and store definitions before advancing the
protocol. Existing workflow checkpoints and reusable forms are retained.

The Actor profile accepts up to 64 outstanding tasks per workflow run. Each task's prefill
and response data can use up to 64 KiB of JSON. Test Run remains available for workflows
that do not open user tasks.

## Classic workflow tasks

User Tasks let a workflow wait for a person to choose an action and optionally submit form data. The task is Runtime-owned human work; `Elsa.Tasks` continues to schedule host startup/background/recurring work.

## Author a purchase approval

1. Enable the `UserTasksStudio` module in Studio and use a backend with the User Tasks capability. The normal Foundation Studio host includes the module. Use a cohesive durable Runtime provider; the aggregate Runtime EF registration includes the journal and its migrations.
2. Open **User Tasks → Forms**, choose **Create form version** and publish a form. Studio assigns its version. Open a listed form to inspect its published fields and constraints, or choose **Create next version** to revise it. Add text, multiline, number, boolean, date or choice fields. Keys are case-sensitive; labels/help are displayed as text. Set required, read-only, length, number range and choice constraints as needed. Publishing another version never changes an existing version.
3. Add **Create User Task** to the workflow. Enter a title, exactly one assignee or group, and action names such as `Approve`, `Reject` and `Request More Info`. Group IDs are tenant role IDs in the default integration. Connect the corresponding outcome ports.
4. Select a form; Studio pins its latest published version automatically. The version dropdown lets you choose an older published version when needed. Supply `Prefill` as a JSON object (for example `{}`), rather than quoted JSON text, or as a workflow expression. Required read-only fields must already have values. `Optional Form Actions` can waive required-field presence for named actions such as Reject; supplied values must still have valid types/constraints and read-only values cannot change.
5. To return a task result from the workflow, add its workflow output in the editor's **Outputs** tab, then select that name from **Set Output → Output Name**. The picker reads the current draft, including unsaved output additions. If an output is renamed or removed, select its current name. For a JSON variable, select **JSON** (`System.Text.Json.JsonElement`); the short text `JsonElement` is not that registered type alias.
6. Publish and execute the workflow. It waits until a validated task response is delivered. The result exposes `TaskId`, `Action` and `Data`; field key casing is preserved in `Data`. Open the run's **Details → Workflow outputs** to inspect captured results and any explicit redaction reason. In the workflow-instance details API, `outputs` keys are the authored output names with their casing preserved (`ReviewData`, not `reviewData`). Earlier previews camel-cased them, so a client that read `reviewData` must read the authored name instead. Native workflows and BPMN native service workers use this same lifecycle.

Actions must be unique, trimmed, nonempty names (maximum 20 actions, 100 characters each). `Unmatched` is reserved by the generic value-outcome contract. Actions are authored literals pinned during publication. Existing tasks keep the actions, form version and prefilled values resolved for their invocation.

## Complete and manage work

Open a task from the User Tasks collection to work on its dedicated page. **Back to task list** returns to the originating inbox, search and page; browser Back also preserves that collection context. An expired page offers **First page** while retaining the inbox and search. Leaving an unsent response requires confirmation through the return action; an unconfirmed completion stays protected until its result is resolved.

**My tasks** contains direct assignments and claimed group work; **Group queue** contains available work for current groups. Claim group work before completing it. Only the current eligible claimant can release it. A direct assignee completes without a claim. Enter the form values and choose an action. Boolean fields explicitly distinguish unanswered, Yes and No.

A recorded response can remain **Response pending** while Runtime delivery recovers. Once acceptance is confirmed, you can leave the task page; Runtime retains delivery responsibility. Keep the accepted receipt; repeating the same completion request ID with the same actor/action/data returns that receipt. A different response cannot overwrite it. After an ambiguous connection failure, Studio retries the original request ID and exact payload. On a conflict, refresh the task; Studio preserves unsent values for review.

Managers can inspect the tenant's work and reassign an open/claimed task to an active tenant user or group. Reassignment clears the old claim. Management permission does not grant permission to complete somebody else's work. History records the trusted actor, operation/action and UTC instant. Completed/cancelled tasks refuse further action. Workflow cancellation/end retires unfinished work; missing state alone is not proof that a task has ended.

## Access and host composition

Grant the permissions needed by each role:

| Permission | Use |
|---|---|
| `user-tasks:read` | Discover/read assigned or eligible work |
| `user-tasks:act` | Claim, release and complete eligible work; also needs read |
| `user-tasks:manage` | Inspect/reassign tenant work and enumerate assignment targets |
| `user-task-forms:read` | Select published form versions |
| `user-task-forms:manage` | Create immutable form versions and enumerate authoring targets |

Workflow design/publishing permissions remain required for authoring/publishing. The default adapter uses the canonical normalized Elsa user/tenant, active user directory, current user roles and active tenant membership roles. Token role claims and request-body actor/group values do not establish eligibility. The built-in cookie-to-bearer exchange preserves the verified canonical user ID through issuance and refresh. Custom identity/token integrations must supply that normalized claim explicitly; a protocol subject alone never establishes a task actor. Current subject and task-resource policies are checked on every operation, including after sign-in. Removing a user's eligibility or permission affects subsequent requests.

Runtime does not depend on Design or HTTP. A non-HTTP host supplies `IUserTaskAccess` for current actor, task permission and assignment validation. The default unsupported adapter refuses. `WorkflowsRuntimeApi` supplies the Foundation Identity adapter while preserving an explicitly selected custom adapter. A host without a complete trusted directory remains usable for other Runtime work; task identity operations refuse safely.

Use `AddRuntimeEntityFrameworkCore` to select the complete durable family. A durable custom `IUserTaskStore` also needs a qualified `IUserTaskPersistenceBoundary` that retains execution completion evidence until acknowledgement, alongside the provider's exact-bookmark checkpoint boundary. `IsDurable` alone is insufficient. The EF boundary accepts its owned journal/execution pair. Volatile tasks are available only with explicit `UserTaskOptions.AllowVolatileTasksForDevelopment = true` and an in-memory execution provider; they do not survive a process restart.

## Custom applications and forms

Discover `elsa.api.user-tasks` version 1 and its `tasks`, `task`, `claim`, `release`, `complete`, `reassign`, `forms`, `form-version`, `targets` and `identity` relations. The capability is operationally conditional on the selected task persistence boundary. The [API contract](../specs/1196-user-tasks/contracts/api.md) describes status/error and concurrency semantics.

Task responses from `tasks`, `task` and every mutation carry `assigneeName`, `groupName` and `claimantName` beside the stored IDs, and an `actorName` on each history entry. The server reads them from the tenant directory (`IUserStore` and `IRoleStore` in the default integration) for anyone who may read the task, so a participant never needs the manager-only `targets` relation to show who holds the work. A user's name is its display name, or its user name when it has none; a disabled user keeps its name in history. A name is `null` when the directory has no matching user or group in the task's tenant, such as the `workflow` actor; show the ID then.

Every mutation supplies the expected `revision` and a stable `requestId`. Completion additionally supplies `action` and an object `data`; preserve opaque key casing. A `202` completion is a durable accepted receipt awaiting Runtime proof, not a completed workflow. Store that receipt/request ID in a custom client so a network retry cannot become another decision. Inbox cursors are encrypted, bound to the scope/tenant/user/view/search and expire after 15 minutes. Configured shared keys preserve cursors across replicas and restarts; return to the first page when a cursor is refused after expiry or key rotation.

An external form integration contributes exactly one `IUserTaskFormProvider` for its provider ID. It validates the immutable form reference/definition and asynchronously validates responses on the server. A provider may interpret the version as an immutable external form revision and support richer response data. It must keep that version's semantics stable; mutable external aliases cannot stand in for a pinned revision. The task freezes the structured form reference/fields and prefill. Missing/duplicate providers refuse operations.

A Studio integration contributes `api.userTaskForms.add({ id, provider, component })` using `StudioUserTaskFormProps`. The renderer receives the frozen form, values, read-only state, exact field errors and an `onChange` callback. Missing/duplicate renderers disable completion. Server validation is authoritative. Integrations supply ordinary component code; task definitions do not execute caller-supplied HTML or scripts.

## Recovery and storage

The journal commits revision, history, accepted response and delivery responsibility together. Tasks are staged before their wait commits and become actionable only after observing the committed invocation's physical bookmark generation. Recovery dispatches the pinned accepted response to that exact generation. Queue acceptance does not mark a task complete; acknowledgement checks the committed activity result. A provider deletion fence retains that evidence until acknowledgement. A replaced wait retires its former task; retries creating a successor invocation create a distinct task.

The recovery worker checks up to 50 records per partition per sweep, advances a fair continuation and uses fresh partition/operation scopes. Its normal interval is two seconds and failure backoff is capped at 30 seconds. Temporary journal outages preserve owed responses, including after bookmark claim. Persisted input policies requiring encryption or withholding are refused before writing task content to this journal. Do not use task inputs as a secret store.

Apply the Runtime module's incremental migrations on existing supported relational stores. The User Tasks migration adds its tables; it does not require deleting an existing development database. See [classic Runtime extension points](https://github.com/elsa-workflows/elsa-foundation/blob/aea76c0b38f18e8037050ea53cef20995ec69a03/essentials/src/Workflows/Runtime/Elsa.Workflows.Runtime/EXTENSION_POINTS.md#user-tasks) for ownership/lifetime seams and the [qualification guide](../specs/1196-user-tasks/quickstart.md) for acceptance evidence.


To qualify a custom form integration, run the durable external-form journey and matching mounted renderer test, then use `e2e-tests/user-tasks/qualify_integration.py --help` to compile an external consumer against the public Runtime Core binary and actual Studio package. `Test-UserTasks.ps1` exercises the same authorized HTTP contract through publication and every action path. The live Studio journey covers activity configuration, visual connections, reusable form authoring, save/reload and participant completion on owned hosts.


API replicas must share the inbox cursor key. Configure `UserTaskOptions.CursorEncryptionKey` with the same Base64-encoded 32-byte key on each replica, or use the configured `RuntimeRecoveryContinuationOptions.SigningKey` (the codec derives a separate key for task cursors). The existing EF recovery key setting provides that shared source. Cursors remain encrypted, bind scope/tenant/user/inbox/search, and expire after 15 minutes. Unconfigured development composition uses an ephemeral key; returning to the first page starts a fresh query after restart or key rotation. Do not generate a different configured key on each replica.
