# Kernel model

A standalone model of the execution kernel ([DESIGN.md](DESIGN.md): the design as built, against draft 5 of the
sketch, in three layers), the stores it runs on, the native Elsa workflow model on it, and a simulation that tries to
break it all. It tests the design before product code exists. It shares nothing with the Elsa runtime and builds as its own
repository: the folder has its own `Directory.Build.*`, `Directory.Packages.props`, `NuGet.config`, `global.json` and
`.editorconfig`.

What was found is in [FINDINGS.md](FINDINGS.md) (section 27: the runtime conformance suite and the Orleans adapter;
section 26: the graph engine; section 25: round 4's decided items; section 24: the three layers and the round 4 names;
section 23: the merge of the four branches and its gates); what each database engine does to the EF store is in
[ENGINES.md](ENGINES.md), the store's own commit SQL (round 5 B) in [COMMIT-SQL.md](COMMIT-SQL.md), MongoDB in
[MONGO.md](MONGO.md), native workflows as an execution model in [NATIVE.md](NATIVE.md), virtual partitions and the fence
granularity in [PARTITIONS.md](PARTITIONS.md), and write volume and bloat on PostgreSQL in [WRITES.md](WRITES.md).

## Layout

| Path | What |
|---|---|
| `src/Kernel.Runtime` | Layer 1, the actor runtime: the seam (`IActorRuntime`, `IScopeHost`, `IScopeActivation`), the wake signal, and `PartitionRuntime`, the built-in runtime (balancer, activations, one pass at a time per partition); addresses, flaws, the owner's defaults |
| `src/Kernel.Durable` | Layer 2, durable actors: `Worker` (a partition's activation: inbox, fenced commit, timers, background jobs, events, poison, journal), `DurableHost`, the seam to layer 3 (`IActorType`, `DurableTurn`), the store contract, the in-memory store, the projector, reply addresses for live output |
| `src/Kernel.Workflows` | Layer 3, actor types: `WorkflowInstance` (status, definition, parent and children, suspend, resume, terminate, rebind, delete, the history envelope) and the execution-model contract (`IExecutionModel`, `ITurn`) |
| `sim` | Toy execution models, the deterministic simulation, the invariant checks, the shrinker and the mutation check; `NativeSim.cs` puts native instances in the mix |
| `models/Graph` | The graph engine: main's Flowchart engine (paths, arrivals, dead paths, gateways) as a library with no kernel or Elsa reference; README says what was adapted |
| `models/Graph.Tests` | Main's Flowchart conformance corpus and join tests against the graph engine |
| `models/Native` | The native Elsa workflow model: authoring surface, activities (its Flowchart on the graph engine), interpreter, HTTP routes and host on the kernel's live output |
| `models/Native.Run` | Native smoke tests, cost and memory measurement, PostgreSQL Core Preview |
| `ef/Kernel.EntityFramework` | The storage interface on Entity Framework Core: PostgreSQL and SQLite in `EfStore.cs`, SQL Server and MySQL dialects in `Engines.cs`; the commit as the store's own SQL in `EfStore.Owned.cs` and the `*CommitDialect.cs` files ([COMMIT-SQL.md](COMMIT-SQL.md)) |
| `ef/Kernel.Ef.Run` | The cost measurement and the concurrent stress test; `EngineRun.cs` runs them on PostgreSQL, SQL Server, MySQL and SQLite (`x ...`) |
| `ef/Kernel.Ef.Conformance` | The conformance suite against the EF store, every engine |
| `conformance/Kernel.Store.Conformance` | The store conformance suite: semantic cases, the stress test, the cost runs, for any provider |
| `conformance/Kernel.Store.Conformance.Run` | The suite against the in-memory store |
| `conformance/Kernel.Runtime.Conformance` | The runtime conformance suite: semantic cases, the fence broken on purpose, the stress test, for any actor runtime |
| `conformance/Kernel.Runtime.Conformance.Run` | The runtime suite against the built-in runtime |
| `runtime/Kernel.Runtime.Hosting` | `RuntimeNode`: the built-in runtime of one node on a clock |
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
for p in sim models/Native.Run ef/Kernel.Ef.Run ef/Kernel.Ef.Conformance conformance/Kernel.Store.Conformance.Run mongo/Kernel.Mongo.Run conformance/Kernel.Runtime.Conformance.Run; do dotnet build $p -c Release; done
dotnet test models/Graph.Tests -c Release      # the graph engine's corpus
```

## PostgreSQL Core Preview

Set `KERNEL_PREVIEW_POSTGRES` to the connection string of an empty PostgreSQL 17 database dedicated to the preview.
Run this from `prototypes/kernel-model` with the SDK in [global.json](global.json).

```bash
models/Native.Run/run-core-preview.sh
```

The first Embedded host starts a Native approval workflow through the public Foundation contract, records Ada's approval,
and exits at the second wait. The next process opens the same database and reads from the first process's journal bookmark.
It sends Ada's operation ID again with a different payload, then sends Grace's approval with a new ID. The stored
serialized workflow state and checkpoint revision stay unchanged through replay. The journal and final result verify that each approval applied once.
Between the two owner processes, a separate diagnostic process bound to another tenant checks that it cannot
send to the workflow or read its checkpoint or journal. This probe uses no measured connection.

The default receipt estimates round trips by adding EF commands and explicit transaction completions. It excludes connection setup. Setup, ownership and journal reads have their own rows.
The relational store reads its format marker once during schema setup and once per nonempty take or retake, including a set take. The preview counts these reads in setup and ownership.
The approval and replay rows include public admission and the Worker pass. Diagnostic checkpoint probes and deliberately
refused commits use a separate unmeasured client. The demonstration scopes execution to one generated root and direct messages.

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
KERNEL_PREVIEW_MEASURE_WIRE=1 models/Native.Run/run-core-preview.sh
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

Public PostgreSQL readers check the format once when opening the journal cursor. On two fresh PostgreSQL 17.12
runs with this check, the first process reported 38 or 39 TCP turns for setup, the restarted process reported six,
and both still reported 13 for ownership. The five workflow phases retained their earlier counts.

The general relational projector checks the format before reading the cursor, reading a journal page and
writing a projection. A PostgreSQL 17 round that advanced the cursor measured six TCP turns with those
checks, up from three. The general journal reader, view reader and sweeper each check the marker on entry.
The public preview uses separate tenant-scoped checkpoint and journal queries.

To run two tenants' Native approvals on the same Embedded worker, use a fresh dedicated database. After building
`models/Native.Run` in Release, run the following commands from `prototypes/kernel-model`. Supply the three
references printed by the first process to the second. The third identifies a root outside the owner's set.

```bash
dll=models/Native.Run/bin/Release/net10.0/Kernel.Native.Run.dll
birth=$(dotnet "$dll" pg-multi-root owned in-batch birth)
read -r label first_ref second_ref outside_ref <<< "$(printf '%s\n' "$birth" | awk '$1 == "multi-references"')"
dotnet "$dll" pg-multi-root owned in-batch restart "$first_ref" "$second_ref" "$outside_ref"
```

The fixture checks the PostgreSQL transaction IDs for two journal rows at different actor revisions, replays a
direct input after restart, and checks that a terminal refusal on one root rolls back both turns. The second
tenant issues its public requests in a separate process bound to that tenant. The writer choices
are `owned` and `ef`. The fence choices are `in-batch` and `checked`.

To observe an abrupt owner loss on a POSIX host, use a fresh dedicated database and run:

```bash
models/Native.Run/run-core-preview-failover.py
```

The fixture starts two Embedded host processes for the same root. The first keeps renewing its lease at the second
wait. It drives idle Worker passes and heartbeat renewals beyond the original database lease deadline. The standby
checks the same node and epoch still have a live renewed lease. After PostgreSQL refuses its take, the fixture sends SIGKILL to its first child. The same standby
acquires a later epoch after the stored deadline, resumes the journal bookmark, replays Ada's input and records
Grace's approval. This exercises one fixed partition. K5 covers Cluster placement and balancing across partitions.

### Flowchart restart on the public PostgreSQL preview

Set `KERNEL_PREVIEW_POSTGRES` to a fresh PostgreSQL database and run from `prototypes/kernel-model`:

```bash
models/Native.Run/run-core-preview.sh pg-flowchart-preview
```

The Flowchart routes an amount of 750 to the approval path and persists a second approval wait. A new process
loads that wait. Replaying the first approval leaves the checkpoint and journal unchanged and runs zero actor
turns. The second approval runs one turn and the join fires once. The automatic branch stays inactive and the
journal records one completion revision.

This is a runnable K7 case on the public Embedded preview. The full Flowchart corpus and Cluster qualification
remain part of K7.

## Built-in Cluster redistribution on PostgreSQL

Set `KERNEL_PREVIEW_POSTGRES` to the connection string of a fresh, empty PostgreSQL database, then run from
`prototypes/kernel-model`:

```bash
dotnet restore models/Native.Run --locked-mode
dotnet run -c Release --no-restore --project models/Native.Run -- pg-runtime-cluster
```

Append `wake` to use the optional PostgreSQL wake transport. The two processes listen on separate
connections. After each of the first two admissions commits, the first node queues the target
partition number for PostgreSQL `NOTIFY`. The receiving node checks both transport callbacks and confirms
that its active partition forwarded both hints to the durable Worker. After the second
node leaves, the first node applies the final approval through the inbox with no hint. Run it
against another empty database to compare with the polling-only run.

Wake hints carry a partition number. They never carry an actor name, tenant or command payload.
The sender uses a bounded queue and a separate connection. A full queue or connection failure
can drop a hint. The node still polls on its ordinary interval.

Two operating-system processes run the built-in `RuntimeNode` and a durable Worker per node. PostgreSQL shows
eight partitions on the first node, four each when the second joins, and all eight back on the first after the
second stops gracefully. A Native workflow waits twice for approval events. The departing node records Ada, and
its successor records Grace at the next journal position.

This fixture uses the general PostgreSQL durable workflow path and the Native compatibility adapter. Public
Foundation use across an owned set, grouped partition transfers, and qualification of the 1,024-partition release
profile remain K5 work.

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
$SIM --placement-check                               # round 5 A: virtual-partition addressing and range placement (src/Kernel.Runtime/VirtualPartitions.cs)
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
EFC="dotnet ef/Kernel.Ef.Conformance/bin/Release/net10.0/Kernel.Ef.Conformance.dll"
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
$EFR x probe|block|definite|deadlock <engine> "<connection>"                         # ENGINES.md
$EFR x fence <engine> "<connection;Database=kernel_fence>"                            # the k-row fence over eight partitions, one stale: COMMIT-SQL.md
$EFR x setcost <engine> "<connection;Database=kernel_setcost>" proxy=1 [writes=1] [runs=5] [shape=both|scattered|contiguous] [count=4096]   # K1: ownership of a set in the integrated Worker, PARTITIONS.md "K1 costs"
$EFR x setidle postgres "<connection;Database=kernel_idle>" seconds=120 writes=1      # K1, D3: four RuntimeNodes idle, the beat's WAL
$EFR x stress|cost postgres "<connection>" writes=1 [history=state|minimal] [pad=4096] [fillfactor=70] [fastvacuum=1]   # WRITES.md; the container needs -c wal_keep_size=4GB
```

Without a fence option the EF store takes the engine's default: the fence in the batch on every engine (SQL Server since
16 November, for the owned commit; ENGINES.md E9; `fence=checked` is the 13 November fallback and stays the default with
`commit=ef`, whose batch still deadlocks takes). Without a commit option it writes the commit with its own SQL on every
engine (round 5 B, COMMIT-SQL.md); `commit=ef` is EF's SaveChanges, the reference. `EngineRun.cs` lists every option. The stress test drives round 1's models plus round 3's commands (suspend, resume,
terminate, delete) and starts on events. Do not run the conformance suite on an engine while a stress test runs there:
the stress test kills the suite's sessions too.

## Native

```
NAT="dotnet models/Native.Run/bin/Release/net10.0/Kernel.Native.Run.dll"
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
