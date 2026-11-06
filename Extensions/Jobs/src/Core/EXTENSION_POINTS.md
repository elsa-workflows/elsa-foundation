# Jobs extension points

This is the contract catalog linked by the [Jobs composition root](../Elsa.Jobs/EXTENSION_POINTS.md).
All interfaces below are **Core — `Elsa.Jobs.Core`**. Implementation registration belongs to the
[Jobs runtime](../Elsa.Jobs/README.md) or [EF provider](../Persistence/EntityFrameworkCore/README.md).
These contracts do not qualify a provider or authorize a second lifecycle authority.

## Overridable contracts

| Contract and signature | Default implementation and owning project | When to override; consumers |
|---|---|---|
| `IJobClient`: `ValueTask<JobAcceptance> SubmitAsync(JobScope, JobSubmission, CancellationToken = default)` | Internal scoped `JobClient`, `Elsa.Jobs`; registered with `TryAddScoped` by `AddElsaJobs`. | Replace the ordinary producer facade while preserving bounded immutable admission, scope and protected-key replay. Used by standalone producers; trusted model adapters use their own saved-origin admission. |
| [`IJobCommands`](JobCommands.cs): the linked declaration supplies every exact request/lease and `ValueTask<T>` command signature, with optional cancellation. | Internal scoped `EfJobStore`, `Elsa.Jobs.Persistence.EntityFrameworkCore`; registered by `AddJobsEntityFrameworkCore`. | Supply another durable authority implementing the complete atomic admission/gate/start/capacity/attempt/result/origin/delivery contract. Used by client, worker and trusted model/operations adapters. |
| [`IJobQueries`](JobQueries.cs): the linked declaration supplies every exact scope/filter/page and `ValueTask<T>` query signature, with optional cancellation. | The same scoped `EfJobStore` selected as `IJobCommands`, unless explicitly replaced. | Supply read-only bounded inspection against the same durable authority. Used by workers and code/API/Studio inspection; cursors must bind exact scope and filters. |
| `IJobRecurrenceCalculator`: `DateTimeOffset? Next(JobRecurrence, DateTimeOffset after)` | Internal singleton `JobRecurrenceCalculator` in `Elsa.Jobs`, registered with `TryAddSingleton` by `AddElsaJobs`; it adapts pure UTC math from `Elsa.Tasks.Schedules`. EF-only composition accepts zero or one host contribution and refuses duplicates. | Supply pure UTC arithmetic for schedule materialization. The command provider depends on it; the calculator never owns schedule cursors, missed policy or job acceptance. Jobs startup requires exactly one calculator; accepted occurrences and cursor/missed policy remain provider-owned. |

An override is one explicit scoped service registration of the interface, replacing its prior
registration rather than adding a second authority. `AddJobsEntityFrameworkCore` uses `TryAdd` so an
explicit host replacement is retained; provider configuration itself may be composed only once.
Startup refuses missing or duplicate command/query/client authorities. A replacement command provider
also supplies queries or a matching explicit query provider; no last-registration choice is a lifecycle contract.

### Attempt-bound progress collaborator

`IJobProgressReporter` (**Core**) has signature
`ValueTask ReportAsync(JobProgress progress, CancellationToken cancellationToken = default)`.
Its current implementation is internal `JobProgressReporter` in `Elsa.Jobs`, created by `JobsWorker`
for the exact attempt and supplied in `JobExecutionContext.Progress`; it is not an ambient DI replacement.
Handlers depend on it. A custom worker supplies a reporter only when it preserves current fenced
ownership, shared payload/progress bounds and safe progress; reporting never owns completion.

## Implementable contributor interfaces

### `IJobHandler<TInput,TResult>` — Core

- **Kind:** registered handler contribution under one exact semantic `JobHandlerSupport`.
- **Signature:** `ValueTask<TResult> ExecuteAsync(TInput input, JobExecutionContext context, CancellationToken cancellationToken)`.
- **Registration:** `services.AddJobHandler<MyHandler, MyInput, MyResult>(support)` in `Elsa.Jobs`.
  `MyHandler` is scoped per physical attempt; the immutable support descriptor is application-wide.
- **Aggregation/consumption:** one internal `JobHandlerRegistry` in `Elsa.Jobs` collects contributions;
  `JobsWorker` resolves exactly one compatible registration before entry. This is direct dispatch,
  with no fan-in event or aggregating event handler.
- **Known implementations:** no built-in production handler. `ReportHandler` in
  [`tools/jobs/PackageProbe`](../../../../tools/jobs/PackageProbe/README.md) is a **cross-domain qualification-only**
  consumer of the public API, not a shipped default handler.

Handlers keep the exact declared schemas and implementation identity, observe cooperative cancellation,
and document downstream effect idempotency. CLR type names are not durable semantic identity.

## Events and lifecycle contributions

Jobs currently publishes no domain events and registers no event handlers or fan-in contributor interfaces.
Its runtime contributes `JobsConfigurationValidator` to `IStartupTask` (**cross-domain**, `Elsa.Tasks.Core`)
and optionally `JobsWorker` to `IBackgroundTask` (**cross-domain**, `Elsa.Tasks.Core`). Both are also hosted
services for plain .NET hosts; the shared instances start once. Validation runs before background startup;
the worker polls at configured `JobsOptions.PollInterval` (default 250ms). Its optional bounded materialization loop has an independent interval (default 250ms), fresh command scopes and no physical-attempt slots. See the
[Tasks catalog](../../../../Essentials/src/Tasks/Elsa.Tasks/EXTENSION_POINTS.md) for those contracts.

## Durable composition obligations

Replacement registrations must reject duplicates rather than silently selecting the last provider.
Handler registrations must reject duplicate `(JobType, Version)` and incompatible schema contracts
before processing accepted work. CLR registration descriptors remain implementation-owned; only
the semantic `JobHandlerSupport` crosses the provider boundary.

`IJobCommands` and `IJobQueries` must address the same configured durable authority. A read-only
operations deployment may omit the worker; an ordinary worker may omit API/Studio and workflow
adapters. No workflow design/runtime assembly is required by these contracts.

Workflow pause/capacity gates include the association's exact nonblank `ModelKind` in addition to
the definition/execution name and trusted JobScope. Queue/job-type gates require null ModelKind.
Providers validate this distinction and preserve the whole gate key in identity, arbitration and
query filtering. Native and BPMN IDs may coincide without sharing holds or limits; no model-name
dispatch switch is introduced by this identity component.

The worker uses injected .NET `TimeProvider` for polling/resource waits. The provider supplies the
authoritative time used in durable lease, due, timeout and pause transitions. Qualify the actual
deployment clock/provider guarantee; do not turn an unsynchronized local clock assumption into a cluster claim.

The [README](README.md) specifies each command's responsibility and the distinction between durable
permission, physical execution, selected result and model consumption. The
[lifecycle contract](../../../../specs/1218-standalone-jobs/contracts/job-lifecycle.md) is the
reviewed integration target; model adapters do not transfer their continuation policy into this package.


### Operation audit inspection

The existing `IJobQueries` replacement includes
`ValueTask<JobOperationAuditSnapshot?> FindOperationAuditAsync(JobScope scope, string operationId, CancellationToken cancellationToken = default)`
and `ValueTask<JobPage<JobOperationAuditSnapshot>> ListOperationAuditsAsync(JobScope scope, JobPageRequest page, CancellationToken cancellationToken = default)`.
The EF authority retains the original first operation metadata atomically with each of its eight
existing administrative receipts. A replacement preserves original actor/reason/time and exact
receipt replay, bounded scope/keyset paging, typed target/confirmation metadata without payloads,
and explicit legacy/corrupt-unavailable projections. Hosts authorize these reads; the contract grants
no global or cross-tenant authority and makes no complete per-job history promise.
