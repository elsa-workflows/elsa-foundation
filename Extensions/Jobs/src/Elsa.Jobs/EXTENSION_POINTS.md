# Jobs runtime composition

| Surface | Kind and registration | Lifetime and obligation |
|---|---|---|
| `AddElsaJobs` | Plain .NET service composition | Call once; settings snapshot is immutable. Registers scoped default `IJobClient`, startup diagnostics and an optional bounded worker. |
| `AddJobHandler<THandler,TInput,TResult>` | `IJobHandler<TInput,TResult>` contribution under exact `JobHandlerSupport` | Handler is scoped per physical attempt. Logical handler/version duplicates are rejected; schema and implementation pins are checked before entry. |
| `JobsFeature` | Optional public inheritable shell feature, depends on Tasks | Uses the same singleton worker as ordinary hosting; supplies a trusted configured partition/tenant rather than reading tenant scope from input. |
| `IJobClient` | Replacement contract declared by Jobs Core | One scoped implementation; ordinary submissions cannot forge workflow association or durable origin. Provider alone acknowledges durable acceptance. |
| `IJobProgressReporter` | Attempt-bound contract supplied to a handler | Bounded safe progress under the latest fenced lease; separate command scope, no authority over completion. |
| `IJobRecurrenceCalculator` | Core replacement; default UTC interval/cron adapter registered with `TryAddSingleton` | Pure cursor arithmetic through `Elsa.Tasks.Schedules.UtcRecurrence`. One calculator is required at startup; the provider retains all occurrence/cursor and missed-work policy. |
| `IHostedService` / `IBackgroundTask` | Runtime lifecycle contributions | One shared worker per service provider, idempotent start, bounded physical slots, cooperative drain/stop. No second lifecycle or retry authority. |
| `IHostedService` / `IStartupTask` configuration validation | Shared startup contribution | One validator instance diagnoses missing/duplicate authorities in both plain host and shell activation. Tasks awaits startup validation before background startup. |

The registry and immutable options are application-wide values. The worker is singleton because it
owns the host/shell lifecycle and physical slots. Commands, queries, producer client and handler
instances stay scoped. Scoped command renewals never share the handler's persistence context.
The internal schedule pump belongs to that same worker lifecycle and runs one bounded call at a time
in fresh command scopes, without occupying a physical slot or becoming another scheduler authority.

See [Core extension contracts](../Core/EXTENSION_POINTS.md) for provider replacement and durable
semantics, and the [runtime README](README.md) for lifecycle, diagnostics and current delivery limits.
