# NexxLogic.Executor.V3 → Elsa Foundation Port Plan

Status: free-flow planning inventory. Not yet routed to a program-goal bucket.
Date: 2026-06-10.
Source repos:
- Components to port: `C:\Users\JoeyBarten\Code\nexxlogic-executor-v3\src` (custom components on **Elsa 3.x**).
- Target brain: `C:\Users\JoeyBarten\source\repos\elsa-foundation` (**modular Elsa 4 foundation**).

This report inventories the NexxLogic components, maps them against what elsa-foundation
already provides, and separates work into: **port now (unblocked)**, **unblock first
(foundation gaps)**, and **port after unblock (dependent)**.

---

## 1. The central finding (read this first)

elsa-foundation today is a **design-time + contracts brain**. The **workflow runtime
execution engine does not exist yet** — it is a stub:

- `src/Elsa/Workflows/Runtime/Core/WorkflowExecutionContext.cs` — every member throws
  `NotImplementedException`.
- `IWorkflowExecutionPool`, `IWorkflowActivity` — contracts only, no engine.
- Only concrete runtime artifacts: `MemoryStorageDriver` and the `Runtime.Http` contracts.
- `docs/reports/unfinished-work.md` confirms the **workflow execution seam is deferred**
  (Elsa §E2.2.2 / §E2.6 / §E2.9) and points to
  `docs/reports/runtime-execution-pre-spec-handoff.md` as the input for the architect-owned
  Speckit unit that has not started.

The activity model itself **is** mature (`IActivity`, `ActivityBase`, `CodeActivity<T>`,
`IActivityExecutionContext`, design catalog, CLR reconciliation, JS/Liquid expressions,
CShells feature composition). So:

> **We can port the *shape* of most activity libraries now** (restructure into the new
> feature/module form, compile against the new contracts, unit-test the activity logic).
> **We cannot achieve runnable end-to-end parity** for anything until the runtime engine,
> and several infra domains, land in foundation.

The plan below makes that distinction explicit per component. Treat "port now" as
"port the structure and logic, test in isolation"; treat "runnable" as gated on Track B.

A second systematic concern: the **feature programming model changed**. Elsa 3 used
`FeatureBase` with `Configure()`/`Apply()` and `module.UseXxx()` fluent extensions.
Foundation uses CShells `IShellFeature` + `[ShellFeature]` attribute + `ConfigureServices`,
composed via `appsettings` `CShells:Shells:*:Features:*`. **Every ported feature must be
rewritten into the CShells shape** — no `module.Use*` extension, no `Configure/Apply`.

A third concern: NexxLogic components pull **proprietary external packages**
(`IbpmBackOffice.*`, `NexxLogic.Transformers`, `SaaS.Core.Security.OAuth`,
`NexxLogic.Security.AccessControl.*`, `Bootstrap.Web.*`). Per the architecture constraints,
these heavy/external dependencies should be isolated behind a seam (bridge/adapter +
provider module) rather than referenced directly from a `.Core` contract.

---

## 2. Foundation capability matrix (what exists to receive a port)

| Capability needed by NexxLogic | Foundation status | Evidence |
|---|---|---|
| Activity model (`IActivity`, `CodeActivity<T>`, `ActivityBase`, exec context, I/O) | **Present (mature)** | `Elsa.Activities.Runtime.Core` |
| Activity design catalog + CLR reconciliation | **Present** | `Elsa.Activities.Design.*` |
| Expressions: JavaScript (Jint), Liquid, literal | **Present** | `Elsa.Expressions.JavaScript.Jint`, `Elsa.Expressions.Liquid` |
| Feature composition / DI | **Present (CShells, new shape)** | `IShellFeature` + `[ShellFeature]`, `appsettings CShells:*` |
| Serialization (System.Text.Json, Newtonsoft) | **Present** | `Elsa.Serialization.*` |
| Design persistence (EF Core + SQLite) | **Present** | `Elsa.*.Design.Persistence.EFCore[.Sqlite]` |
| HTTP contracts incl. `IHttpEndpointAuthorizationHandler` | **Partial** | `Elsa.Http.*`, `Elsa.Workflows.Runtime.Http` (contracts present; trigger runtime incomplete) |
| In-memory caching | **Present** | `Elsa.Caching.Memory` |
| File-system distributed lock | **Present** | `Elsa.Locking.FileSystem` |
| Startup / background tasks | **Present** | `Elsa.Tasks.*` (`IStartupTask`, `IBackgroundTask`) |
| Runtime storage driver contract | **Contract only** | `IStorageDriver` + `MemoryStorageDriver` only |
| **Workflow runtime execution engine** | **STUB — absent** | `WorkflowExecutionContext` throws everywhere |
| **Workflow lifecycle events** (`WorkflowExecuted`, etc.) | **Absent** (depends on runtime) | no runtime engine |
| **Runtime persistence + EF migrations** | **Absent / stub** | `Runtime.StorageDrivers` stub |
| **Scheduling** (Quartz / recurring) | **Absent** | constitution names `Elsa.Scheduling.*`; no projects |
| **Messaging / Service Bus transport** (MassTransit, Azure SB) | **Absent** | constitution names `Elsa.Messaging.*`; no projects |
| **Distributed caching** (MassTransit-backed) | **Absent** | only `Caching.Memory` |
| **Distributed lock providers** (Postgres / SqlServer) | **Absent** | only `Locking.FileSystem` |
| Alterations, OpenTelemetry, clustering/heartbeat, ElsaX history/streaming | **Absent** | not in foundation |

---

## 3. Component inventory & classification

NexxLogic components fall into three buckets. **P** = port now (shape/logic, test in
isolation). **B** = blocked, needs a foundation gap closed first. **D** = dependent (port
after a specific Track-B item lands).

### 3a. Activity libraries

| Component | Activities | External deps | Class | Why |
|---|---|---|---|---|
| `Activities.Common` | (infra: `ActivityResult`, `ActivityOutcomes`, `JsonStreamer`) | — | **P** | Foundational shared types; port first, others depend on it. |
| `Activities.Cryptography` | `RijndaelEncrypt`, `RijndaelDecrypt` | Jint (have), custom Rijndael-CLS | **P** | Pure `CodeActivity`; JS feature present. Self-contained. |
| `Activities.Generic` | `ExecuteJsonTransformation(+V1)`, `ApplyStringOperations`, `ValidateAgainstSchema`, `GetAccessToken`, `ReadSecurityToken`, `GetRegistryDefinitionsByKeys` | JLio, Newtonsoft.Json.Schema, JWT, `SaaS.Core.Security.OAuth`, `NexxLogic.Transformers`, a "registry" | **P** (mostly) | Activities portable; OAuth + transformer + registry deps need a seam. `GetRegistryDefinitionsByKeys` depends on a registry concept — confirm whether it maps to the foundation activity/definition catalog or a separate store. |
| `Activities.Database` | `ExecuteStoredProcedureV2` (+ obsolete) | `IbpmBackOffice.StoredProcedure.Services`, SqlServer DbContext | **P** | Activity + service portable; isolate `IbpmBackOffice` behind a contract; EF DbContext config is feature-local. |
| `Activities.Banking` | `ParseBankStatement`, `ExportBankStatement` (+ obsolete) | `IbpmBackOffice.BankConnector` (`IParserDispatcher`) | **P** | Activity portable; isolate bank connector behind a seam. |
| `Activities.Storage` | `UploadBlob` (+ obsolete) | depends on `AzureStorage` feature | **P*** | Activity portable; requires `AzureStorage` provider feature ported first (3b). |
| `Activities.AzureServiceBus` | `ListTopicSubscriptions`, `TopicPurger`, `TopicSubscriptionCleaner`, `TopicSubscriptionsRegenerator`, `SubscriptionMessageCount` | `Azure.Messaging.ServiceBus`, `Azure.ResourceManager.ServiceBus` | **P** | These are **management activities** calling the Azure SDK directly — NOT the Elsa message-bus transport. They do not need the absent messaging domain. Portable as an activity+provider feature. |
| `Activities.Toolbox` | `IdentitySettingsGenerator` | — | **P** | Self-contained; also registers workflows (`AddWorkflowsFrom`). |

### 3b. Infrastructure / plugin components

| Component | Role | Class | Why |
|---|---|---|---|
| `AzureStorage` | Blob client factories (`BlobServiceClientFactory`, `BlobContainerClientFactory`) | **P** | Pure provider feature over Azure SDK. Port as a provider module; prerequisite for Storage activities and the blob storage driver. |
| `Authorization` (`HttpEndpointAuthorizationHandler`) | Custom HTTP endpoint authz via NexxLogic AccessControl | **D → Track B HTTP** | Foundation already declares `IHttpEndpointAuthorizationHandler` (`Runtime.Http`), so the seam exists. But it's only meaningful once the HTTP **trigger runtime** executes workflows. Port the handler against the existing contract; full value gated on HTTP/runtime. Isolate `NexxLogic.Security.AccessControl` behind an adapter. |
| `BlobStorageDriver` (`NexxbizBlob1StorageDriver : IStorageDriver`) | Variable persistence to blob | **D → Track B runtime persistence** | `IStorageDriver` contract exists; can implement the driver shape now. But the runtime that *consumes* storage drivers is stubbed, and it hangs a `WorkflowExecuted` cleanup handler — both gated on the runtime engine. |
| `DatabaseMigrations` (EF migrations for runtime, incl. GZip log compression) | Runtime persistence schema | **B → Track B runtime persistence** | Targets the runtime persistence model that does not exist in foundation. Blocked until runtime persistence lands. |
| `SubscribeNotify.Elsa3` (`WorkflowExecutedNotificationHandler`) | Webhook on workflow completion, status/substatus → HTTP code mapping | **B → Track B runtime lifecycle events** | Entirely dependent on runtime workflow lifecycle events (`WorkflowExecuted`, `WorkflowStatus/SubStatus`) that don't exist yet. Isolate `NexxLogic.Notifier` / OAuth header builder behind a seam when ported. |
| `LoadBalancer` (YARP reverse proxy) | Deployment infra | **out of scope** | Not an Elsa component; port independently as ops infra if still needed. |
| `Executor.V3` host (`Program.cs`, role-based `SetupContext`, MassTransit/caching/scheduling/clustering wiring) | Final assembly / composition | **B → last** | The runnable host is the *terminal* unit. Becomes a CShells `appsettings` composition once its constituent features exist. |

---

## 4. Track B — foundation gaps to unblock first (the critical path)

These are **foundation work units**, not ports. Several are architect-owned and already have
pre-spec input. Ordered by leverage.

| # | Foundation gap | Unblocks | Notes / existing input |
|---|---|---|---|
| **B1** | **Workflow runtime execution engine** (the deferred execution seam: Design→executable artifact, execution context, scheduling loop, variables/IO substrate) | Everything runnable; lifecycle events; storage-driver consumption | Architect-owned Speckit unit. Input: `docs/reports/runtime-execution-pre-spec-handoff.md`; constitution §E2.2.2/§E2.6/§E2.9. **This is the master blocker.** |
| **B2** | **Runtime workflow lifecycle events** (`WorkflowExecuted` and status/substatus model) | `SubscribeNotify`, blob-driver variable cleanup | Falls out of B1; confirm event names/shape during B1 design. |
| **B3** | **Runtime persistence + EF migrations** (instance store, activity execution log incl. compression) | `DatabaseMigrations`, durable execution | Design persistence EFCore exists as a template; runtime side is stub. Sequenced with/after B1. |
| **B4** | **Distributed lock providers** (Postgres, SqlServer) | Clustering / multi-instance host | Foundation has `Locking.Core` + `Locking.FileSystem`; add provider modules. Independent of B1 — can run in parallel. |
| **B5** | **Distributed caching** transport | Multi-instance cache invalidation in host | Foundation has `Caching.Memory`; needs a distributed provider. Tied to B6 transport choice. |
| **B6** | **Messaging / Service Bus transport domain** (`Elsa.Messaging.*` / MassTransit + Azure SB) | Host messaging, alterations-over-bus, distributed caching | Absent entirely; large unit. Distinguish from the AzureServiceBus *management activities* (3a, not blocked). |
| **B7** | **Scheduling domain** (`Elsa.Scheduling.*`, Quartz) | Recurring/triggered workflow activation in host | Absent; `IBackgroundTask`/`IStartupTask` substrate exists as a base. |
| **B8** | **HTTP trigger runtime completeness** | `Authorization` handler end-to-end, HTTP-triggered workflows | Contracts present (`Runtime.Http`); execution path gated on B1. |

Parallelizable without B1: **B4** (distributed locks), and groundwork for **B6/B7**
(domain scaffolding). Everything else sequences behind B1.

---

## 5. Track A — port now (independent of the runtime engine)

Each is a `elsa-create-feature` unit producing a CShells feature, with activity logic
unit-tested in isolation (`elsa-add-unit-tests`). Do a `elsa-feature-dependency-map` check
before each to lock the dependency envelope, and use `elsa-add-bridge-adapter` to isolate
proprietary external packages.

Suggested order (dependencies first):

1. **A0 — `Activities.Common` port.** Shared `ActivityResult` / `ActivityOutcomes` /
   serialization helpers into a foundation-shaped support package. Everything else depends
   on it. Decide: reuse foundation's `Result<T>`/outcome constants vs. port NexxLogic's
   (per "duplication beats dependency" — likely adapt to foundation primitives).
2. **A1 — `Activities.Cryptography`.** Smallest self-contained activity pair; good pilot for
   the FeatureBase→IShellFeature translation pattern and JS-handler registration.
3. **A2 — `AzureStorage` provider feature.** Prerequisite for Storage activities and the
   blob driver. Pure Azure-SDK provider module.
4. **A3 — `Activities.Storage` (`UploadBlob`).** Depends on A2.
5. **A4 — `Activities.Generic`.** Largest/highest-value. Sub-slice it:
   - 4a transformers (JLio) + string operations,
   - 4b validators (JSON schema),
   - 4c authorization activities (`GetAccessToken`, `ReadSecurityToken`) — isolate OAuth seam,
   - 4d `GetRegistryDefinitionsByKeys` — **first confirm** what "registry" maps to in
     foundation (activity catalog vs. a separate definitions store); may reveal a hidden
     dependency.
6. **A5 — `Activities.AzureServiceBus`** management activities (+ provider feature for SB
   admin clients). Independent of the messaging transport domain.
7. **A6 — `Activities.Database` (`ExecuteStoredProcedureV2`).** Isolate
   `IbpmBackOffice.StoredProcedure.Services` behind a contract; feature-local SqlServer
   DbContext config.
8. **A7 — `Activities.Banking`.** Isolate `IbpmBackOffice.BankConnector` (`IParserDispatcher`)
   behind a seam.
9. **A8 — `Activities.Toolbox`.** Self-contained; also ports bundled workflows.

Each A-unit is independently shippable and testable even before B1 lands.

---

## 6. Track C — port after the matching Track-B item lands

| # | Component | Gated on |
|---|---|---|
| C1 | `BlobStorageDriver` (`IStorageDriver` impl + variable cleanup handler) | B1 (driver consumption) + B2 (`WorkflowExecuted`) |
| C2 | `SubscribeNotify` webhook handler | B2 (lifecycle events) |
| C3 | `DatabaseMigrations` (runtime schema/migrations) | B3 (runtime persistence) |
| C4 | `Authorization` handler end-to-end | B8 (HTTP trigger runtime) |
| C5 | **Host composition** — replace `Executor.V3` `Program.cs`/role setup with CShells `appsettings` shells (Default / Api / WorkflowInsights roles) | A* + B* complete; use `elsa-cshells-appsettings` + `elsa-feature-composition` |

---

## 7. Recommended sequencing (one work unit at a time)

```
Phase 0  A0 (Common)                      ← unblocks all activity ports
Phase 1  A1 Cryptography (pilot)          ← proves FeatureBase→IShellFeature pattern
         ‖ B4 distributed locks           ← parallel foundation track, no B1 dependency
Phase 2  A2 AzureStorage → A3 Storage
         A4 Generic (4a→4d)
         A5 AzureServiceBus
Phase 3  A6 Database, A7 Banking, A8 Toolbox
── meanwhile, architect track ──
         B1 runtime execution engine      ← MASTER BLOCKER, start ASAP in parallel
         B2 lifecycle events (within B1)
         B3 runtime persistence
         B6 messaging / B7 scheduling      ← large, schedule after B1 scoped
         B8 HTTP trigger runtime
── after the matching B lands ──
Phase 4  C1 BlobStorageDriver, C2 SubscribeNotify, C3 DatabaseMigrations, C4 Authorization
Phase 5  C5 host composition (CShells appsettings)
```

The two tracks run in parallel: **A-units deliver portable value immediately**; the
**architect-owned B1 runtime unit is the critical path** to anything runnable and should be
scoped first from the existing pre-spec handoff.

---

## 8. Cross-cutting porting rules (apply to every A/C unit)

- **Feature shape:** rewrite `FeatureBase` + `Configure/Apply` + `module.UseXxx()` into
  CShells `[ShellFeature]` `IShellFeature.ConfigureServices`. No fluent `module.Use*`.
- **External deps:** isolate `IbpmBackOffice.*`, `NexxLogic.Transformers`,
  `SaaS.Core.Security.OAuth`, `NexxLogic.Security.AccessControl`, `Bootstrap.Web.*` behind a
  contract + adapter (provider module). Do not reference them from `.Core`.
- **Packaging:** no premature `<Domain>` umbrella when only one provider exists; prefer
  specific provider sub-packages. Duplication of small glue (5–10 lines) beats a dependency.
- **Activity I/O:** map Elsa 3 `Input<T>`/`Output<T>` + `context.Get/Set` +
  `CompleteActivityWithOutcomesAsync` to foundation `InputArgument<T>`/`OutputArgument<T>` +
  `IActivityExecutionContext.Get/Set` + outcomes. Confirm exact API parity per activity.
- **Tests:** constitution requires unit tests for feature registration and logic-bearing
  implementations; **xunit only, no FluentAssertions**.
- **Catalog/reconciliation:** register ported activities through the CLR reconciliation
  source so they appear in the activity catalog (catalog is source-of-truth for the picker).
- **Obsolete activities:** the V1/obsolete duplicates (`BankStatementParserActivity`,
  `ExecuteStoredProcedureActivity`, `UploadBlobActivity`, `ExecuteJsonTransformationV1`) —
  decide drop vs. port-for-compat per unit; default to dropping unless a live workflow pins them.

---

## 9. Open questions to resolve before/while planning units

1. **B1 scope** — is the runtime execution engine being authored in *this* foundation repo,
   or in `elsa-core` / a separate workspace? This decides whether A-units can be validated
   end-to-end here at all.
2. **Registry semantics** — what does `GetRegistryDefinitionsByKeys` read (transformation /
   schema / HTTP-connector definitions)? Maps to foundation catalog or a new store? (Affects A4d.)
3. **External package availability** — are `IbpmBackOffice.*`, `NexxLogic.*`, `SaaS.Core.*`
   available as NuGet feeds to the foundation build, or must they be vendored/re-homed?
4. **Target runtime** — confirm foundation targets net10.0 (obj output shows `net10.0`);
   verify all external NuGets (JLio, Jint v4, Azure SDKs, Newtonsoft.Json.Schema) have
   compatible versions.
5. **Role model** — do the three host roles (Default / Api / WorkflowInsights) survive as
   three CShells shells, and which features each shell activates (C5).
```
