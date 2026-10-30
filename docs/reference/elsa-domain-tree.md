# Elsa Domain Tree

Supports Elsa constitution §E2.1, which pins only the domain names (owner decision D1, #2578). Moved verbatim from Elsa constitution v5.0.0.

Applying framework §2.18's methodology to Elsa, the root-level domains are listed below *(domains
reconciled against the shipped tree 2026-09-16; ETL admitted by [ADR 0087](../adr/0087-etl-owns-its-execution-domain.md) on 2026-10-20; Rules admitted by [ADR 0088](../adr/0088-rules-owns-its-execution-domain.md) on 2026-10-21)*. This table records only what a generated map cannot:
each domain's purpose. It deliberately names no packages. Package names changed faster than this
table was maintained, and by 2026-09-16 roughly a quarter of the names it listed no longer existed.
The generated [domain map](https://github.com/elsa-workflows/elsa-foundation/blob/generated-maps/docs/maps/domain-map.md) owns package enumeration and project counts,
and CI regenerates it after every merge (see the [maps index](../maps/README.md)), so it cannot drift the same way.

| Domain | Purpose (one verb-led sentence) |
|---|---|
| `Elsa.Activities` | Provides the activity system: design-time activity definitions, reconciliation, reusable-activity composition, runtime activity handling, and the built-in activity libraries. |
| `Elsa.Agent` | Hosts AI-agent sessions and capabilities behind provider modules. |
| `Elsa.Api` | Exposes application APIs through the endpoint framework and publishes the client capability document. |
| `Elsa.Attention` | Aggregates permission-scoped attention items from domain contributors behind one query endpoint. |
| `Elsa.Caching` | Provides caching contracts and providers. |
| `Elsa.Diagnostics` | Streams and persists diagnostics: console logs, structured logs, and OpenTelemetry ingestion. |
| `Elsa.Etl` | Executes data transformations over independently recoverable partitions. |
| `Elsa.Events` | Publishes and delivers in-process events (framework §2.6 unified event model). |
| `Elsa.Expressions` | Evaluates expressions inside workflow steps. |
| `Elsa.Foundation` | Provides application-foundation services such as identity. |
| `Elsa.Git` | Wraps the git executable for sources that reconcile workflow definitions from a repository. |
| `Elsa.Http` | Provides HTTP content, routing, download, and cache behavior. |
| `Elsa.Locking` | Provides distributed locking. |
| `Elsa.Mediator` | Routes commands and requests in-process. |
| `Elsa.Modularity` | Discovers, describes, enables, validates, and composes modules and features. |
| `Elsa.Persistence` | Provides the shared EF Core persistence policy that each domain's `*.Persistence.EntityFrameworkCore` module builds on (ADR 0073). |
| `Elsa.Pipelines` | Defines pipeline contracts for composable middleware. |
| `Elsa.Primitives` | Provides dependency-free base primitives and hosting seams (charter in §E2.3). |
| `Elsa.Rules` | Executes finite rule inference with durable round progression. |
| `Elsa.Secrets` | Stores and resolves secrets. |
| `Elsa.Serialization` | Serialises payloads and workflow models. |
| `Elsa.Studio` | Persists Studio client state, currently per-user preferences. |
| `Elsa.Tasks` | Schedules background work inside the host. |
| `Elsa.Workflows.Dashboard` | Serves workflow run-health and portfolio queries from Runtime and Design projections. |
| `Elsa.Workflows.Design` | Designs workflow definitions: contracts, models, validations, reconciliation, and design-time persistence. |
| `Elsa.Workflows.Publishing` | Publishes executable workflow artifacts from designed definitions. |
| `Elsa.Workflows.Runtime` | Executes workflows: instances, execution pipeline, bookmarks, runtime persistence. |
| `Elsa3` | Imports Elsa 3 definitions one-way at the migration boundary (§E2.7). |

Host and test-only groupings that appear in the generated map — `Elsa.Workbench`, `Elsa.Architecture`,
`Elsa.Samples`, `Elsa.Testing` — are not domains and are intentionally absent.

Three domains listed in earlier drafts — `Elsa.Scheduling`, `Elsa.Messaging`,
and `Elsa.Notifications` — do not exist in code and are removed from the pinned
tree; they may return as planned domains when work starts. In-process pub/sub
(formerly `Elsa.Notifications`) shipped as `Elsa.Events` under the framework
§2.6 unified event model.

Sub-domain decomposition follows framework §2.1's naming convention. Variation suffixes are added only when a domain hosts more than one implementation (e.g. `Elsa.Serialization.Newtonsoft` vs `Elsa.Serialization.SystemText`) or when a single implementation already implies a variation choice (e.g. `Elsa.Locking.FileSystem`).
