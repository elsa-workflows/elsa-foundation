# Jobs extension points

Contracts are in `Elsa.Jobs.Core`; implementation composition belongs to the Jobs module and provider.
No contract below qualifies a provider or admits a duplicate lifecycle authority.

| Contract | Kind and consumer | Lifetime/composition | Required guarantees |
|---|---|---|---|
| `IJobClient` | Replacement; ordinary standalone producer | Scoped by default; one configured authority | Durable accepted acknowledgement, bounded validated immutable input, protected-key replay/conflict. |
| `IJobHandler<TInput,TResult>` | Registered contribution; standalone worker | Resolve one scoped handler for its explicit registered semantic identity | Exact declared schemas; cooperative cancellation; documented effect idempotency; no inferred CLR durable identity. |
| `IJobProgressReporter` | Attempt-bound context collaborator; handler | Created/bound for one attempt | Current-lease check, bounded redacted progress, no completion authority. |
| `IJobCommands` | Replacement; client, worker and trusted operations/model adapters | Scoped by default; exactly one durable mutation provider per configured authority | Atomic pause/start/capacity and occurrence acceptance; fenced replay-safe transitions; durable origin recognition; exact result/consumer identity. |
| `IJobQueries` | Replacement; worker and code/API/Studio inspection | Scoped by default; same authority as commands | Read-only bounded exact-scope queries; cursor binding; missing/unavailable distinction. |
| `IJobRecurrenceCalculator` | Replacement; schedule command implementation | Immutable calculator may be singleton | Pure UTC next-occurrence computation; no cursor writes, job acceptance or missed-occurrence policy. |

Replacement registrations must reject duplicates rather than silently selecting the last provider.
Handler registrations must reject duplicate `(JobType, Version)` and incompatible schema contracts
before processing accepted work. CLR registration descriptors remain implementation-owned; only
the semantic `JobHandlerSupport` crosses the provider boundary.

`IJobCommands` and `IJobQueries` must address the same configured durable authority. A read-only
operations deployment may omit the worker; an ordinary worker may omit API/Studio and workflow
adapters. No workflow design/runtime assembly is required by these contracts.

The worker uses injected .NET `TimeProvider` for polling/resource waits. The provider supplies the
authoritative time used in durable lease, due, timeout and pause transitions. Qualify the actual
deployment clock/provider guarantee; do not turn an unsynchronized local clock assumption into a cluster claim.

The [README](README.md) specifies each command's responsibility and the distinction between durable
permission, physical execution, selected result and model consumption. The
[lifecycle contract](../../../../specs/1218-standalone-jobs/contracts/job-lifecycle.md) is the
reviewed integration target; model adapters do not transfer their continuation policy into this package.
