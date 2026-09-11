# Elsa 4 Gap Analysis — Features to Port from Elsa 3

> Sources reviewed (June 2026):
> - Server: `elsa-workflows/elsa-core` (3.7/3.8) + `elsa-workflows/elsa-extensions` (3.7)
> - Studio: `elsa-workflows/elsa-studio` (3.7/3.8)
> - Elsa 4 server: `elsa-workflows/elsa-foundation` (main)
> - Elsa 4 studio: `elsa-workflows/elsa-foundation-studio` (main)
>
> This is a high-level inventory. The Elsa 4 runtime engine is intentionally being rebuilt from clean contracts.
>
> **Correction (verified against `main`, code-as-truth pass):** an earlier draft of this report described the runtime engine as "entirely a stub." That is no longer accurate — and was already stale when first written. The Block 1 runtime execution seam has been specified and implemented across `specs/007`–`specs/080`, with committed contracts, models, and in-process default implementations under `src/Elsa/Workflows/Runtime/Core`. Section 1 below has been re-verified row by row against the source. The remaining downstream gaps (sections 2–12) are genuine, but most are no longer *blocked* on the runtime foundation — that foundation has largely landed.

---

## 1. Core Runtime Engine

**Largely landed.** The planned 9-slice runtime execution seam (see `docs/reports/elsa-4-runtime-execution-action-plan.md`) has been built out — and extended well past the original 9 slices, through `specs/080`. `WorkflowExecutionContext` is a full implementation (264 lines, no stubbed members), backed by the split state model, named pipeline slots, checkpoint commit envelope, bookmark/resume-target resolution, post-commit outbox, recovery scanner, and an in-process execution-agent provider. The table below is re-verified against the source; the one item in this section that remains a genuine gap is sub-workflow execution (1.10).

Status legend: ✅ implemented · 🟡 partial / contract-only · ❌ genuine gap. Status verified against `src/Elsa/Workflows/Runtime/Core` on `main`.

| # | Feature | Description | Status (verified against code) |
|---|---------|-------------|---------|
| 1.1 | **Workflow execution engine** | The core workflow runner: create execution context, schedule root activity, run the scheduling loop until suspension or completion. | ✅ Implemented. `WorkflowExecutionContext` is a full 264-line implementation with no stubbed members (spec `064-runtime-workflow-execution-context`). |
| 1.2 | **Workflow and activity pipelines** | Separate middleware pipelines for workflow-level concerns (exception handling, scheduling, outbox) and activity-level concerns (input evaluation, invocation, output capture, logging). | ✅ Implemented. Named pipeline slots with an inspectable resolved plan (`RuntimePipelinePlan`, `IWorkflowRuntimeMiddleware`/`IActivityRuntimeMiddleware`); spec `009-runtime-pipeline-slots`. |
| 1.3 | **Activity scheduling loop** | The main execution loop that dequeues scheduled work items and invokes activities until the scheduler is empty or the workflow suspends. | ✅ Implemented. Scheduler work queue + per-command work handlers (`IWorkflowSchedulerWorkQueue`, `Workflow*SchedulerWorkHandler`); specs `022`–`037`. |
| 1.4 | **Workflow state extraction and checkpointing** | Extract durable runtime state at named commit boundaries (workflow start, activity executing, activity executed, suspension, completion). | ✅ Implemented. Named checkpoint commit envelope + pluggable persistence policy (`RuntimeCheckpoint`, `RuntimeCheckpointCommit`, `IRuntimeCheckpointPersistencePolicy`); specs `008`, `034`, `080`. |
| 1.5 | **Bookmarks and suspend/resume** | Activities create durable bookmarks; the runtime can suspend and later resume a workflow instance at a bookmark by stimulus hash or bookmark ID. | ✅ Implemented. `BookmarkState` + artifact-stored `ResumeTargetId` resolution (no C# callback method names persisted); specs `010`, `055`–`058`. |
| 1.6 | **Incident handling and fault strategies** | Activities can fault; incident strategy (fault-workflow vs continue-with-incidents) resolves per workflow or global config; fault counts propagate to ancestors. | ✅ Implemented. `IncidentState` first-class state + activity/bookmark fault-incident handling; specs `043`, `062`, `063`. |
| 1.7 | **Dispatch outbox** | Post-commit side-effect delivery: workflow dispatch commands written to an outbox during execution are delivered only after state has committed successfully. | ✅ Implemented. Post-commit outbox (record intent → checkpoint commit → deliver → mark-delivered) via `RuntimePostCommitOutbox`, `IRuntimePostCommitOutboxProcessor`; specs `046`–`048`. |
| 1.8 | **Workflow lifecycle (start / suspend / resume / cancel / fault / complete)** | Full workflow status state machine including `WorkflowStarter`, `WorkflowResumer`, `WorkflowRestarter`, and corresponding REST trigger endpoints. | 🟡 Partial. Start/schedule/complete/checkpoint and bookmark resume are implemented (`WorkflowExecutionStartDispatcher`, completion + resume work handlers, `ExecuteWorkflowRequestHandler`). An explicit cancel / fault / restart admin surface and full trigger-endpoint coverage are still thin — see also 2.2/2.4. |
| 1.9 | **Executable artifact (WorkflowExecutable)** | A runtime-owned compiled representation of a workflow definition: immutable, pinned by version, not dependent on Design-side data at execution time. | ✅ Resolved. The earlier graph-shaped drift is gone: `WorkflowExecutable` now pins a single `ExecutableNode RootActivity` (flattened into `Nodes`/`NodesById`) with an artifact resume-target table; specs `070-workflow-root-activity-contract`, `071-activity-owned-composite-structure`. |
| 1.10 | **Workflow-as-activity (sub-workflow) execution** | A `WorkflowDefinitionActivity` that runs a referenced workflow definition as a nested activity, with cycle guards and nested execution context. | ❌ **Still a gap.** `WorkflowDefinitionActivity.Execute` still throws `NotSupportedException` (`src/Elsa/Activities/Composition/Runtime/Activities/WorkflowDefinitionActivity.cs:24`). Specs `005-workflow-as-activity` and `068-runtime-composed-activity-execution` define the target; execution is not yet wired. |
| 1.11 | **Interrupted execution recovery** | Startup task that finds workflow instances stuck in an executing state (e.g. after a crash) and requeues them. | ✅ Implemented. `IRuntimeRecoveryScanner` + `RuntimeRecovery` operational markers (requeue from last checkpoint, not a domain retry); spec `049`. |
| 1.12 | **Graceful shutdown and drain** | Quiescence mechanism that stops accepting new work and drains in-flight executions before shutdown; back-pressure-aware bookmark queuing. | ✅ Implemented. Scheduler drain coordinator + cooperative pause gate (`RuntimeSchedulerDrain`, `IWorkflowSchedulerDrainer`, `IWorkflowSchedulerPauseGate`, `ControlPlaneState`); specs `023`, `024`, `054`. |
| 1.13 | **Resilience / domain retry policies** | Per-activity or per-workflow retry policies (attempts, backoff, jitter) separate from operational interrupted recovery. | 🟡 Contract-only. `IRuntimeDomainRetryPolicy` exists as a distinct policy boundary from operational recovery (spec `050`); concrete attempt/backoff/jitter strategies are not yet ported from `Elsa.Resilience`. |

---

## 2. Workflow Management

| # | Feature | Description | Remarks |
|---|---------|-------------|---------|
| 2.1 | **Workflow versioning** | Definitions have version numbers; a definition can have one published version and multiple drafts; workflows can be started pinned to a specific version. | Elsa 4 has a publishing API but the versioning semantics (latest vs published, upgrade, retract) are not fully implemented. |
| 2.2 | **Workflow instances API** | CRUD and query endpoints for workflow instances: list, filter by status/definition/correlation, get detail, cancel, fault, delete, bulk-delete. | `Elsa.Workflows.Api` in Elsa 3. Not present in Elsa 4. |
| 2.3 | **Alterations** | Modify running workflow instances: add/remove activities, reschedule bookmarks, set variables, cancel branches. | `Elsa.Alterations` in Elsa 3. Not present in Elsa 4. |
| 2.4 | **Runtime admin API** | Operator endpoints for pause, resume, drain, force-fault, and restart workflow instances or the entire runtime. | `Elsa.Workflows.Api/Endpoints/RuntimeAdmin` in Elsa 3. Not present in Elsa 4. |
| 2.5 | **Labels and categories** | Tag workflow definitions with labels/categories for organisation, search, and permission-based visibility. | `Elsa.Labels` in Elsa 3. Not present in Elsa 4. |
| 2.6 | **Activity execution records** | Persisted per-activity execution history with identity, type, status, I/O snapshots, timestamps, fault data, call-stack depth. | 🟡 Partial. Elsa 4 has a write-only activity-execution *inspection projection* (`IActivityExecutionInspectionWriter`/`Store`, `ActivityExecutionInspectionProjection`; spec `079`) as the observability equivalent — payload capture is policy-gated and never read back for continuation. Full Elsa-3 `ActivityExecutionRecord` parity (rich I/O snapshots, query API) is not yet ported. |
| 2.7 | **Workflow execution log** | Higher-level workflow lifecycle event log (started, suspended, resumed, completed, faulted) separate from activity execution records. | `StoreWorkflowExecutionLogSink` in Elsa 3. |
| 2.8 | **Distributed runtime** | Run workflow execution across a cluster; distributed locking and bookmark coordination across nodes. | `Elsa.Workflows.Runtime.Distributed` in Elsa 3. Not present in Elsa 4. |
| 2.9 | **Key-value store** | General-purpose key/value store used internally for workflow correlation, idempotency keys, and extension scenarios. | `Elsa.KeyValues` in Elsa 3. Not present in Elsa 4. |
| 2.10 | **Hosting management** | Multi-node instance management: register, deregister, heartbeat, and query running Elsa host nodes. | `Elsa.Hosting.Management` in Elsa 3. Not present in Elsa 4. |
| 2.11 | **SAS tokens** | Signed time-limited tokens for secure external callback URLs (e.g. HTTP resume endpoints). | `Elsa.SasTokens` in Elsa 3. Not present in Elsa 4. |
| 2.12 | **Dashboard/shell API** | API surface that powers the Studio shell: feature flags, installed modules manifest, remote-feature gating. | `Elsa.Dashboard.Api` and `Elsa.Shells.Api` in Elsa 3. Not present in Elsa 4. |
| 2.13 | **Blob storage workflow providers** | Load workflow definitions from blob storage (Azure Blob, S3) instead of the database; supports ElsaScript files. | `Elsa.WorkflowProviders.BlobStorage` + `.ElsaScript` in Elsa 3. Not present in Elsa 4. |

---

## 3. Composite Activities

| # | Feature | Description | Remarks |
|---|---------|-------------|---------|
| 3.1 | **StateMachine activity** | A composite activity that models states and transitions; supports entry/exit actions and event-driven transitions. | Present in Elsa 3. Not ported to Elsa 4. |
| 3.2 | **ForEach activity** | Iterate over a collection and execute a body activity for each item. | Present in Elsa 3. Not yet ported to Elsa 4 — no `ForEach` activity found under `src/Elsa/Activities`. (The runtime now supports iteration scope via `IterationId`, so the blocker is the activity itself, not the engine.) |
| 3.3 | **While / DoWhile activities** | Execute a body activity repeatedly while a condition holds. | Present in Elsa 3. Status in Elsa 4 unclear. |
| 3.4 | **Switch / conditional branching** | Route execution to one of several branches based on an expression value. | Present in Elsa 3. Status in Elsa 4 unclear. |
| 3.5 | **Parallel (Fork + Join) activity** | Execute multiple branches concurrently; join waits for all (or a subset) to complete before continuing. | Present in Elsa 3. Not ported to Elsa 4. |

---

## 4. Built-in Activity Library

Most integration activities from Elsa 3 live partly in `elsa-core/src/modules` (messaging, scheduling, Dapper, MongoDB, etc.) and partly in `elsa-workflows/elsa-extensions` (Slack ✅, Telnyx ✅, GitHub ✅, SQL ✅ as of 3.7.0). None of these have Elsa 4 equivalents yet. The expectation is that extensions will eventually live in a future `elsa-foundation-extensions` (or similar) workspace.

| # | Feature | Description | Remarks |
|---|---------|-------------|---------|
| 4.1 | **HTTP endpoint activity** | Expose an HTTP endpoint that starts or resumes a workflow on request (`HttpEndpoint`). | HTTP module exists in Elsa 4. No longer blocked by a stub runtime — the start/resume seam is in place (`ExecuteWorkflowRequestHandler`, bookmark resume); the remaining work is the `HttpEndpoint` activity + trigger wiring itself. |
| 4.2 | **Send HTTP request activity** | Make an outbound HTTP call from within a workflow; supports methods, headers, body, and response binding. | Not present in Elsa 4. |
| 4.3 | **HTTP response / redirect activities** | Write an HTTP response (status code, body, headers) or redirect within an HTTP-triggered workflow. | Not present in Elsa 4. |
| 4.4 | **Send email activity** | Send email via SMTP. | Not present in Elsa 4. |
| 4.5 | **Timer / delay / cron activities** | Suspend a workflow for a duration, until an absolute time, or on a cron expression. | Not present in Elsa 4. |
| 4.6 | **Message bus activities** | Publish and receive messages via MassTransit/RabbitMQ, Kafka, or Azure Service Bus. | Not present in Elsa 4. Elsa 3 extensions. |
| 4.7 | **SQL activities** | Execute SQL queries and non-queries; bind results to workflow variables. | Not present in Elsa 4. `elsa-extensions` in Elsa 3. |
| 4.8 | **File system / IO activities** | Read/write files, watch directories. | Not present in Elsa 4. |
| 4.9 | **Compression activities** | Compress and decompress data (GZip, Zip). | Not present in Elsa 4. |
| 4.10 | **Slack activities** | Post messages and listen for replies via Slack API. | Not present in Elsa 4. |
| 4.11 | **Azure storage activities** | Read/write Azure Blob Storage and Azure Queue. | Not present in Elsa 4. |
| 4.12 | **GitHub / DevOps activities** | Trigger and react to GitHub/Azure DevOps events and actions. | Not present in Elsa 4. |
| 4.13 | **Command-line activities** | Execute shell commands from a workflow. | Not present in Elsa 4. |
| 4.14 | **CSV activities** | Read and write CSV files; bind rows to workflow variables. | Not present in Elsa 4. |
| 4.15 | **Telnyx / telephony activities** | Make and receive calls/SMS via Telnyx. | Not present in Elsa 4. |

---

## 5. Expression System

| # | Feature | Description | Remarks |
|---|---------|-------------|---------|
| 5.1 | **C# scripting expressions** | Evaluate C# code snippets as activity input expressions using Roslyn. | `Elsa.Expressions.CSharp` in Elsa 3. Not present in Elsa 4. |
| 5.2 | **Python scripting expressions** | Evaluate Python code as activity input expressions using IronPython. | `Elsa.Expressions.Python` in Elsa 3. Not present in Elsa 4. |
| 5.3 | **ElsaScript DSL** | Experimental text-based DSL for authoring workflows as code alternative to JSON; supports import/export. | `Elsa.Dsl.ElsaScript` in Elsa 3. Not present in Elsa 4. |
| 5.4 | **Secrets in JavaScript expressions** | Resolve secrets by name within JavaScript expressions (e.g. `getSecret('ApiKey')`). | `Elsa.Secrets.JavaScript` in Elsa 3. Not present in Elsa 4. |

---

## 6. Persistence Providers

Elsa 4 currently only has SQLite via EF Core and the experimental Groundwork provider.

| # | Feature | Description | Remarks |
|---|---------|-------------|---------|
| 6.1 | **SQL Server provider** | EF Core persistence targeting SQL Server. | `Elsa.Persistence.EFCore.SqlServer` in Elsa 3. Not present in Elsa 4. |
| 6.2 | **PostgreSQL provider** | EF Core persistence targeting PostgreSQL. | `Elsa.Persistence.EFCore.PostgreSql` in Elsa 3. Not present in Elsa 4. |
| 6.3 | **MySQL provider** | EF Core persistence targeting MySQL. | `Elsa.Persistence.EFCore.MySql` in Elsa 3. Not present in Elsa 4. |
| 6.4 | **Oracle provider** | EF Core persistence targeting Oracle. | `Elsa.Persistence.EFCore.Oracle` in Elsa 3. Not present in Elsa 4. |
| 6.5 | **MongoDB provider** | Non-relational persistence via MongoDB driver. | `Elsa.Persistence.VNext.MongoDb` in Elsa 3. Not present in Elsa 4. |
| 6.6 | **Dapper provider** | Lightweight persistence via Dapper (SQL without EF Core migration overhead). | `elsa-extensions` in Elsa 3. Not present in Elsa 4. |
| 6.7 | **Runtime state persistence** | Persistence of workflow instance state, bookmark stores, trigger stores, and scheduler queue. | Blocked on runtime execution being implemented. |
| 6.8 | **Secrets persistence (multi-DB)** | EF Core providers for secrets on SQL Server, PostgreSQL, MySQL, Oracle. | Elsa 4 has Groundwork-backed secrets only. |

---

## 7. Scheduling Integrations

| # | Feature | Description | Remarks |
|---|---------|-------------|---------|
| 7.1 | **Quartz.NET integration** | Use Quartz as the scheduler backend for timer and cron triggers. | `elsa-extensions` in Elsa 3. Not present in Elsa 4. |
| 7.2 | **Hangfire integration** | Use Hangfire as the scheduler backend. | `elsa-extensions` in Elsa 3. Not present in Elsa 4. |
| 7.3 | **Scheduling abstraction** | Provider-neutral scheduling contract so hosts can swap Quartz/Hangfire/custom. | `Elsa.Scheduling` in Elsa 3. Not present in Elsa 4. |

---

## 8. Multi-tenancy

| # | Feature | Description | Remarks |
|---|---------|-------------|---------|
| 8.1 | **Tenant resolution** | Resolve the current tenant from HTTP headers, claims, routes, or custom strategy. | `Elsa.Tenants` + `Elsa.Tenants.AspNetCore` in Elsa 3. Not present in Elsa 4. |
| 8.2 | **Tenant-scoped persistence** | Isolate workflow definitions, instances, bookmarks, and secrets per tenant in the database. | Dependent on tenant resolution. Not present in Elsa 4. |
| 8.3 | **Tenant-agnostic workflows** | Workflows that run across tenant contexts without tenant isolation. | Configuration concern. Not present in Elsa 4. |

---

## 9. Connections Framework

| # | Feature | Description | Remarks |
|---|---------|-------------|---------|
| 9.1 | **Connections** | Named, typed connection definitions (e.g. "MyAzureServiceBus") that activities reference by name; secrets stored separately. | `elsa-extensions` (`Connections`) in Elsa 3. Not present in Elsa 4. Distinct from the Secrets module. |
| 9.2 | **OpenAPI activity provider** | Generate typed workflow activities from an OpenAPI spec so any REST API becomes a palette of designer activities. | `elsa-extensions` in Elsa 3. Not present in Elsa 4. A major connector-ecosystem primitive. |
| 9.3 | **Webhooks** | Register outbound webhooks triggered by workflow events (instance completed, activity executed, etc.). | `elsa-extensions` in Elsa 3. Not present in Elsa 4. |
| 9.4 | **OrchardCore integration** | Run Elsa workflows inside an OrchardCore CMS tenant; use OrchardCore content events as triggers. | `elsa-extensions` in Elsa 3. Not present in Elsa 4. |
| 9.5 | **ProtoActor-backed runtime** | Optional actor-model runtime using Proto.Actor for high-throughput concurrent workflow execution. | `elsa-extensions` in Elsa 3. Not present in Elsa 4. |
| 9.6 | **Elasticsearch** | Index workflow instances and definitions in Elasticsearch for advanced search. | `elsa-extensions` in Elsa 3. Not present in Elsa 4. |

---

## 10. Developer Tooling

| # | Feature | Description | Remarks |
|---|---------|-------------|---------|
| 10.1 | **Activity unit testing helpers** | Test utilities for executing individual activities in isolation: fake execution contexts, assertion helpers, workflow test runner. | `Elsa.Testing.Shared` in Elsa 3. Not present in Elsa 4. |
| 10.2 | **API client library** | A typed .NET client for the Elsa REST API to use in tests and external integrations. | `Elsa.Api.Client` in Elsa 3. Not present in Elsa 4. |

---

## 11. Studio / Web Designer

Elsa 3's studio is `elsa-workflows/elsa-studio` (Blazor, modular). Elsa 4's studio is `elsa-workflows/elsa-foundation-studio` — a **React + ASP.NET Core** modular shell. As of June 2026 the Elsa 4 studio only has the module protocol and shell bootstrapping; no workflow-specific UI modules exist yet.

| # | Feature | Description | Remarks |
|---|---------|-------------|---------|
| 11.1 | **Studio module protocol / shell** | CShells-based module manifest system, `GET /_elsa/studio/modules` API, and Vite React shell that loads frontend modules by manifest. | ✅ **Done** in `elsa-foundation-studio`. The extension boundary is proven; no workflow modules contributed yet. |
| 11.2 | **Workflow definitions list and management UI** | Browse, search, create, publish, and delete workflow definitions. | Not present. Requires management API (2.x) to exist. |
| 11.3 | **Visual Flowchart designer** | Drag-and-drop designer for Flowchart-based workflows; activity palette, property editors, connections. | Not present. `elsa-foundation-designer` has early UX work (React Flow-based) referenced in the studio README. |
| 11.4 | **Sequence / StateMachine designers** | Designer surfaces for Sequence and StateMachine workflow roots. | Not present. Depends on StateMachine activity (3.1) and runtime being working. |
| 11.5 | **Workflow instance viewer** | Visual viewer showing execution progress on the workflow graph, call-stack, and incident details. | Not present. Depends on activity execution records (2.6). |
| 11.6 | **Workflow instance list and management UI** | Browse, filter, cancel, and retry workflow instances. | Not present. Requires instances API (2.2). |
| 11.7 | **Diagnostics pages** | Structured logs, console logs, and OpenTelemetry pages in the Studio shell. | Console log streaming module exists in `elsa-foundation-studio` as a sample. Structured log and OTel pages not yet present. |
| 11.8 | **Identity / authentication module** | OIDC login, token refresh, permission-aware menus and routes. | Not present. Elsa 3 studio shipped this in 3.7.0 (Blazor). Needs reimplementation for React shell. |
| 11.9 | **Alterations designer** | UI for modifying running workflow instances. | Not present. Depends on alterations API (2.3). |
| 11.10 | **Custom elements / embedding API** | Embeddable designer and instance viewer components for host applications. | Not present. Elsa 3 studio has Blazor custom elements. React migration and SDK contracts need to be defined. |
| 11.11 | **Activity unit testing UI** | Integration with activity testing helpers and test runner feedback. | Not present. |

---

## 12. AI / Copilot

Elsa 4 already has the Agent domain (`Elsa.Agent.*`). The remaining gap is feature parity with what Elsa 3 added in 3.8.

| # | Feature | Description | Remarks |
|---|---------|-------------|---------|
| 12.1 | **AI Copilot / Weaver** | Server-side copilot: AI-assisted workflow generation from intent, with proposal persistence, audit events, provider/session contracts, and EF Core storage. | `Elsa.AI.*` merged in Elsa 3 (#7523). Elsa 4 has Agent infrastructure but not the full Weaver Copilot feature set. |

---

## Priority Notes

- **Block 1 (runtime engine) has largely landed.** The workflow execution pipeline, split state model, bookmarks/resume, checkpoint commit, post-commit outbox, drain, and recovery are implemented (specs `007`–`080`). The remaining in-section gap is sub-workflow execution (1.10). Downstream work — integration activities, persistence providers, the management API — is now unblocked at the contract level and can begin being tested end-to-end, with sub-workflow scenarios excepted until 1.10 is wired.
- **Elsa 4 is a clean-slate rewrite** of the runtime layer, not a direct port. The action plan in `elsa-foundation/docs/reports/elsa-4-runtime-execution-action-plan.md` gives the original 9-slice sequence; the actual implementation went further (through spec `080`), adding the addendum decisions D11–D16 (volatile waits, generators, control plane, wait-intents, in-process execution-agent provider).
- **elsa-foundation vs elsa-foundation-studio vs future extensions repo.** The core server (`elsa-foundation`), Studio (`elsa-foundation-studio`), and integration extensions are separate repos. Items in sections 4 (activity library) and 9 (connections/integrations) will land in a separate extensions workspace — not in `elsa-foundation` itself.
- **elsa-extensions (Elsa 3)** already has Slack, Telnyx, GitHub, and SQL (MySQL/PostgreSQL/SQLite/SQL Server) at 3.7.0. These four are the most ready to be ported once the Elsa 4 runtime is in place.
- **elsa-foundation-studio** is React-based, not Blazor. Studio features from Elsa 3 cannot be lifted-and-shifted — they need to be re-implemented as React modules using the new CShells/manifest protocol.
- **Studio is a hard dependency on the server API.** Most Studio gaps (11.2–11.11) cannot be meaningfully built until the matching server APIs (sections 1–2) exist.
