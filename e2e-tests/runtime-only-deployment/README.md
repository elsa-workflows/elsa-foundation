# Runtime-only artifact deployment (spec 151, T126)

Proves spec 151's headline claim against a **genuinely runtime-only engine**: a workflow-executable
closure exported from a publish-capable server through the real endpoint
`GET publishing/workflows/{versionId}/executable-export` is imported and activated at startup, and
executed, by a server that has **no design surface, no publishing surface and no compiler**.

Sibling of [`../artifact-deployment`](../artifact-deployment/README.md), which proves the same round
trip on `Elsa.Workbench`. Keep both: that one is faster and covers the fully-featured engine (it is
where the converter-ordering and capability-collision defects were found), but its single shell
composes design, publishing and the compiler, and environment variables can override a configuration
value while never *removing* a key — so it can only prove *"an engine whose store never held these
definitions"*. This suite closes the gap T126 named, and the two assertions only this shape can
reach: **the importing engine cannot resolve a compiler**, and **the same engine without a locking
feature refuses to start**.

| Script | What it exercises |
|---|---|
| `Test-RuntimeOnlyArtifactDeployment.ps1` | Packs two package feeds from source, runs two `Elsa.Foundation.Host` instances, and asserts import → activation → execution → idempotency → latest-wins, plus the runtime-only and lockless guards. |

## Why two Foundation.Host instances

`Elsa.Foundation.Host` compiles in **no Elsa feature**. It is Nuplane (package feed and assembly
loader) plus CShells (shell activation and routing); every feature arrives as a `.nupkg` and its
`shells.json` ships an empty feature list. That is what makes the claim expressible: instead of
fighting to subtract design from a combined host, each instance composes exactly what it should.

- **Instance A — publish-capable** (`:5401`): design + publishing + runtime. Authors a CHILD workflow
  and a PARENT that waits on a `DispatchWorkflow` of it, publishes both, exports the closure over
  HTTP, and is then **stopped** — nothing that can compile a workflow runs for the rest of the suite.
- **Instance B — runtime-only** (`:5402`): runtime + reconciliation + locking + persistence. Imports
  the mounted closure at startup and executes it.
- **Instance B without a lock** (`:5403`): the same composition minus `FileSystemDistributedLocking`,
  which must refuse to activate.

Neither instance is the developer's server. Separate ports, separate SQLite databases, separate
content roots under `%TEMP%\elsa-runtimeonly`, and **no file in the repository is written to** — each
instance's `appsettings.json` and `shells.json` are generated into its own temp content root and the
shared host binary is launched against it with `ASPNETCORE_CONTENTROOT`. There is also no Groundwork
deploy step: `AutoApplySchemaOnStartup` defaults to true, so each instance builds its own schema.

## Instance B's composition, and why each entry is there

The list was built by addition, not by trimming a plausible-looking server: start with the smallest
set that could work, boot, let it fail, add the one feature the failure demanded. Every entry below
maps to an assertion or to a boot that failed without it.

| Feature | Why it is in a *runtime-only* engine |
|---|---|
| `FastEndpoints` | Maps the runtime API the suite reads. |
| `ApiSecurity` (`AllowAnonymous`) | Reads those endpoints without composing an identity stack. |
| `Serialization` | Decodes the mounted closure; nothing imports without it. |
| `Events` | **Demanded by a failed boot**: `JsonPayloadConvertersInitializingStartupTask` takes `IInlineEventPublisher`. |
| `Mediator` | **Demanded by a failed boot**: the runtime endpoints take `IRequestSender`; endpoint mapping failed shell activation. |
| `Tasks` | Runs the reconciler's startup task (declared `DependsOn`). |
| `FileSystemDistributedLocking` | The reconcile pass is a `[SingleNodeTask]` guarded by `IDistributedLockProvider`. Phase F removes it and asserts the refusal. |
| `GroundworkRuntimePersistenceSqlite` | Durable runtime lane — idempotency and latest-wins are only meaningful across a restart. |
| `WorkflowsRuntimeResumption` | Declared `DependsOn` of the persistence feature. |
| `WorkflowsRuntimeApi` | Serves every HTTP assertion in phases B–D. |
| `WorkflowsRuntimeTriggers` | Declared `DependsOn` of `JsonWorkflowArtifactReconciliation`. |
| `WorkflowsRuntimeRecurringTriggers` | **Demanded by a failed activation**: with a durable store, activation throws *"found an `IRecurringTriggerScheduleStore` with no `IRecurringTriggerScheduleProjectionPreparer`"*. |
| `JsonWorkflowArtifactReconciliation` | The feature under test. |
| `ActivitiesRuntime` | CLR activity registry; the reconciler's `[TaskDependency]`, and what the import gate checks against. |
| `ActivitiesPrimitives` | `WriteLine` — the suite's execution evidence. |
| `ActivitiesSequenceRuntime` | The parent is **Sequence-rooted**, which is T128's proof condition. |
| `ActivitiesDispatchWorkflowRuntime` | The parent waits on the pinned child — the portability claim. |

`ApiCapabilities` is enabled automatically as a declared dependency.

### `GroundworkUnifiedPersistenceSqlite` would have silently broken the claim

The obvious persistence choice is the one `Elsa.Workbench` uses. It is the wrong one here:
`Elsa.Persistence.Groundwork.Sqlite.Unified` reaches `Elsa.Persistence.Groundwork.ReferenceComposition`
→ `Elsa.Workflows.Publishing.Persistence.Groundwork` → **`Elsa.Workflows.Publishing.Core`**, which is
where the compiler lives. Composing it would have given the "runtime-only" engine a compiler while
every other assertion still passed. `GroundworkRuntimePersistenceSqlite` is the runtime-lane package
and pulls none of it. This is exactly the failure mode phase E exists to catch, and it is why phase E
asserts on the **feed contents and the loaded assemblies**, not merely on the engine having started.

## What phase E actually proves

Three independent layers, deliberately, because each one alone has a plausible way of being vacuous:

1. **The feed.** For this host the feed *is* the feature surface — a feature whose assembly is absent
   cannot be composed. No `Elsa.Workflows.Design*`, `Elsa.Workflows.Publishing*` or
   `Elsa.Activities.Design*` package is in it.
2. **The process.** Nuplane logs one line per package it loads; none of the loaded packages match
   those prefixes. The assertion also requires a plausible number of loaded packages, so a traversal
   that found nothing cannot pass.
3. **The routes.** `POST publishing/workflows/{versionId}/publish`,
   `GET publishing/workflows/{versionId}/executable-export` and `GET design/workflows/definitions` —
   the three routes phase A used — all answer **404** on instance B, while
   `GET runtime/workflows/executables` answers 200 on the same host, so the probe is not just
   observing a dead server.

## How execution is observed

By the artifact's own output in instance B's log, not through the instance API.

The runtime instance-detail endpoints are mapped on instance B and reachable, but
`HttpContextActivityExecutionInspectionAuthorizationContext` denies an **untrusted principal** and the
handler then answers 404 — and `ApiSecurity.AllowAnonymous` lets a request through without making its
principal trusted. So on any anonymous host, `GET runtime/workflows/instances/{id}` is 404 regardless
of whether the run succeeded. (Verified on both instances, so it is not a runtime-only limitation;
`../artifact-deployment` does not hit it because it logs in as `admin`.)

Composing an identity stack purely to read a status would add a feature no assertion needs, so the
imported workflow carries per-run stamped `WriteLine` markers instead:

- `CHILD-RAN-<stamp>` — the pinned child from the closure executed on the importing engine.
- `PARENT-V1-RESUMED-<stamp>` — printed by the node *after* a `WaitForCompletion` dispatch and the
  *last* node in the sequence, so seeing it means both "the child reached a terminal outcome" and
  "the parent ran to the end".

Each run reads only the log lines produced after its own dispatch.

## Feeds

Two directory feeds per instance, and both parts matter:

- **Feed 1** — the `Elsa.*` packages this suite packs from source.
- **Feed 2** — the genuine third-party packages those nuspecs declare, copied out of the global NuGet
  cache. Nothing is downloaded; the run is offline and deterministic.

Nuplane never requests packages the host runtime already provides (`CShells.*`,
`Microsoft.Extensions.*`), so framework packages must **not** be in a feed. But every other declared
dependency must resolve from *some* feed, and **one unresolvable package fails the entire
reconciliation cycle and aborts startup** — an all-or-nothing behaviour whose log names the missing
package but never which package wanted it.

**Packing is unconditional on every run** (T126, Joey): a feed holding packages from an earlier build
would let instance B run stale code and pass — or fail — for the wrong reason. `dotnet pack -c Release`
with **no version flags**; overriding the version cascades into the nuspec's dependency versions
(ADR 0067). Both closures are packed once into a staging folder and the two feeds are filled by
copying out of it, so the ~35 packages they share are not built twice.

`-SkipPack` reuses the previous run's feeds. It exists for iterating on the assertions and
deliberately breaks the freshness guarantee; do not use it to judge a code change.

### Three framework assemblies must be shared with the host

`Elsa.Api.FastEndpoints` declares `Microsoft.AspNetCore.Http.Abstractions` **2.3.10** — the last
standalone release of an assembly that is now part of the ASP.NET Core shared framework
(`Directory.Packages.props` explains why the pin is correct and must not be "fixed"). Nuplane resolves
that package and loads the 2.3.10 copy into the shell load context, after which every feature assembly
touching the modern surface dies with
`Could not load type 'Microsoft.AspNetCore.Builder.IEndpointConventionBuilder'` and its features vanish
from the catalog. The package still has to be **present** for resolution, but the **loader** must take
the host's copy, which is what `Nuplane:Loading:SharedAssemblies` is for. The generated `appsettings.json`
shares `Microsoft.AspNetCore.Http.Abstractions`, `Microsoft.AspNetCore.Http.Features`,
`System.Text.Encodings.Web` and `System.Memory` alongside the three CShells contracts.

## The CLR activity scan folder

Instance A's design catalog is populated by `ClrActivityReconciliation`, which scans a **folder** of
assemblies with a reflection-only `MetadataLoadContext`. `Elsa.Workbench` gets this for free because
its `bin` folder holds every activity assembly; this host's base directory holds none — the activity
assemblies live inside `.nupkg` files. So the suite extracts `lib/net10.0` out of the packed
`Elsa.Activities.*` packages into a flat scan folder and points `Options:FolderPath` at it. Without
it, instance A starts happily and reports **zero** activities, and phase A fails at the first
`Get-ActivityVersionId`.

## Caveats

- **Slow by construction.** It packs ~100 projects in Release on every run and starts five host
  processes. Budget on the order of twenty minutes.
- Uses ports **5401**, **5402** and **5403**, not 5095 — it does not touch, stop or restart the
  developer's server, and can run alongside it.
- Requires only a built solution (`dotnet build`). No Groundwork deploy step, no login, no seeded
  identity.
- Everything it creates lives under `%TEMP%\elsa-runtimeonly`. The per-run directory (content roots,
  databases, mount, staging) is removed at the end; the packed stage and the feeds are kept because
  they are large, rebuilt next run anyway, and useful when a failure needs explaining.
- Host logs are written to `%TEMP%\elsa-runtimeonly-<label>.out.log` (labels: `publish`, `runtime-b`,
  `runtime-c`, `runtime-d`, `lockless`) and survive the run.
