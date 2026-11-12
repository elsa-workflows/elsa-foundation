# Kernel model

A standalone model of the execution kernel ([DESIGN.md](DESIGN.md): the design as built, against draft 4 of the
sketch), the stores it runs on, the native Elsa workflow model on it, and a simulation that tries to break it all.
It tests the design before product code exists. It shares nothing with the Elsa runtime and builds as its own
repository: the folder has its own `Directory.Build.*`, `Directory.Packages.props`, `NuGet.config`, `global.json` and
`.editorconfig`.

What was found is in [FINDINGS.md](FINDINGS.md) (section 23: the merge of the four branches and its gates); what each
database engine does to the EF store is in [ENGINES.md](ENGINES.md), MongoDB in [MONGO.md](MONGO.md), and native
workflows as an execution model in [NATIVE.md](NATIVE.md).

## Layout

| Path | What |
|---|---|
| `src/Kernel` | The kernel: contracts, storage interface, partition worker, balancer, projector, reply addresses for live output, in-memory store, the owner's defaults. No clock, thread, timer or random number of its own |
| `sim` | Toy execution models, the deterministic simulation, the invariant checks, the shrinker and the mutation check; `NativeSim.cs` puts native instances in the mix |
| `models/Native` | The native Elsa workflow model: authoring surface, activities, interpreter, HTTP routes and host on the kernel's live output |
| `models/Native.Run` | Native smoke tests, cost and memory measurement |
| `ef/Kernel.EntityFramework` | The storage interface on Entity Framework Core: PostgreSQL and SQLite in `EfStore.cs`, SQL Server and MySQL dialects in `Engines.cs` |
| `ef/Kernel.Ef.Run` | The cost measurement and the concurrent stress test; `EngineRun.cs` runs them on PostgreSQL, SQL Server, MySQL and SQLite (`x ...`) |
| `ef/Kernel.Ef.Conformance` | The conformance suite against the EF store, every engine |
| `conformance/Kernel.Store.Conformance` | The store conformance suite: semantic cases, the stress test, the cost runs, for any provider |
| `conformance/Kernel.Store.Conformance.Run` | The suite against the in-memory store |
| `mongo/Kernel.Mongo` | The storage interface on MongoDB |
| `mongo/Kernel.Mongo.Run` | The suite against MongoDB, its connection killer, probes |

## Build

One project at a time (they share `obj/` folders through project references):

```
for p in sim models/Native.Run ef/Kernel.Ef.Run ef/Kernel.Ef.Conformance conformance/Kernel.Store.Conformance.Run mongo/Kernel.Mongo.Run; do dotnet build $p -c Release; done
```

## The simulation

```
SIM="dotnet sim/bin/Release/net10.0/Kernel.Sim.dll"
$SIM --seeds 200000                                  # 200,000 seeds of 300 actions each
$SIM --seeds 20000 --actions 1000                    # 20,000 seeds of 1,000 actions
$SIM --native --seeds 200000                         # the same with native instances in the mix (40 % of outside actions)
$SIM --native --seeds 20000 --actions 1000
$SIM --seed 22 --flaw TrialPerPartition              # replay one seed; prints a shrunk trace when it fails
$SIM --mutations --seeds 20000                       # break the kernel on purpose, one flaw at a time
$SIM --native-mutations --seeds 20000                # break the native model on purpose
$SIM --native --native-flaw ResumeAnyChild --seed 1  # replay one native flaw
$SIM --seeds 4000 --coverage                         # also count what happened
$SIM --seeds 5000 --actions 1000 --faults 2          # scale the faults
$SIM --backlog 1000                                  # how long a backlog of suspects takes to recover
```

A seed fixes everything: the number of nodes, every action and every fault. `--flaw` takes the names of
`Kernel.Flaw` (comma-separated); each one switches off one rule of the protocol. FINDINGS section 23 lists the first
failing seed of every flaw.

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

## The store conformance suite

```
dotnet conformance/Kernel.Store.Conformance.Run/bin/Release/net10.0/Kernel.Store.Conformance.Run.dll semantic     # in memory
EFC="dotnet ef/Kernel.Ef.Conformance/bin/Release/net10.0/Kernel.Ef.Conformance.dll"
$EFC sqlite [--fence-in-batch] semantic
$EFC postgres "$PG" [--fence-in-batch] [--m5] semantic                 # --m5: E1 switched off, must fail the fence cases
$EFC sqlserver "$MS" [--fence-in-batch] [--rcsi-off] semantic
$EFC mysql "$MY;uid=root;pwd=kernel" [--fence-in-batch] [--rc] semantic
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
$EFR x stress postgres "$PG;Database=kernel_stress" seconds=40 messages=5000 [fence=batch]
$EFR x stress sqlserver "$MS;Database=kernel_stress" seconds=40 messages=5000          # RCSI on unless rcsi=0
$EFR x stress mysql "$MY;database=kernel_stress" seconds=40 messages=5000 [isolation=rc]
$EFR x stress sqlite "Data Source=.out/stress.db" seconds=40 messages=5000 [wal=0]
$EFR x stress <engine> "<same>" seconds=15 messages=1500 unfenced=1       # the fence broken on purpose: must fail
$EFR x cost <engine> "<connection;Database=kernel_cost>" proxy=1 [fence=batch]       # round trips on the TCP stream
$EFR x probe|block|definite|deadlock <engine> "<connection>"                         # ENGINES.md
```

`EngineRun.cs` lists every option. The stress test drives round 1's models plus round 3's commands (suspend, resume,
terminate, delete) and starts on events. Do not run the conformance suite on an engine while a stress test runs there:
the stress test kills the suite's sessions too.

## Native

```
NAT="dotnet models/Native.Run/bin/Release/net10.0/Kernel.Native.Run.dll"
$NAT smoke
$NAT cost sqlite
$NAT cost postgres "$PG;Database=kernel_native"
$NAT memory 3000
```

## How the simulation works

- Several nodes run against one in-memory store. Each `(node, partition)` pair is an *actor* with its own worker.
- One action happens at a time, chosen from the seed: a worker takes, polls, loads, runs one step, flushes, beats or
  reads events; a node balances; a store call executes; its answer arrives, arrives as "connection lost" although it
  executed, or the call never executes; an outsider adds a message, retries an old one, hands a start to an owner in
  memory, publishes an event, registers a start subscription, suspends, resumes, terminates, rebinds or deletes an
  instance; a request starts or resumes an HTTP workflow, or its caller goes away; the transport between nodes
  delivers, repeats or drops a reply; off-step work completes; time passes; a wall clock jumps; a node crashes and
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
