# Kernel model

A standalone model of the execution kernel ([DESIGN.md](DESIGN.md): the design as built, against draft 5 of the
sketch, in three layers), the stores it runs on, the native Elsa workflow model on it, and a simulation that tries to
break it all. The prototype keeps its own build settings. The relational provider imports the repository's
central package versions. Its projects reference the backend
[Actor Foundation packages](../../essentials/src/ActorFoundation/README.md), so the public contracts and Embedded
tenant binding use the same assemblies as a package consumer.

What was found is in [FINDINGS.md](FINDINGS.md) (section 27: the runtime conformance suite and the Orleans adapter;
section 26: the graph engine; section 25: round 4's decided items; section 24: the three layers and the round 4 names;
section 23: the merge of the four branches and its gates); what each database engine does to the EF store is in
[ENGINES.md](ENGINES.md), the store's own commit SQL (round 5 B) in [COMMIT-SQL.md](COMMIT-SQL.md), MongoDB in
[MONGO.md](MONGO.md), native workflows as an execution model in [NATIVE.md](NATIVE.md), virtual partitions and the fence
granularity in [PARTITIONS.md](PARTITIONS.md), and write volume and bloat on PostgreSQL in [WRITES.md](WRITES.md).

## Layout

| Path | What |
|---|---|
| `../../essentials/src/ActorFoundation/BuiltIn/Runtime` | Layer 1, the actor runtime: the seam (`IActorRuntime`, `IScopeHost`, `IScopeActivation`), the wake signal, and `PartitionRuntime`, the built-in runtime (balancer, activations, one pass at a time per partition); addresses, flaws, the owner's defaults |
| `src/Kernel.Durable` | Layer 2, durable actors: `Worker` (a partition's activation: inbox, fenced commit, timers, background jobs, events, poison, journal), `DurableHost`, the seam to layer 3 (`IActorType`, `DurableTurn`), the store contract, the in-memory store, the projector, reply addresses for live output |
| `src/Kernel.Workflows` | Layer 3, actor types: `WorkflowInstance` (status, definition, parent and children, suspend, resume, terminate, rebind, delete, the history envelope) and the execution-model contract (`IExecutionModel`, `ITurn`) |
| `sim` | Toy execution models, the deterministic simulation, the invariant checks, the shrinker and the mutation check; `NativeSim.cs` puts native instances in the mix |
| `models/Graph` | The graph engine: main's Flowchart engine (paths, arrivals, dead paths, gateways) as a library with no kernel or Elsa reference; README says what was adapted |
| `models/Graph.Tests` | Main's Flowchart conformance corpus and join tests against the graph engine |
| `models/Native` | The native Elsa workflow model: authoring surface, activities (its Flowchart on the graph engine), interpreter, HTTP routes and host on the kernel's live output |
| `../../essentials/tests/ActorFoundation/Native/Run` | Native smoke tests, cost and memory measurement, PostgreSQL Core Preview |
| `ef/Kernel.EntityFramework` | The storage interface on Entity Framework Core: PostgreSQL and SQLite in `EfStore.cs`, SQL Server and MySQL dialects in `Engines.cs`; the commit as the store's own SQL in `EfStore.Owned.cs` and the `*CommitDialect.cs` files ([COMMIT-SQL.md](COMMIT-SQL.md)) |
| `ef/Kernel.Ef.Run` | The cost measurement and the concurrent stress test; `EngineRun.cs` runs them on PostgreSQL, SQL Server, MySQL and SQLite (`x ...`) |
| `../../essentials/tests/ActorFoundation/Store/EntityFramework` | The conformance suite against the EF store, every engine |
| `../../essentials/tests/ActorFoundation/Store/Conformance` | The store conformance suite: semantic cases, the stress test, the cost runs, for any provider |
| `conformance/Kernel.Store.Conformance.Run` | The suite against the in-memory store |
| `conformance/Kernel.Runtime.Conformance` | The runtime conformance suite: semantic cases, the fence broken on purpose, the stress test, for any actor runtime |
| `conformance/Kernel.Runtime.Conformance.Run` | The runtime suite against the built-in runtime |
| `src/Kernel.Durable/ActorContract` | The shared actor-turn adapter and header checks consumed by the built-in providers |
| `../../essentials/src/ActorFoundation/BuiltIn/Hosting` | `RuntimeNode`: the built-in runtime of one node on a clock |
| `mongo/Kernel.Mongo` | The storage interface on MongoDB |
| `mongo/Kernel.Mongo.Run` | The suite against MongoDB, its connection killer, probes |
| `experiments/VirtualPartitions` | Round 5 A: the two fence granularities of virtual partitions (per virtual partition, per owned range) as EF stores, the measurement harness and the ownership simulation; results in [PARTITIONS.md](PARTITIONS.md) |
| `experiments/K1.LockOrder` | K1 slice 3: two overlapping range commits on PostgreSQL, SQL Server and MySQL, with the k fence rows locked in ascending partition id and, as the negative, in batch order; [its README](experiments/K1.LockOrder/README.md) |
| `experiments/Astra4.Sizes` | The fourth gate 2 review's size and identity probe: the real event-match encoding, placement hash and Native background arm, measured beside the proposed compact forms; see its [README](experiments/Astra4.Sizes/README.md) |
| `experiments/Review4.Blockers` | Gate 2, fourth review: FR-1 (R4-B1, release cohort precedence) and RT-1 (R4-B2, the retirement obligation) on the real Native model and creation protocol, each under the reviewed, repaired and negative rules; [its README](experiments/Review4.Blockers/README.md) |
| `experiments/Review5.Delivery` | Gate 2, fifth review: EV-1 (R5-M2, event eligibility by the match step, the entitlement point and the sweep's coverage), FR-2 (R5-M3, the drain's handoff credit), JR-1 (R5-M4, a fenced job result across Park) and PO-1 (R5-M6, precedence in the poison comparison) on the real in-memory store and Native model, each under the reviewed, repaired and negative rules; [its README](experiments/Review5.Delivery/README.md) |
| `experiments/Review5.Termination` | Gate 2, fifth review: RT-2 (R5-B1, idempotent cancellation of a job ticket; R5-m3, generation-carrying jobs), RT-3 (R5-B2, the paged, durable cascade, ThenDelete and key reuse while Discharging) and RT-4 (R5-M5, the parent binding of kernel-marked terminates at running and ended recipients, with the open-drain and marker paths), every turn's commit through the real `InMemoryStore.CommitAsync`, each under the reviewed, repaired and negative rules; [its README](experiments/Review5.Termination/README.md) |
| `experiments/Review6.Blockers` | Gate 2, sixth review: NT-1 (R6-B1, the model's terminal growth reserved in the envelope check, on the real Native `TerminateMetadata` and `WorkflowInstance` terminate, with the measured 7 bytes per open invocation inbox) and PI-1 (R6-B2, deliveries bound to a publication incarnation, on the real in-memory publish, match, catch-up and sweep paths; event-first's split sweeps are harness batches that remove rows directly, so the built `SweepAsync` is no evidence for that interleaving), each under the reviewed, repaired and negative rules; [its README](experiments/Review6.Blockers/README.md) |
| `experiments/Review7.Blockers` | Gate 2, seventh review: NT-2 (R7-B1, the terminal projection counts the retained `CustomHandles`, `Model` and `Definition` at their encoded size, on real detached starts through `Turn.Start` and `DurableTurn.StartChild`, the real `WorkflowInstance` terminate and the in-memory commit, whose `Refuses` hook holds the row cap measured as the JSON UTF-8 size of the row's fields) and MX-1 (R7-M1, workflow-issued event ids carry a reserved `~` and the outside publish refuses it, on the real `DurableTurn.Publish` and the in-memory publish, match and delivery paths), each under the reviewed, repaired and negative rules; [its README](experiments/Review7.Blockers/README.md) |

## Build

One project at a time (they share `obj/` folders through project references):

```
for p in sim ../../essentials/tests/ActorFoundation/Native/Run ef/Kernel.Ef.Run ../../essentials/tests/ActorFoundation/Store/EntityFramework conformance/Kernel.Store.Conformance.Run mongo/Kernel.Mongo.Run conformance/Kernel.Runtime.Conformance.Run; do dotnet build $p -c Release; done
dotnet test models/Graph.Tests -c Release      # the graph engine's corpus
```

## Inspect and retry parked work

From `prototypes/kernel-model`, run:

```bash
dotnet restore sim --locked-mode
dotnet run -c Release --no-restore --project sim -- --park-operator-demo
```

An approval actor parks when its service is unavailable. The operator lists parked actors through
`IActorReads`, opens the actor's inbox, and sees a consumed approval and an update that arrived
during Park on separate pages. A retry command uses the consumed approval's opaque handle.
After the service is available, the actor resumes and the journal confirms that each input
applied once. A second Park is discarded. The example shows the inbox rows that remain in
each open resolution. The fixture uses the in-memory Actor Foundation. Provider projections,
Studio integration and the remaining operator operations have their own milestone acceptance.

## In-memory workflow run history

`WorkflowRunList` reads a tenant-scoped projection of workflow checkpoints. A workflow enters the list
at its first fenced checkpoint. Later checkpoint commits, including changes with no public journal entry,
supply its `UpdatedAt`. Retirement removes the projected row. The projection uses the foundation clock
that records each accepted checkpoint.

The query applies inclusive UTC `From` and `To` bounds to the projected time. Pages sort by that time
descending and exact workflow reference ascending. The opaque bookmark is bound to the tenant and filters.
Pages read current committed metadata, so a workflow updated during a traversal can move before its bookmark.
The product's historical fallback fields remain relevant to older execution records. This slice covers new
in-memory actor-backed runs and their own read route.

Run the Native workflow witness from `prototypes/kernel-model`.

```sh
dotnet run --project sim -c Release -- --workflow-run-history-check
```

## Migration plan outcomes from the journal

From `prototypes/kernel-model`, run:

```sh
dotnet run -c Release --project sim -- --migration-plan-reads
```

This K9 example projects one plan's committed journal through `IActorReads`, using its opaque page token.
It counts each selected exact actor reference once. An answer of `NotAtSource` can be replaced by a decisive result.
The first decisive result survives replay. An unanswered actor is pending before close and has no answer after close.
Close fixes the selected set. A late reply for a selected actor still updates the closed plan. A closed empty plan
has no missing answers. A cancelled page leaves its bookmark in place for a retry. A fresh projector recovers
the same results from the journal. A plan runner tracks selection completion separately from answers to the
selections the projector has already seen. The view’s `AsOf` is a lower bound on confirmed catch-up. An empty
journal read at the current bookmark advances it to the database time of that read. While applying nonempty
pages the view keeps its previous catch-up time, initially zero.

The example stores its view and bookmark in memory and uses a fixture to write the plan journal from real actor turns.
The persisted view and its paging are separate K9 work. K11 supplies the migration plan actor, exact-ref dispatch
and operator controls.

### Persisted plan outcomes in SQLite

From `prototypes/kernel-model`, build the runner and give it a new SQLite file:

```sh
dotnet restore ../../essentials/tests/ActorFoundation/Native/Run --locked-mode
dotnet build -c Release ../../essentials/tests/ActorFoundation/Native/Run --no-restore
dll=../../essentials/tests/ActorFoundation/Native/Run/bin/Release/net10.0/Kernel.Native.Run.dll
scratch=$(mktemp -d)
dotnet "$dll" sqlite-plan-projection build "$scratch/plan.db"
dotnet "$dll" sqlite-plan-projection inspect "$scratch/plan.db" <plan-ref> <digest>
```

Use the plan reference and SHA-256 digest printed by `build`. The inspector checks the exact actor/outcome pairs, closed flag, catch-up time and source bookmark against that receipt. A fixture actor records batches and the exact senders' outcomes in
the public journal. A SQLite transaction persists each page and its source bookmark together. A failed insert
rolls back both. Cancelling after either an empty or a populated source read preserves the page for retry. Two projectors can read the same source page, with one committing it. A new projector resumes
at the stored bookmark. The stored `AsOf` is a lower bound on confirmed catch-up. A partial page keeps the
previous time. An empty tail advances it only if the stored bookmark still matches, including the first empty
read. The independent `inspect` process reads three stable pages with one migrated, one refused
and one unanswered actor from the closed plan. A separately tenant-bound reader checks the populated neighboring scope.

This verifies the physical SQLite view and local keyset reads. The journal producer in the fixture uses the
in-memory Actor Foundation. Connecting this persisted view to the existing in-memory K11 migration plan runner
remains open, along with support for other view providers.

To page the persisted result through the operator read contract, use the same file and printed plan reference:

```sh
dotnet "$dll" sqlite-plan-projection list "$scratch/plan.db" <plan-ref> 1
dotnet "$dll" sqlite-plan-projection list "$scratch/plan.db" <plan-ref> 1 '<Next from previous result>'
```

For a projection file created before operator paging was added, run
`dotnet "$dll" sqlite-plan-projection enable-paging "$scratch/plan.db"` once. It initializes the continuation
key and the cursor index without rebuilding the view. Repeating it preserves existing continuations.

Each JSON page has `Closed`, `AsOf`, `Items` and `Next`. A continuation is opaque and stays valid across
a new CLI process. It is tied to the plan, the page size and the trusted host tenant. It carries a stored row number and supports opaque references of different lengths. The CLI binds the demo
tenant and reads the view file locally. Application hosting and operator authentication are separate work.

### Dispatch a committed migration batch

From `prototypes/kernel-model`:

```sh
dotnet run -c Release --project sim -- --migration-plan-dispatch
```

This K11 dispatch example reads a committed batch and sends `Migrate` outside actor turns to its exact actor refs.
A lost admission reply leaves the batch pending. Within the FC-16 receipt window, the restarted runner sends the
same operation IDs and receives deduplication receipts. A deleted selection stays unanswered even when the caller key
has been reused. The journal shows one result per responding life in this run. Authorization and input-size refusals
leave the batch pending. An acknowledgment retry inside the same window receives a duplicate receipt.

The fixture uses the real in-memory operator route for `Migrate`. Its selection and acknowledgment messages
are trusted fixture control inputs. The product adapter must authorize those controls. The product plan actor,
operator scheduling and target-side [long-gap replay handling](../../docs/architecture/migrating-running-instances.md#plans-and-bulk-migration)
remain separate K11 work.

### Validate a waiting Native checkpoint

```sh
dotnet run -c Release --project sim -- --native-migration-validation
```

This K11 check loads real Balanced and Durable checkpoints through the in-memory Actor Foundation.
It tries a new Sequence tail, a change to the untaken If branch and a reordered Parallel. The check
also covers loop bodies and nodes referenced only by a stored result, scheduled child or pending trigger.
The validator is read-only and handles structured Native checkpoints with unchanged declarations.
Callers must verify actor status, source definition and version readability before using it.
This validation covers the same checkpoints used by the command demonstration below. Adding defaults,
outstanding jobs, Flowchart and StateMachine compatibility are separate work.

### Apply a compatible Native migration

```sh
dotnet run -c Release --project sim -- --native-migration-apply
```

The public in-memory command suspends a waiting Native workflow, rebinds a compatible Sequence and
delivers its result to a plan witness. The workflow resumes on the same event wait and executes the
target tail. Balanced and Durable runs lose the answer before and after the migration commit. A cold
owner completes the schedule with one result and one successful lifecycle entry under Full, State
and Minimal history. An older model holds commands on unreadable running and finished checkpoints.
A newer owner processes those commands when fresh input releases the hold. A request with a usable
plan address and a missing definition receives one durable refusal across repeated inputs. The plan witness
exercises real actor delivery. Declaration mapping and graph and statechart migration remain separate work.

### Dispatch a real migration plan

```sh
dotnet run -c Release --project sim -- --migration-plan-actor
```

The in-memory demo pages two Native workflows from the public instance read model. The plan model
records one undispatched page in its Full journal. An independent runner reads the committed page
and dispatches migrations to the exact actor references. A fresh runner reconstructs the same
batch after ownership changes. An authenticated acknowledgment clears the batch. The operator host
provides a shared control key to plan owners and runners. Before releasing a batch, the runner
checks that the exact plan is running, unparked and within its deadline. Its completion check waits
for the final selection, its dispatch acknowledgment when needed, and an outcome for every selected life.
The foundation's actual creator settings must permit durable Full history. The outcome projection reports both
migrations, and the workflows finish under the target definition. A second plan closes on its
scheduled deadline and records an outcome that arrives after close. RetryAll re-arms a failed deadline.
Terminating a Park closes the outcome view and leaves outstanding selections as unanswered.
Operator API integration,
automatic selection scheduling and provider qualification remain separate work.

### Run migration selection and completion

```sh
dotnet run -c Release --project sim -- --migration-plan-runner
```

The in-memory operator service selects through the public actor list, dispatches the committed
batch, and requests completion once the result projection has reached the final selection
and every selected life has answered. Each automatic plan binds one execution model.
A provider page can span several 128-actor batches. The runner keeps the read cursor until
it has recorded a selection for every eligible life on the page. Step results report admission.
Close reports whether it admitted a command. A caller can retry after the creator turn becomes visible.
The plan actor records the resulting transition in its own commit. The host grants tenant operator permission
and invokes the runner as work becomes available.

The example restarts after a lost selection admission answer, completes a plan over two actual
Native workflows and closes an empty plan. A 129-actor page completes in two batches even
when all selected actors refuse migration and remain at the source. Another plan keeps an unanswered deleted life
pending until an operator closes it. Public HTTP and Studio endpoints, scheduling by the
provider host and persisted outcome projection integration remain separate work.
Earlier draft plans without a model binding still support their explicit batches. The automatic
runner requires a binding when it creates a plan. The host enables those starts after every
eligible owner can record the model binding on its first turn. An older owner with an existing
version 2 checkpoint withholds it until a compatible owner takes over. A version 2 owner can
terminate a version 1 pending plan within its original terminal state reserve.

## Timed jobs on the PostgreSQL Worker

From `prototypes/kernel-model`, set `KERNEL_PREVIEW_POSTGRES` to a fresh dedicated database:

```sh
dotnet restore ../../essentials/tests/ActorFoundation/Native/Run --locked-mode
dotnet build -c Release ../../essentials/tests/ActorFoundation/Native/Run --no-restore
dotnet ../../essentials/tests/ActorFoundation/Native/Run/bin/Release/net10.0/Kernel.Native.Run.dll pg-timed-job-worker checked
```

Use `in-batch` on another fresh database for the other owned SQL fence mode. The Worker commits the issued
job ticket, its absolute deadline, the waiting checkpoint and journal in the same PostgreSQL transaction.
A callback publishes before the deadline. The worker resumes only once the database clock reaches the deadline,
applies the job result and recalls the timer. The same actor starts another job. A new owner retries its
pending ticket before the deadline. After expiration, a third owner counts one abandoned trial, validates the
old fence is refused and delivers the deadline without entering the callback. The physical inbox is empty and
the journal shows one outcome per job even after the displaced callbacks return.

This covers the legacy relational actor protocol. In that path a Worker completion consumes the original issued
ticket and commits the actor's result directly in its checkpoint and journal. The public generated-root path
still needs a qualified writer for tracked job generations and private ticket, deadline and result envelopes.

## PostgreSQL Core Preview

Set `KERNEL_PREVIEW_POSTGRES` to the connection string of an empty PostgreSQL 17 database dedicated to the preview.
Run this from `prototypes/kernel-model` with the SDK in [global.json](global.json).

```bash
../../essentials/tests/ActorFoundation/Native/Run/run-core-preview.sh
```

The first Embedded host starts a Native approval workflow through the public Foundation contract, records Ada's approval,
and exits at the second wait. The next process opens the same database and reads from the first process's journal bookmark.
It sends Ada's operation ID again with a different payload, then sends Grace's approval with a new ID. The stored
serialized workflow state and checkpoint revision stay unchanged through replay. The journal and final result verify that each approval applied once.
Between the two owner processes, a separate diagnostic process bound to another tenant checks that it cannot
send to the workflow or read its checkpoint or journal. This probe uses no measured connection.

The default receipt adds EF commands and explicit transaction completions. It excludes connection setup
and the provider's direct PostgreSQL batch for each admission. Enable the wire meter for the full count. Setup, ownership and journal reads have their own rows.
The relational store reads its format marker during schema setup, each nonempty take or retake, and each renewing heartbeat.
The preview counts these reads in setup and ownership. A relational format advance uses a coordinated stop. Fully stop every
predecessor process before the first advance if it was built before the marker-lock protocol. The missed-stop check serializes
only readers that already use that lock. The migration race fixture runs such a reader against the previous stored format.
The approval and replay rows include public admission and the Worker pass. Diagnostic checkpoint probes and deliberately
refused commits use a separate unmeasured client. The demonstration scopes execution to one generated root and direct messages.

Initialize the PostgreSQL store with `EfStore.CreateAsync`, then call
`EfStore.InstallPreviewRootSchemaAsync` with the ordinary `KernelDb` context. Run installation during setup
or while existing owners and callers are stopped. It preserves issued references, deduplication receipts
and cursor keys. On an older preview that lacks the actor-list projection, it creates and backfills the projection.
After `pg-core-preview birth` exits, `pg-core-preview install-probe <reference> <bookmark>` in the same
disposable database checks backfill by concurrent installer calls and verifies bookmark and receipt continuity.

The Embedded preview composes `IIngress` through `EfStore.OpenPreviewRootIngressAsync`.
A trusted host supplies a bound scope, the actor type and the receipt retention window. The provider accepts
durable starts for that type and ordinary direct input to exact roots. Opening the adapter checks the
installed schema. Each admission holds the shared store-format lock in its write transaction.
A PostgreSQL batch first locks the marker and then reads the issuer or exact root in one round trip. The ordinary adapter refuses privileged controls. Held requests and event-based starts fall outside
this adapter's scope.

For a format-refusal check, run the Native `pg-core-preview birth` phase in a fresh disposable database,
then run `pg-core-preview format-probe <reference>` in a new process against the same database.
The probe verifies typed refusal after the format changes. It also starts admission while a migration
holds the marker, confirms the admission is waiting, then runs the migration's DDL. Both write paths
must refuse the changed schema without a lock cycle. The probe restores the marker and tables. Its accepted control writes leave test data in that database.

A host can use `EfStore.OpenPreviewRootReadsAsync` to open `IActorReads` for an already-installed ordinary
PostgreSQL generated-root store. Compose the store with the public `new KernelDb(options)` PostgreSQL context.
The trusted hosting layer supplies the caller's `BoundScope`. The provider supplies point, list and journal reads
with bookmarks that cross process restarts. Other preview profiles require their matching internal context.
Inbox inspection and operator selections remain full-provider work.

The preview's public `IActorReads.GetAsync` and `ListAsync` use a persisted, tenant-indexed projection of generated roots. PostgreSQL
updates the list within the fenced metadata commit, including suspension changes that leave no public journal entry.
Point queries use the tenant and exact reference. Point and list queries refuse when either required source trigger
is missing, disabled or bound to a different function.
The trigger functions belong to the source table's installation schema. Run preview setup with that schema
active so all preview relations and trigger functions are installed together.
If the projection trigger was disabled while writing checkpoints, rebuild the projection from committed metadata before serving it again. List queries use a reference bookmark bound to the tenant and filters. The existing root schema
installer backfills public metadata for earlier preview databases. This slice represents running and finished roots,
and leaves the release-wide projector, Park, and operator selections to K9 follow-up.

On a fresh disposable PostgreSQL database, `pg-root-list <owned|ef> <checked|in-batch> birth` prints a
`projection-bookmark`. Run `pg-root-list <owned|ef> <checked|in-batch> restart <bookmark>` against that database
from a fresh process. The birth phase renames the owner lookup columns and journal table after it captures the bookmark.
Both reads still work against committed metadata, with the trigger check bound to the canonical owner table. The fixture also checks that a foreign reference returns no actor.

The opt-in PostgreSQL workflow-history projection is installed via `EfStore.InstallRootWorkflowHistoryAsync`
after the root admission schema. A version 1 marker, timestamp table, chronology index and checkpoint trigger
are installed in one transaction. The trigger checks the exact bound root and qualifies the checkpoint revision.
It stores the database clock in Unix milliseconds, reusing the stored terminal time on completion. It advances
with ordinary journal-silent checkpoints and rolls back with the owner transaction. It adds one server-side
projection upsert with its indexes per qualified checkpoint. The worker sends the same SQL commands as before.

The chronology route uses inclusive UTC bounds at the precision of the caller's dates and orders results by
timestamp descending and exact reference ascending. It joins the timestamp and actor-list projections. Reads
check both format markers and the trigger chain on the current owner relation in the same statement. A canonical
replacement must have all the required triggers. A rename with no canonical replacement can still serve the
existing projection from the same installation schema. Re-enable a trigger only if owner writes were quiesced throughout the disabled interval or
after an explicit rebuild from authoritative data. Missed silent checkpoint timestamps cannot be reconstructed
from a later checkpoint or from the public journal. The generic actor list still shows prior roots, which enter chronological history
on their first checkpoint observed after installation. Physical workflow retirement, legacy timestamps and
product routing remain outside this preview.

On a fresh disposable PostgreSQL database, run `pg-root-workflow-history <owned|ef> <checked|in-batch> birth`
with the Native runner. Birth checks that a run finishing at revision 1 shares its terminal time and database
transaction with the journal and both projections. Then it renames the checkpoint and journal tables so
`restart <projection-bookmark>` and `tenant <projection-bookmark> <tenant-reference>` exercise both projections
from separate processes. Use the bookmark and reference printed by birth.

On a fresh PostgreSQL database, `pg-root-history <owned|ef> <checked|in-batch>` on the same runner
exercises two ordinary turns with no journal entries. The Minimal workflow saves its first approval. The Full
workflow consumes an unmatched input admitted while its scheduled start was pending. The owner offers the
creator at its deadline and releases the input after the first checkpoint. Each workflow reaches revisions
1, 2 and 3. Its journal has two contiguous positions for revisions 1 and 3. The public tail resumes from
the start bookmark and returns the recorded entries in revision 3.

To measure TCP turns as well, use a dedicated loopback database with `Host=127.0.0.1`, its published port and
`SSL Mode=Disable` in `KERNEL_PREVIEW_POSTGRES`. The meter disables optional GSS encryption for its connection
and refuses `GSS Encryption Mode=Require`.

```bash
KERNEL_PREVIEW_MEASURE_WIRE=1 ../../essentials/tests/ActorFoundation/Native/Run/run-core-preview.sh
```

The meter counts the client's first send and each subsequent send after a server response. Pipelined requests can
share one turn. Each process opened one measured TCP connection during setup and reused it for all rows below.
Across three fresh databases, PostgreSQL 17.12 and Npgsql 10.0.3 gave these counts with pooling enabled and
multiplexing disabled. The output also reports PostgreSQL Sync and Query groups and new connections per phase.

| Operation | EF estimate | Observed TCP turns |
|---|---:|---:|
| Start admission | 5 | 7 |
| Start turn | 12 | 12 |
| First approval including admission | 18 | 20 |
| Duplicate approval after restart including admission | 5 | 5 |
| Finishing approval including admission | 19 | 21 |

In two before-and-after runs on PostgreSQL 17, schema setup on an empty database took 34 or 35 TCP turns before
the format check and 37 with the marker table, seed and read. Both the first and restarted process took 13 TCP
turns for ownership, up from 12. The five workflow operations above kept their measured counts.

The first public PostgreSQL readers checked the format when opening the journal cursor. On two fresh PostgreSQL
17.12 runs with that check, the first process reported 38 or 39 TCP turns for setup, the restarted process reported
six, and both still reported 13 for ownership. The five workflow phases retained their earlier counts. The current
reader checks the format in each list and journal page statement.

The relational schema marker is now 2 for the readable format columns on node rows. Schema 1 preview databases
refuse open and acquisition before a node-row write. Use a fresh dedicated preview database for this revision.

The general relational projector checks the format before reading the cursor, reading a journal page and
writing a projection. A PostgreSQL 17 round that advanced the cursor measured six TCP turns with those
checks, up from three. The general journal reader, view reader and sweeper each check the marker on entry.
The public preview uses separate tenant-scoped checkpoint and journal queries.

To run two tenants' Native approvals on the same Embedded worker, use a fresh dedicated database. After building
`../../essentials/tests/ActorFoundation/Native/Run` in Release, run the following commands from `prototypes/kernel-model`. Supply the three
references printed by the first process to the second. The third identifies a root outside the owner's set.

```bash
dll=../../essentials/tests/ActorFoundation/Native/Run/bin/Release/net10.0/Kernel.Native.Run.dll
birth=$(dotnet "$dll" pg-multi-root owned in-batch birth)
read -r label first_ref second_ref outside_ref <<< "$(printf '%s\n' "$birth" | awk '$1 == "multi-references"')"
dotnet "$dll" pg-multi-root owned in-batch restart "$first_ref" "$second_ref" "$outside_ref"
```

The fixture checks the PostgreSQL transaction IDs for two journal rows at different actor revisions, replays a
direct input after restart, and checks that a terminal refusal on one root rolls back both turns. The second
tenant issues its public requests in a separate process bound to that tenant. The writer choices
are `owned` and `ef`. The fence choices are `in-batch` and `checked`.

To let an Embedded worker discover public roots from their partition, use a fresh dedicated database and run:

```bash
dll=../../essentials/tests/ActorFoundation/Native/Run/bin/Release/net10.0/Kernel.Native.Run.dll
dotnet "$dll" pg-discover-roots birth 0
dotnet "$dll" pg-discover-roots restart 0
```

The first process activates an empty partition, admits roots and leaves a final approval ready. The second process
takes the same partition and finishes that approval before the observer looks up any actor references. The poll
joins eligible inbox rows to their admitted births, respects its row limit, and returns tenant and reference with
the creator. Direct inputs admitted before a scheduled birth become eligible after its first checkpoint. The fixture
checks that an independent ready root runs during that interval, a replay adds no turn, and the other partition keeps its pending work.
Each tenant uses its own fixed-scope public client process. To select a writer and fence, append `owned` or `ef`
and `in-batch` or `checked` to both commands. This preview runs one registered workflow actor type on its dedicated database.

To run two processes under the built-in Cluster runtime, point `KERNEL_PREVIEW_POSTGRES` at a fresh dedicated
PostgreSQL database and run the following after building `../../essentials/tests/ActorFoundation/Native/Run` in Release:

```bash
dotnet ../../essentials/tests/ActorFoundation/Native/Run/bin/Release/net10.0/Kernel.Native.Run.dll pg-cluster-roots 1024
```

Each Native application explicitly registers `PostgresPreviewHostedService` in its .NET Generic Host.
The service starts the node through `PostgresPreviewHosting.StartAsync` and stops it during application shutdown.
The application passes the installed PostgreSQL context factory, a distinct node name and its public actor type.
All nodes on the database use the same actor type and definitions. The launcher checks the installed
partition layout and runs the owner-call watchdog. Supply a process-termination policy for a call
that remains stuck after cancellation or when the application's shutdown deadline expires. The application
stops and disposes its Generic Host. A direct caller of the launcher owns the returned runtime. The hosted service's
`LastError` reports the latest exception caught by the runtime loops. The driver's start, direct input and journal
reads use the EF provider's public surfaces. The Flowchart and StateMachine qualification fixtures keep their own setup.

Run `pg-preview-host-lifetime` on a fresh disposable PostgreSQL database to watch application shutdown
keep renewing through fenced drain passes while an entered turn waits beyond one lease. The completed
sibling commits first and the slow turn commits before release. A replacement application resumes the root,
then verifies the saved journal bookmark and replay.

To check launcher validation, a plain public actor and watchdog escalation, run
`pg-preview-host-watchdog` on a fresh disposable PostgreSQL database. After the actor completes, the fixture
stalls an entered heartbeat and observes the escalation callback before releasing the stall. The fixture makes
that callback throw and checks that the hosted service reports the exception through `LastError`.

The first node checkpoints roots on distinct partitions in one PostgreSQL transaction. A second OS process joins
with its own RuntimeNode. A single store snapshot shows the two nodes each own half the partitions. The second
node holds the same epoch from the first approval's admission through its checkpoint, then stops gracefully.
The fixture waits until the surviving node owns every partition and observes the workflow's partition at a higher
epoch. Replaying that approval is a duplicate. A new approval completes the workflow with one new journal revision. Both hosts discover their roots from their
assigned sets. This preview covers generated public roots, direct input and graceful handover with the PostgreSQL
owned-SQL writer. Run `root-set` in PostgreSQL EF conformance for the stale-fence and per-partition Seen/Trial checks.

For an abrupt peer exit in the same two-process Cluster fixture, use a fresh dedicated PostgreSQL database:

```bash
dotnet ../../essentials/tests/ActorFoundation/Native/Run/bin/Release/net10.0/Kernel.Native.Run.dll pg-cluster-crash 1024
```

The coordinator terminates only its spawned peer process while the workflow waits for its second approval.
The fixture reads the peer's live lease after process exit. A temporary diagnostic trigger on the workflow's
partition records the peer lease deadline, owner, epoch and both the successful UPDATE's statement start and its
row-trigger time. The fixture requires that UPDATE to start after the deadline. The provider samples the lease
earlier in the same transaction. The observer records committed ownership changes. The public journal bookmark
spans process loss and replay.

To observe an abrupt owner loss on a POSIX host, use a fresh dedicated database and run:

```bash
../../essentials/tests/ActorFoundation/Native/Run/run-core-preview-failover.py
```

The fixture starts two Embedded host processes for the same root. The first keeps renewing its lease at the second
wait. It drives idle Worker passes and heartbeat renewals beyond the original database lease deadline. The standby
checks the same node and epoch still have a live renewed lease. After PostgreSQL refuses its take, the fixture sends SIGKILL to its first child. The same standby
acquires a later epoch after the stored deadline, resumes the journal bookmark, replays Ada's input and records
Grace's approval. This exercises one fixed partition. K5 covers Cluster placement and balancing across partitions.

For a Durable Flowchart handover, set `KERNEL_PREVIEW_POSTGRES` to a fresh database. Both runtime nodes and the public reader use the scoped continuation format (schema 3, message protocol 3, journal codec 1). Run:

```sh
dotnet ../../essentials/tests/ActorFoundation/Native/Run/bin/Release/net10.0/Kernel.Native.Run.dll pg-cluster-flowchart 1024
```

The two RuntimeNodes establish an equal split. A public purchase Flowchart starts on the peer. A fixture callback
pauses that process after its creator's commit, and the parent checks that the checkpoint and pending continuation
share a PostgreSQL transaction ID. The parent locks that partition row, rechecks the peer epoch and pending input,
and holds the lock until its child process has exited. The surviving RuntimeNode then takes
the partition at a higher epoch, applies that exact continuation once, and runs both approval branches to one join.
A changed-body approval replay preserves the original result. Two actor revisions are silent. The partition journal
keeps consecutive positions. The check also runs with eight virtual partitions.

For a Balanced StateMachine handover, point `KERNEL_PREVIEW_POSTGRES` at another fresh database and run:

```sh
dotnet ../../essentials/tests/ActorFoundation/Native/Run/bin/Release/net10.0/Kernel.Native.Run.dll pg-cluster-statechart 1024
```

A Native order chart starts on the peer. A declined payment commits the shipment-release wait. The driver
admits the shipment acknowledgement while the peer is paused after that commit, then stops the peer process
while holding the target fence row. The original shipment acknowledgement and the waiting checkpoint remain
stored. At a higher epoch the surviving RuntimeNode applies that input once and enters the stock-release wait.
The public journal shows the evaluated, exited, transitioned, entered and handled steps at one revision.
A changed-payload replay leaves the checkpoint and journal unchanged. The check also runs with eight virtual
partitions.

For the grouped storage operations alone, run `pg-root-self-set owned <checked|in-batch>` with the same
runner on a fresh database. This uses two homes with different epochs. Two checkpoints and their private
continuations commit together. A stale sibling's fence rolls back a mixed consume, and the current set advances
only the home that writes a journal entry. With `ef` the fixture checks that the store declines grouped
ownership before changing any lease or inbox row.

### Workbench PostgreSQL provider rehearsal

Run `bash tools/ci/actor-foundation/check-workbench-postgres.sh` from the repository root. The script
builds a disposable archive of the selected revision and adds a qualification command to that
Workbench. The relational provider and Workbench import the same central package versions. The script
matches the provider's compile graph to its own lock and the emitted shared dependencies to the original
Workbench lock. It also requires the two locks to agree on the EF providers, drivers and explicit version
floors. Workbench can select its own versions of other transitive packages. Each test uses a fresh loopback
PostgreSQL database.

The archived executable runs a dedicated Generic Host before normal Workbench composition. Two
Workbench processes run the real Native approval with the hosted PostgreSQL launcher.
The first stops after Ada's approval. The next takes a higher epoch, checks that replay preserves
the checkpoint bytes and journal, then records Grace's approval once and completes the workflow. Both processes verify that stopping
the host releases the partition before disposal.
The meter reports each process's provider traffic, including connection setup, owner polling and
shutdown. Schema setup and direct diagnostic reads are outside that meter. The separate warm
public preview budget has its own command.

Orderly shutdown of an idle set host releases the owned fences together. The runtime conformance runner's
`set-release` case covers a cancelled drain, overlapping stop calls and the fixed-host path. To check the full
1,024-row release and a stale epoch on SQLite, run `dotnet run -c Release --project ../../essentials/tests/ActorFoundation/Store/EntityFramework --
sqlite release-set --owned-commit --fence-in-batch` from `prototypes/kernel-model`. The same check accepts
`--checked-fence`.

### PostgreSQL public-start round-trip budget

The public PostgreSQL writer can finish a Balanced Native root in its first fenced checkpoint. The creator,
checkpoint, terminal time and journal share one transaction. The fixture runs three workflows of ten WriteLine
activities each, checks their output and journal in authored order, and repeats each public start against the same
reference.

From this directory, set `KERNEL_PREVIEW_POSTGRES` to a fresh disposable PostgreSQL database on plaintext
loopback (`Host=127.0.0.1`, `SSL Mode=Disable`). Use a fresh database for each writer and fence combination.

```sh
dotnet run -c Release --project ../../essentials/tests/ActorFoundation/Native/Run -- pg-write-budget owned in-batch
```

Use `ef` for the EF writer, or `checked` for a separate fence statement. The measured interval starts at public
admission and ends at the fenced terminal checkpoint, on an already-bound Embedded owner with warmed connections.
The report lists bind, heartbeat and take separately. Schema setup, replay and verification reads are outside
the measured interval. The meter reports wire direction turns (round trips), PostgreSQL Sync/Query groups, EF
commands and transaction completions separately. The provider's direct admission batch appears in the wire
count. EF command counters cover commands executed by EF.

PostgreSQL 17 measured **16 round trips in-batch and 17 checked** with both writers. Each public admission cost
3 trips. Polling and the terminal commit used 13 in-batch or 14 checked. The initial qualified path took 22 and
23 respectively. The 16-trip test passes with the in-batch fence. Add `--require-budget` to make an over-budget
result fail the command. Large inputs claim their receipt before the body is formatted or sent, so a duplicate
can reuse the original reference without uploading another body.

### Flowchart restart on the public PostgreSQL preview

Set `KERNEL_PREVIEW_POSTGRES` to a fresh PostgreSQL database and run from `prototypes/kernel-model`:

```bash
../../essentials/tests/ActorFoundation/Native/Run/run-core-preview.sh pg-flowchart-preview
```

The Flowchart routes an amount of 750 to the approval path and persists a second approval wait. A new process
loads that wait. Replaying the first approval leaves the checkpoint and journal unchanged and runs zero actor
turns. The second approval runs one turn and the join fires once. The automatic branch stays inactive and the
journal records one completion revision.

This is a runnable K7 case on the public Embedded preview. The full Flowchart corpus and Cluster qualification
remain part of K7.

### Order StateMachine restart on the public PostgreSQL preview

Set `KERNEL_PREVIEW_POSTGRES` to a fresh dedicated database and run from `prototypes/kernel-model`:

```sh
../../essentials/tests/ActorFoundation/Native/Run/run-statechart-preview.sh
```

The Embedded processes use Balanced persistence and run the same Native order chart as the
[public in-memory example](models/Statecharts.Run/README.md#order-statemachine-on-the-public-actor-foundation).
The first process starts the order through `WorkflowRuntime`. A declined payment enters
`ReleasingShipment` and runs the shipment release activity. The process admits the shipment acknowledgement
through the public Foundation ingress and exits before offering it to the machine.

A second process takes the partition at a higher epoch. It reads the same persisted chart configuration and
pending acknowledgement, then drives the same order to `ReleasingStock`. The stock release activity runs once.
The public journal after the saved bookmark shows one evaluated and handled transition, including its exit
and entry. Replaying the acknowledgement's operation ID with a stock release payload leaves the order, inbox
and journal unchanged.

This is one K7 Embedded restart case with a pending direct input and Full history. The broader StateMachine
invariants, Durable persistence and Cluster qualification have separate K7 acceptance cases.

### Parallel paths in the PostgreSQL Flowchart preview

Use a fresh dedicated database in `KERNEL_PREVIEW_POSTGRES` for each run. From `prototypes/kernel-model`:

```sh
../../essentials/tests/ActorFoundation/Native/Run/run-flowchart-preview.sh manual
```

The public runtime starts a Native purchase Flowchart. A decision selects manual risk review while finance waits in
parallel. The risk review finishes, and a second Embedded host process takes the scope at a higher epoch. Replaying
the risk operation with a finance payload leaves the checkpoint and journal unchanged. Finance then completes the
last live branch. The journal shows one join, and the terminal checkpoint contains both decisions.

Run with `automatic` on a separate fresh database to select the bypass path. Finance still has to finish. The inactive risk branch cannot hold the join open.

For Durable persistence, run `../../essentials/tests/ActorFoundation/Native/Run/run-flowchart-preview.sh manual durable` (or `automatic durable`).
The first Embedded process exits with an immediate self-send in the inbox. The fixture checks that this envelope
and the initial checkpoint share a PostgreSQL transaction ID. The next process picks up the continuation and runs
to the finance wait. A third process resumes the same workflow using its saved journal cursor. The manual route
replays the risk approval with a changed body and checks the saved result. Both routes require one join, no remaining
self inputs, and an unchanged journal on final replay. `KERNEL_PREVIEW_FENCE=checked` or `in-batch` selects the
fence mode. This scoped path supports immediate sends back to the exact generated actor creation. Scheduled sends,
cross-actor sends and other authored effects remain outside this preview.

The Durable command creates a dedicated PostgreSQL database format (schema 3, message protocol 3,
journal codec 1). Fresh creation seeds that marker with the schema. Existing continuation databases
use the [coordinated job-generation upgrade](#relational-job-generation-format) and retain their private inputs.
Opening an ordinary database checks its existing marker before installing continuation tables or enabling sends.
An ordinary protocol-2 database still requires a separate migration to adopt protocol 3. An earlier reader
refuses ownership after the schema advance.

The public preview writes ordinary text directly. A payload containing NUL or an isolated UTF-16 unit uses a binary
field in the same message row. It occupies the original UTF-8 byte budget, with three bytes per isolated unit.
The adapter maps these inbox rows to a dedicated storage scope. The Worker sees its original issued key, and ordinary issued messages can use the same ID and application kind independently. To run the stored input, size and writer
checks on a fresh PostgreSQL database, use `pg-root-self owned checked` with the Native runner. The other choices
are `ef` for the writer and `in-batch` for the fence.

On a fresh database, `dotnet ../../essentials/tests/ActorFoundation/Native/Run/bin/Release/net10.0/Kernel.Native.Run.dll pg-flowchart-preview merge-early`
replaces the join with a merge. The waiting assertion detects the premature finish.

### PostgreSQL Delay preview

On a fresh dedicated PostgreSQL database set `KERNEL_PREVIEW_POSTGRES` and run:

```sh
../../essentials/tests/ActorFoundation/Native/Run/run-delay-preview.sh owned checked
```

The Delay profile seeds schema 3, message protocol 4 and journal codec 1. An existing schema-2 Delay store
retains its deadlines through the [coordinated job-generation upgrade](#relational-job-generation-format).
The immediate-continuation Flowchart stays on protocol 3 and the ordinary preview uses protocol 2.
Pass `durable` as a third script argument to run the same Delay in Durable mode. The default is Balanced.

The first Embedded process starts a public Native sequence with an eight-second Delay. It checks that the saved wait
and its issued timer share a PostgreSQL transaction and that the deadline equals the issuing turn time plus eight
seconds. A second process reads the original handle and absolute deadline
at a higher ownership epoch. A future-horizon poll sees the timer early. A ready poll excludes it, and an ordinary
application input of the same kind leaves the frame waiting. At the deadline the worker resumes the frame once.
A process restarted after the deadline observes the timer ready immediately and completes the same frame.
The receipt prints the deadline, the worker's application time, the database's terminal checkpoint time and
whether the early-input check ran. The
fixture checks the public journal after another worker pass. Use `ef` for the EF writer or `in-batch` for the batch
fence. This preview exercises a single generated root, one partition and a clean process restart.

Run `../../essentials/tests/ActorFoundation/Native/Run/run-delay-preview.sh owned checked recall` on another fresh database to let a public approval
win a Native Parallel join before the sibling Delay fires. Its checkpoint recalls the persisted timer. A cold process
keeps the same actor, delivers an independently admitted notice at the original deadline and finishes on a second
approval. The journal records the cancelled frame once. This Embedded preview supports recall of already persisted
self inputs on a generated root. Same-turn retraction, whole-workflow operator termination, Park and Cluster timer
recovery fall outside this case.

### PostgreSQL Native background job preview

Set `KERNEL_PREVIEW_POSTGRES` to a fresh dedicated PostgreSQL database and run from `prototypes/kernel-model`:

```sh
../../essentials/tests/ActorFoundation/Native/Run/run-root-job-preview.sh checked
```

Use `in-batch` on another fresh database to exercise the other owned SQL fence. The first Embedded process starts
two public Native workflows. Each start commits the waiting checkpoint, issued job ticket and journal together.
A replacement process recovers each job at a higher epoch. Each callback receives and returns its own opaque
public actor reference. The Worker commits the first result, consumes its exact ticket and issues the next
generation's ticket in the same transaction. A third process reloads and completes both successor jobs.
For each job root, the public journal shows one success for each background activity and one end marker.
A job-free Native approval also reloads across both ownership changes and keeps its job generation at zero.

The profile uses schema 3, message protocol 6 and journal codec 1. Owners initialize the job capability before
binding a scope. The fixture checks that a missing initialization cannot consume a pending creator. Readers of the
ordinary protocol refuse ownership. The fixture checks refusal of cross-root, stale and unproven completions, including a physical
argument change between preparation and the locked read. Only the qualified Worker can write these checkpoints.
This opt-in uses Embedded, Balanced persistence and one active untimed job per generated root.
Timed and held results, job cancellation, inline promotion and job fault recovery require separate qualification.

#### Timed Native job issuance qualification

On a disposable PostgreSQL server with permission to create a database, set `KERNEL_PREVIEW_POSTGRES` and run
the Native runner with `pg-root-timed-job-issuance checked` or `pg-root-timed-job-issuance in-batch`.
The fixture creates and drops its own database. It uses an isolated schema-3, protocol-7, codec-1
marker so a protocol-6 owner refuses it. Existing database profiles have no upgrade into the fixture.

A Native background node declares a timeout. The first process exits after the fenced commit, before
the Worker sees its answer or hands off locally. A second process takes the same partition at a higher epoch and inspects the
same Native wait, generation, private ticket and deadline. It checks the original absolute deadline against
the database clock and verifies that both rows, the checkpoint and the journal share a transaction.
Changing the persisted deadline by one millisecond makes the original ticket pair fail validation.
The cold owner's store poll excludes both rows after the deadline. Production settlement, result delivery and
recovery still need qualification.

#### Timed Native job settlement qualification

Set `KERNEL_PREVIEW_POSTGRES` to a fresh dedicated database for each run. Use
`pg-root-timed-job-settlement checked complete` with the Native runner. The other fence is `in-batch`, and
`timeout` runs the deadline-first case. `late-clock` delays PostgreSQL responses and releases the current
owner's callback after the database deadline while its local estimate still reads earlier.
This fixed-root fixture uses schema 3, message protocol 9 and journal
codec 1. Settlement checkpoints use a dedicated state encoding. Protocol-2 ordinary and protocol-6 job
point readers refuse that encoding. A protocol-7 client refuses ownership and point reads at the format check.

The first owner commits a real Native job and deadline. A fresh owner then settles the pair under its own
fence. In the completion case the callback publishes before the deadline and the owner settles after it.
In the timeout case the new owner takes after the deadline and the old callback returns late. Protocol 9 adds
one PostgreSQL clock query for each callback result offered for publication. The fixture counts that query
and uses the sampled time to qualify completion even when the commit runs later. An unavailable sample leaves
the deadline eligible to settle the job. A stale fence
and a current fence offered an early timeout leave the pair pending.

Settlement replaces both rows with one recorded result and advances the checkpoint without changing the
saved Native wait. A third owner applies that stored result through Native and records one terminal outcome.
Use `unhandled-timeout` to run a workflow with no timeout handler. The third owner consumes the exact recorded
timeout and persists an authored `Faulted` outcome with workflow substatus `job`, an end marker and no Park.
The accepted receipt remains a duplicate. A fourth owner waits for the delayed input admitted before the
fault and settles that input under its fence, without a new workflow turn or journal entry.
Run unhandled timeouts on a fresh fixture database with every settlement owner and ingress using the
same updated build.
The fixture counts durable outcomes. The completion case deliberately executes the callback again after takeover.

### PostgreSQL Suspend and Resume preview

On a fresh dedicated PostgreSQL database, set `KERNEL_PREVIEW_POSTGRES` and run `../../essentials/tests/ActorFoundation/Native/Run/run-root-operator-preview.sh [owned|ef] [checked|in-batch]`.

The first process starts Native workflows and admits one-way Suspend and Resume through an explicitly authorized
host. Receipts are scoped to the tenant and operation, including concurrent retries with changed targets. A second
process applies Suspend with the current ownership fence. A stale owner leaves the exact command pending. On a
running root the worker clears earlier ready ordinary input before the command. A suspended root withholds ordinary
input and offers its first due command, allowing Resume to pass the waiting approvals. Independent roots can advance.
Due controls use the database clock, and the poll applies these rules before the page limit.

A third process loads the suspended Native header at a higher epoch and admits another approval while suspended.
Resume settles its occurrence and reopens delivery in the same PostgreSQL transaction. The pending approvals then
finish the original Native frame in order, once each. A command queued behind an input that finishes the root settles
without changing the terminal checkpoint. The preview host grants no operator permissions by default.

This opt-in profile retains message protocol 5 and can apply commands admitted by the previous build. Restart all
owners on the new build to enable dispatch. A protocol-5 admission-only owner still withholds ordinary delivery on
a suspended Native root, but cannot apply its pending commands. Earlier profiles refuse a protocol-5 store at startup.

## Built-in Cluster redistribution on PostgreSQL

Set `KERNEL_PREVIEW_POSTGRES` to the connection string of a fresh, empty PostgreSQL database, then run from
`prototypes/kernel-model`:

```bash
dotnet restore ../../essentials/tests/ActorFoundation/Native/Run --locked-mode
dotnet run -c Release --no-restore --project ../../essentials/tests/ActorFoundation/Native/Run -- pg-runtime-cluster 1024
```

Omit the partition count for the eight-partition smoke. Append `wake` to use the optional PostgreSQL wake transport. The two processes listen on separate
connections. After each of the first two admissions commits, the first node queues the target
partition number for PostgreSQL `NOTIFY`. The receiving node checks both transport callbacks and confirms
that its active partition forwarded both hints to the durable Worker. After the second
node leaves, the first node applies the final approval through the inbox with no hint. Run it
against another empty database to compare with the polling-only run.

Wake hints carry a partition number. They never carry an actor name, tenant or command payload.
The sender uses a bounded queue and a separate connection. A full queue or connection failure
can drop a hint. The node still polls on its ordinary interval.

Two operating-system processes run the built-in `RuntimeNode` and a durable Worker per node. At the release count,
PostgreSQL shows all 1,024 partitions on the first node, 512 each after the join, and all 1,024 back on the first
after the second stops gracefully. The join checks one set give-up through the host and the transferred rows'
common PostgreSQL take transaction. A Native workflow waits twice for approval events. The departing node records Ada, and
its successor records Grace at the next journal position.

This fixture uses the general PostgreSQL durable workflow path and the Native compatibility adapter. Public
Foundation use across an owned set and the remaining release-profile acceptance cases stay in K5.

## The simulation

```
SIM="dotnet sim/bin/Release/net10.0/Kernel.Sim.dll"
$SIM --seeds 200000                                  # 200,000 seeds of 300 actions each
$SIM --seeds 20000 --actions 1000                    # 20,000 seeds of 1,000 actions
$SIM --seeds 200000 --count 4096                     # K1: the store's virtual partitions, 1,024 (the default) or 4,096
$SIM --native --seeds 200000                         # the same with native instances in the mix (40 % of outside actions)
$SIM --native --seeds 20000 --actions 1000
$SIM --seed 22 --flaw TrialPerPartition              # replay one seed; prints a shrunk trace when it fails
$SIM --contract --seeds 100 --actions 300              # K2 public-contract delivery workload
$SIM --contract --seed 1 --flaw WallClockTimers        # replay the early timer negative control
$SIM --contract --seed 1 --flaw KeepConsumedMessages  # replay the committed inbox negative control
$SIM --contract-native --seeds 100                   # run the original Native mix through the public actor turn
$SIM --contract-native --seed 1 --native-flaw UnstableEffectKey
$SIM --contract-native --seed 1 --native-flaw JoinWithoutGuard
$SIM --contract-actors --seeds 100 --actions 100      # public counter and parent-child workloads
$SIM --contract-evolution --seeds 100                 # rolling state upgrade and atomic start followed by send
$SIM --contract-evolution --seed 1 --flaw SkipUpgrade
$SIM --contract-actors --seed 1 --flaw CancelKnownChildren
$SIM --contract-actors --seed 1 --flaw KeepConsumedMessages
$SIM --contract-events --seeds 100                   # sequences, waiters, correlation actors and event starts
$SIM --contract-events --seed 1 --flaw CorrelationForgets
$SIM --contract-events --seed 1 --flaw KeepConsumedMessages
$SIM --contract-http --seeds 100                      # immediate, early, timer, job, and resumed requests
$SIM --contract-http --seed 1 --flaw EmitBeforeCommit
$SIM --inline-ephemeral-check                        # public write-free completion, short waits and atomic promotion
$SIM --inline-ephemeral-check --flaw ForgetShortWaits
$SIM --contract-jobs --seeds 100                     # public Runner, Fragile and nested Tree workloads
$SIM --contract-jobs --seed 1 --flaw VolatileJobTicket
$SIM --contract-jobs --seed 1 --flaw JobPoisonAsError
$SIM --contract-jobs --seed 1 --tree-stall           # an uncooperative job keeps the trial gate held
$SIM --job-trial-check                               # counted withdrawal, cancellation and lost-ack boundaries
$SIM --lease-check                                   # deterministic late-ack, scheduler and takeover witness (LEASE.md)
$SIM --public-owner-check                            # FC-1 per-owner serialization and fenced takeover
$SIM --public-owner-check --flaw IgnoreLeaseDeadline,CommitDespiteFence
$SIM --placement-check                               # round 5 A: virtual-partition addressing and range placement (../../essentials/src/ActorFoundation/BuiltIn/Runtime/VirtualPartitions.cs)
$SIM --mutations --seeds 20000                       # break the kernel on purpose, one flaw at a time
$SIM --native-mutations --seeds 20000                # break the native model on purpose
$SIM --native --native-flaw ResumeAnyChild --seed 1  # replay one native flaw
$SIM --seeds 4000 --coverage                         # also count what happened
$SIM --seeds 5000 --actions 1000 --faults 2          # scale the faults
$SIM --backlog 1000                                  # how long a backlog of suspects takes to recover
```

The K2 contract workload drives `IIngress` and checks `IActorReads`. Each seed boots two caller keys in
two tenants. It interleaves exact and keyed sends, retries, future inputs, and live event fan-out.
The driver expires an owner and takes its scopes on a second node. It loses one turn commit before
it executes and one answer after the batch lands. The oracle reads the paged journal and checks
once-only effects, order of ordinary inputs, due time and tenant isolation. It also checks the
committed receipt count and the empty inbox. An adapter can run the same schedule and oracle by
providing its ingress, reads, and deterministic clock and fault controls.

The Native workload uses the original nine `NativeWorkflows.Mix` definitions. It starts them through
`WorkflowRuntime` and drives the same clock and owner controls. A pending Native timer crosses takeover,
the Balanced outbound workflow replays a lost write, and the built-in publisher advances a live Flowchart
after its commit answer is lost. The seeded schedule varies signals, due times, takeovers and the execution
mode of the other workflows. Public journal and read observations check the background job, attached and
detached children, threshold joins, graph arrivals and completion. The observed HTTP and email effects
check that a replay keeps its key and that different activations keep distinct keys.

The public actor workload uses the legacy Counter and parent-child inputs through the public workflow
turn. The counter's journal checks each sender's sequence through holds, forwards, a definition rebind,
retries, and loss before and after a write. Two live owners run the family workloads. A lost creation
answer leaves one child per start, a pending timer crosses takeover, and each parent receives one final
notice from the exact child. In the cancellation case the other owner takes its child scopes after the
parent sends the cancel.

The sequence and event workload runs Balanced and Durable sequences, live and keeping waiters,
actor-managed correlation and event-created actors through the public turn. It checks each sequence's
journal order and commit boundaries across a lost write and a lost answer. It also checks retained
events versus live delivery, event expiry, duplicate publication and registration, and route removal.
The sequence cases use durable ingress. The legacy suite covers unwritten Ephemeral starts.
Actor-managed start-on-match stays outside this replay.

`--paged-publication-check` exercises typed outside sends on the in-memory store and Worker. It checks folded
receipts, a 171-wait broadcast, the byte cut, lost outcomes, takeover accounting, rejection of an old page under
a new reservation, and the dead-letter receipt. The fault driver activates the actual publisher's partition.

`--cascade-pages-check` runs the live in-memory termination pages, including the 1,000-entry boundary,
loss before and after a commit, a retirement marker continuing the original discharge, keyed reuse, and a
Native Parallel with 1,001 parent-bound children. It also checks the application send cap independently.

`--retirement-cascade-check` runs qualified Delete on an ended child, an operator terminate that leaves its
obligation held, and the recorded parent's terminate which starts the cascade. It checks one-page and
two-page cascades across takeover, an exact registry hand-off, and root retirement. Park drains, ThenDelete,
and physical-provider qualification have separate cases.

The focused [held request cases](sim/ContractHeldRequestChecks.cs) in `--lease-check` cover a live
response after takeover and an input that stays durable across local reset. The seeded actor workload
uses already-started counter receivers and one state version. The other legacy actor and transport
mixes continue to use their separate simulation and case suites.

The Counter evolution workload moves a version-one checkpoint through upgrade, a rollback to an older
owner and another compatible takeover. It portably expresses legacy start-on-forward as a detached
`Start` and a `Send` staged in the same turn, keeping the receiver ref in the sender's checkpoint. An
aborted creator's provisional ref never routes. Once the creator commits, the exact ref receives later
forwards and survives a lost commit answer.

A seed fixes everything: the number of nodes, every action and every fault. `--flaw` takes the names of
`Kernel.Runtime.Flaw` (comma-separated); each one switches off one rule of the protocol. FINDINGS section 22 lists the first failing seed of every round-3 flaw (the seeds of earlier flaws, in sections 2 and 3, have since moved), section 24 the two flaws round 4 renamed.

The public job and tree workload uses the Runner, Fragile and Tree inputs. It observes physical job
callbacks through the model contract, and Park, pending attempts and applied effects through public
reads. Runner cancels one job, then loses the owner of another and completes the old attempt on either
side of its successor. Fragile throws or has its row refused by the store, then exhausts the turn and
job allowances in separate schedules with actual expired leases. The tree loses the answer to its
root's terminate with descendant commands still pending. Cooperative leaf jobs let successor owners
clear the trials and finish the cascade. Public events verify the leaves' live waits and their
removal. The proposed paged cascade and Park recovery transitions belong to K3.

`--tree-stall` leaves physical leaf callbacks pending after signalling cancellation and checks that
their trial still gates the root. `--job-trial-check` exercises the separate counted, fenced trial
clear before and after a lost answer, late callback exit, command admission and Hold, an already
published result, and real takes across the two store outcomes. The direct checks use a mixed
actor-type router. A cold checkpoint defers its withholding callback while the trial runs, and
a pending child creator or ordinary predecessor keeps its place ahead of a terminate. A timed
job's own deadline settles without releasing physical isolation before its driver exits.
The in-memory store supports the clear. Provider implementation and qualification belong to K4.

## Databases

Let Docker pick the ports and read them back; assume no port is free.

```
docker run -d --rm -P -e POSTGRES_PASSWORD=kernel --name km-pg postgres:17
docker run -d --rm -P --platform linux/amd64 -e ACCEPT_EULA=Y -e 'MSSQL_SA_PASSWORD=Kernel_Model1' --name km-mssql mcr.microsoft.com/mssql/server:2022-latest
docker run -d --rm -P -e MYSQL_ROOT_PASSWORD=kernel --name km-mysql mysql:8.4
docker run -d --rm -P --name km-mongo mongo:8.0 --replSet rs0 --bind_ip_all
docker exec km-mongo mongosh --quiet --eval 'rs.initiate({_id:"rs0",members:[{_id:0,host:"localhost:27017"}]})'
docker port km-pg 5432; docker port km-mssql 1433; docker port km-mysql 3306; docker port km-mongo 27017

PG="Host=localhost;Port=<pg>;Username=postgres;Password=kernel"
MS="Server=localhost,<mssql>;User Id=sa;Password=Kernel_Model1;TrustServerCertificate=True"
MY="server=localhost;port=<mysql>"                          # EngineRun adds the users; the suite wants uid=root;pwd=kernel
MO="mongodb://localhost:<mongo>/?directConnection=true"
```

SQL Server runs under amd64 emulation on arm64: slow, but the counts are not affected. Stop every container afterwards
(`docker stop km-pg km-mssql km-mysql km-mongo`).

## The runtime conformance suite

On the in-memory store and the built-in runtime. (The Orleans adapter ran here too until its retirement on 19 November;
FINDINGS section 27 and the K1 costs keep its results.) Each command's exit code says whether the runtime conforms; the `-broken` forms break
the store's fence on purpose and must fail on it.

```
RTC="dotnet conformance/Kernel.Runtime.Conformance.Run/bin/Release/net10.0/Kernel.Runtime.Conformance.Run.dll"
$RTC semantic [filter]; $RTC semantic-broken
$RTC stress 20 2000; $RTC stress-broken 10 1000
```

## The store conformance suite

```
dotnet conformance/Kernel.Store.Conformance.Run/bin/Release/net10.0/Kernel.Store.Conformance.Run.dll semantic     # in memory
EFC="dotnet ../../essentials/tests/ActorFoundation/Store/EntityFramework/bin/Release/net10.0/Kernel.Ef.Conformance.dll"
$EFC sqlite [--fence-in-batch|--checked-fence] [--ef-commit|--owned-commit] semantic   # no flag: the engine's default fence, below; the owned commit (COMMIT-SQL.md)
$EFC postgres "$PG" [--fence-in-batch|--checked-fence] [--m5] semantic # --m5: E1 switched off; it changes nothing on the owned commit (COMMIT-SQL.md), and failed the fence cases on EF's
$EFC <engine> ... --checked-fence semantic-broken fence                # the fence broken on purpose: the three fence cases must fail (exit 0)
$EFC <engine> ... --checked-fence --fence-ignores-count semantic fence          # the owned fence's count ignored: the same three must fail (exit 1; semantic-broken would break the fence itself)
$EFC sqlserver "$MS" [--fence-in-batch|--checked-fence] [--rcsi-off] semantic
$EFC mysql "$MY;uid=root;pwd=kernel" [--fence-in-batch|--checked-fence] [--rc] semantic
MOR="dotnet mongo/Kernel.Mongo.Run/bin/Release/net10.0/Kernel.Mongo.Run.dll"
$MOR "$MO" [--fence-in-batch] semantic
$MOR "$MO" semantic-broken                                             # the fence broken on purpose: its cases must fail
$MOR "$MO" stress 40 5000
$MOR "$MO" stress-broken 15 1500                                       # must fail
$MOR "$MO" [--fence-in-batch] cost
$MOR "$MO" probe crash|limits
```

### Relational store format checks

Place the format command before the fence option. From `prototypes/kernel-model`, for example:

```sh
dotnet run -c Release --project ../../essentials/tests/ActorFoundation/Store/EntityFramework -- sqlite version-services --checked-fence
dotnet run -c Release --project ../../essentials/tests/ActorFoundation/Store/EntityFramework -- postgres "$PG" version-services --checked-fence
```

Repeat with `--fence-in-batch`. Use a disposable server for network providers. SQLite uses a temporary file.
The other format commands are `node-formats`, `version-take`, `format-migration` and `job-generation-format`.
`version-take` and `version-services` exercise a higher and lower value for each of schema, message protocol and
journal codec. An unsupported marker leaves ownership, the journal, projection and retained rows untouched.
Restoring the supported marker lets ownership take and the store's service operations proceed.

### MySQL node-lease cancellation

From `prototypes/kernel-model`, use an isolated TLS-enabled MySQL test server with an administrative fixture
connection. Build the EF conformance runner with a locked restore, then run:

```sh
dotnet run -c Release --project ../../essentials/tests/ActorFoundation/Store/EntityFramework -- mysql "$MY" lease-cancellation --owned-commit --checked-fence
```

Repeat with `--fence-in-batch`. This check injects a throwing cancellation callback at heartbeat connection
opening and during a take's renewal. The kernel keeps the one-third-lease deadline, classifies the elapsed
operations as `Unknown` and `Transient`, and verifies that neither published a renewal. Caller cancellation
preserves the caller token at take, retake and both heartbeat connections, including a callback registered
after cancellation. Connector/NET's own connection timeout remains a separate [K4 qualification case](../../docs/program-goals/clean-slate-kernel.md#k4-relational-qualification).

## Stress and cost on the EF engines

```
EFR="dotnet ef/Kernel.Ef.Run/bin/Release/net10.0/Kernel.Ef.Run.dll"
mkdir -p .out                                                             # the SQLite files live here
$EFR x stress postgres "$PG;Database=kernel_stress" seconds=40 messages=5000 [fence=checked|batch]
$EFR x stress sqlserver "$MS;Database=kernel_stress" seconds=40 messages=5000 [fence=checked|batch]   # RCSI on unless rcsi=0
$EFR x stress mysql "$MY;database=kernel_stress" seconds=40 messages=5000 [fence=checked|batch] [isolation=rc]
$EFR x stress sqlite "Data Source=.out/stress.db" seconds=40 messages=5000 [fence=checked|batch] [wal=0]
$EFR x stress <engine> "<same>" stalelease=1 [fence=checked|batch]                 # the Worker's lease check off: stale owners reach the fence, and the fence alone keeps the end state (must hold)
$EFR x stress <engine> "<same>" stalelease=1 unfenced=1 [fence=checked|batch]      # the fence broken on purpose: must fail on every engine (FINDINGS 29 F-B8); without stalelease=1 it holds on PostgreSQL and MySQL (F-B2)
$EFR x stress <engine> "<same>" stalelease=1 fencecount=0 [fence=checked|batch]    # the owned fence's count ignored: must fail on every engine (F-B8)
$EFR x stress <engine> "<same>" [commit=ef|owned]                                    # the commit writer (default: owned, COMMIT-SQL.md)
$EFR x cost <engine> "<connection;Database=kernel_cost>" proxy=1 [fence=checked|batch] [commit=ef|owned]   # round trips on the TCP stream
$EFR x reference-cost <engine> "<connection;Database=kernel_cost>" disposable=1      # K4 reference budget with the default owned writer
$EFR x probe|block|definite|deadlock <engine> "<connection>"                         # ENGINES.md
$EFR x fence <engine> "<connection;Database=kernel_fence>"                            # the k-row fence over eight partitions, one stale: COMMIT-SQL.md
$EFR x setcost <engine> "<connection;Database=kernel_setcost>" proxy=1 [writes=1] [runs=5] [shape=both|scattered|contiguous] [count=4096]   # K1: ownership of a set in the integrated Worker, PARTITIONS.md "K1 costs"
$EFR x setidle postgres "<connection;Database=kernel_idle>" seconds=120 writes=1      # K1, D3: four RuntimeNodes idle, the beat's WAL
$EFR x stress|cost postgres "<connection>" writes=1 [history=state|minimal] [pad=4096] [fillfactor=70] [fastvacuum=1]   # WRITES.md; the container needs -c wal_keep_size=4GB
```

The K4 reference command resets its disposable database. For network engines, give the wire meter a
loopback host (`127.0.0.1` or `localhost`) and an explicit TCP port without alternate routing. Use an isolated disposable MySQL server because
the fixture provisions a server-wide worker account. The command runs ten Balanced no-ops on the owning worker,
checks that they persisted in order in one finished revision and one journal row, and enforces the tuned
budgets of two PostgreSQL and three SQL Server wire round trips or three SQLite engine calls. It records
the MySQL wire count. Database setup and evidence reads sit outside the measured interval.

Without a fence option the EF store takes the engine's default: the fence in the batch on every engine (SQL Server since
16 November, for the owned commit; ENGINES.md E9; `fence=checked` is the 13 November fallback and stays the default with
`commit=ef`, whose batch still deadlocks takes). Without a commit option it writes the commit with its own SQL on every
engine (round 5 B, COMMIT-SQL.md); `commit=ef` is EF's SaveChanges, the reference. `EngineRun.cs` lists every option. The stress test drives round 1's models plus round 3's commands (suspend, resume,
terminate, delete) and starts on events. Do not run the conformance suite on an engine while a stress test runs there:
the stress test kills the suite's sessions too.

## Native

```
NAT="dotnet ../../essentials/tests/ActorFoundation/Native/Run/bin/Release/net10.0/Kernel.Native.Run.dll"
$NAT smoke
$NAT park                    # Park removes active subscriptions; suspension preserves them
$NAT cost sqlite
$NAT cost postgres "$PG;Database=kernel_native"
$NAT memory 3000
```

## The gates of a refactor

What round 4 stage A ran before and after (FINDINGS section 24 has the numbers); a change that claims to change no
behaviour must give the same output, times and renamed words aside:

```
$SIM --seeds 200000
$SIM --seeds 20000 --actions 1000
$SIM --native --seeds 20000 --actions 1000
$SIM --mutations --seeds 20000
$SIM --native-mutations --seeds 20000
dotnet conformance/Kernel.Store.Conformance.Run/bin/Release/net10.0/Kernel.Store.Conformance.Run.dll semantic
$EFC sqlite --checked-fence semantic; $EFC sqlite --fence-in-batch semantic
$NAT smoke; $NAT cost sqlite
dotnet test models/Graph.Tests -c Release
mkdir -p .out; $EFR x cost sqlite "Data Source=.out/cost.db" fence=checked                # and fence=batch; delete .out/cost.db* between runs
$EFR x cost postgres "$PG;Database=kernel_cost" proxy=1 fence=checked                     # and fence=batch; and commit=ef for the reference path
$EFC postgres "$PG" --checked-fence semantic; $EFC postgres "$PG" --fence-in-batch semantic
$EFR x fence postgres "$PG;Database=kernel_fence"
$NAT cost postgres "$PG;Database=kernel_native"
```

The `tcp` column of the PostgreSQL cost run varies by a read or two between runs for the 1,000-instance rows; the
counted and sent round trips do not.

## How the simulation works

- Several nodes run against one in-memory store, which has a fence row and a journal for each of `--count` virtual
  partitions (1,024 by default, or 4,096). A world places four of them on its two or three nodes (K1 slice 7). Each
  node's process is an *actor* with one worker that owns a set of those partitions, through `DurableHost.ForNode`, so a
  flush spans the partitions it touches under the k-row fence. Every seventh seed runs a worker per partition instead,
  as on a store without owned sets. The simulation is the actor runtime (layer 1): each process has a
  `PartitionRuntime` over the node's host, and takes, give-ups and balancing go through them, one at a time; the
  pieces of a pass (poll, load, turn, flush, meet) are separate actions, so that one seed keeps its schedule. Once
  faults stop, the settling loop runs whole passes (`PassAsync`).
- One action happens at a time, chosen from the seed: a runtime activates a partition (a take) or gives it up; a
  worker polls, loads, runs one turn, flushes, beats or reads events; a node balances; a store call executes; its
  answer arrives, arrives as "connection lost" although it executed, or the call never executes; an outsider adds a
  message, retries an old one, hands a start to an owner in memory, publishes an event, registers a start
  subscription, suspends, resumes, terminates, rebinds or deletes an instance; a request starts or resumes an HTTP workflow, or its caller goes away; the transport between nodes
  delivers, repeats or drops a reply; a background job completes; time passes; a wall clock jumps; a node crashes and
  restarts, with or without its old process living on, sometimes at another model version; a node pauses or resumes;
  a projector runs a round; the sweep runs. With `--native`, native workflows are started, resumed by events or
  directly, and cancelled.
- A store call is on a wire: the request executes at one moment and the answer arrives at another. A worker's
  continuation runs inline when the simulation delivers the answer, so there are no threads.
- After every action the invariants are checked against the rows, the owners' memory and an oracle of what was sent.
- Then faults stop, the system runs until idle, and the end state is checked.
- A failing run is shrunk: actions are removed while the same invariant still breaks, and the remaining trace is printed.

All monotonic clocks tick at the database's rate; wall clocks are skewed and jump. Bounded drift of the monotonic
clock is not modelled. The simulation and the stress tests use their own short leases; the kernel's default is 10 s.


### PostgreSQL continuation payloads on older prototypes

A physical event slot can share a number with a private continuation. The event match keeps their identities
distinct. On a reopened continuation or timer database, enabling the writer verifies the active private-body
trigger. Replacing a predecessor trigger is refused if it left a split matched payload. Generic delivery refuses
the affected row even when the current trigger is active. Restore the exact envelope from its authoritative
event source before resuming delivery.

### Relational job-generation format

During the coordinated stop, call `EfStore.UpgradeJobGenerationFormatAsync` before reopening a schema-2
database. The ordinary relational format advances from (2, 1, 1) to (3, 2, 1). The PostgreSQL immediate-continuation
format advances from (2, 3, 1) to (3, 3, 1). The PostgreSQL Delay format advances from (2, 4, 1) to (3, 4, 1).
Both keep their existing private inputs and refresh the private inbox view in the schema transaction.
For PostgreSQL generated roots, the migration installs and backfills a missing actor-list projection before
publishing the new format. Run the same coordinated-stop command on an already advanced preview database
to install a projection left out by an older coordinator. Ordinary relational stores do not get a root projection.
The three numbers identify schema, message protocol and journal codec.
Checkpoints that predate the creation's next-job counter read as null. Existing messages read as generation zero.
MySQL prepares the two additive columns on a separate connection while the migration holds the version marker.
An interrupted preparation can be retried. Reopening requires a reader for the resulting three-part format.

To check a real PostgreSQL predecessor, build its Native runner and this checkout's Native and EF conformance
runners in Release. Run `bash ../../essentials/tests/ActorFoundation/Store/EntityFramework/check-generation-profiles.sh /absolute/path/to/old/Kernel.Native.Run.dll`
from this prototype. The script uses a disposable PostgreSQL 17 container and three fresh databases. It starts each
workflow with the old executable and completes the same wait or pending private input after the schema upgrade.

Run `job-generation-format` with the EF conformance executable to exercise the
migration and cold field reads. The legacy writer still refuses qualified Foundation
job turns. K4's Worker, Park, current-generation result and job-entry cases require
the qualified relational writer.

### PostgreSQL different-version rehearsal

From `prototypes/kernel-model`, run this with Docker and the locked .NET SDK.

```sh
bash ../../essentials/tests/ActorFoundation/Store/EntityFramework/check-mixed-version-rehearsal.sh
```

The script builds its process driver against archived runtime sources at schema-2 revision `08caf14c0e`, schema-3
revision `6f38d98648`, and the current commit. It runs them in separate processes against a disposable PostgreSQL 17
database. The old owner writes and keeps its partition. Migration refuses while that owner is live, with the
original rows and schema preserved. After a coordinated stop, the new owner writes with the advanced marker.
The old owner at startup, an independent direct take, the sweeper and the projector each refuse with the
stored and readable versions. Every refusal leaves the rows unchanged. The new owner commits again at the
same epoch.

After the new owner stops, `6f38d98648` takes over, reads the upgraded checkpoint and commits the next turn
with the journal prefix preserved. Its sweeper and projector also process the upgraded data.
That is the tested PostgreSQL prototype rollback floor for the ordinary relational format
(schema 3, message protocol 2, journal codec 1) in
this schedule. Release notes still need their floor for the actual shipped build and store formats.
The script prints the receipt directory, source revisions and provider assembly hashes.
