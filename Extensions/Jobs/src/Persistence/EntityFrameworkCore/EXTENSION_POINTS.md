# Jobs EF composition

| Surface | Kind | Consumer and replacement |
|---|---|---|
| `AddJobsEntityFrameworkCore` | Composition | Ordinary .NET producer/worker hosts; installs one Jobs authority using the shared host migration policy. |
| `JobsEntityFrameworkCoreFeature` | Inheritable feature | CShell hosts; uses the same registration with normal shell lifecycle migration. |
| `JobsEntityFrameworkCoreOptions` | Host configuration | Provider/connection/schema/pooling, bounded page/claim/retry limits and opt-in terminal payload durations captured at acceptance. Host supplies an engine package. |
| `IJobCommands` | Scoped replacement | Jobs runtime, trusted model adapters and authorized operations; default `EfJobStore` owns atomic mutations. |
| `IJobQueries` | Scoped replacement | Producer lookup and operations; follows the selected combined command provider unless independently replaced. Reads never mutate state. |
| `IJobRecurrenceCalculator` | Pure arithmetic replacement | Schedule materialization; implementation supplied by Jobs runtime, never cursor/lifecycle authority. |
| `JobsRuntime` EF module | Persistence contribution | Shared EF tools and migrator discover the internal provider contexts, separate history table and schema-family content declarations. |

See the [module contract and limits](README.md) and [Core contracts](../../Core/README.md).
