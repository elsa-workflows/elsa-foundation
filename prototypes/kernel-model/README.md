# Kernel model

A standalone model of the execution kernel sketch ([DESIGN.md](DESIGN.md), draft 3 as revised by round 3) and a simulation that tries to break it.
It tests the design before product code exists. It shares nothing with the Elsa runtime and builds as its own
repository: the folder has its own `Directory.Build.*`, `Directory.Packages.props`, `NuGet.config`, `global.json` and
`.editorconfig`.

What was found is in [FINDINGS.md](FINDINGS.md).

## Layout

| Path | What |
|---|---|
| `src/Kernel` | The kernel: contracts, storage interface, partition worker, balancer, projector, reply addresses for live output, in-memory store. No clock, thread, timer or random number of its own |
| `sim` | Toy execution models, the deterministic simulation, the invariant checks, the shrinker and the mutation check |
| `ef/Kernel.EntityFramework` | The storage interface on Entity Framework Core for PostgreSQL and SQLite |
| `ef/Kernel.Ef.Run` | The cost measurement and the concurrent stress test |

## Run

```
dotnet build sim -c Release
dotnet sim/bin/Release/net10.0/Kernel.Sim.dll --seeds 100000              # 100,000 seeds of 300 actions each
dotnet sim/bin/Release/net10.0/Kernel.Sim.dll --seed 626 --flaw HandOverWhileBehind   # replay one seed; prints a shrunk trace when it fails
dotnet sim/bin/Release/net10.0/Kernel.Sim.dll --mutations --seeds 20000   # break the kernel on purpose, one flaw at a time
dotnet sim/bin/Release/net10.0/Kernel.Sim.dll --seeds 4000 --coverage      # also count what happened (requests answered, trees terminated, ...)
dotnet sim/bin/Release/net10.0/Kernel.Sim.dll --seeds 5000 --actions 1000 --faults 2   # scale lost calls, wrong answers, crashes, pauses and long silences

dotnet build ef/Kernel.Ef.Run -c Release
dotnet ef/Kernel.Ef.Run/bin/Release/net10.0/Kernel.Ef.Run.dll cost sqlite
docker run -d --rm -P -e POSTGRES_PASSWORD=kernel --name kernel-model-pg postgres:17-alpine
docker port kernel-model-pg 5432                                          # the port Docker picked
dotnet ef/Kernel.Ef.Run/bin/Release/net10.0/Kernel.Ef.Run.dll cost postgres "Host=localhost;Port=<port>;Username=postgres;Password=kernel;Database=kernel_cost"
dotnet ef/Kernel.Ef.Run/bin/Release/net10.0/Kernel.Ef.Run.dll stress "Host=localhost;Port=<port>;Username=postgres;Password=kernel;Database=kernel_stress" 40 5000
dotnet ef/Kernel.Ef.Run/bin/Release/net10.0/Kernel.Ef.Run.dll stress-unfenced "<same>" 15 1500   # the fence broken on purpose: must fail
dotnet ef/Kernel.Ef.Run/bin/Release/net10.0/Kernel.Ef.Run.dll probe "<same>"                     # seq order inside one commit (finding F8)
```

A seed fixes everything: the number of nodes, every action and every fault. `--flaw` takes the names of
`Kernel.Flaw` (comma-separated); each one switches off one rule of the protocol.

## How the simulation works

- Several nodes run against one in-memory store. Each `(node, partition)` pair is an *actor* with its own worker.
- One action happens at a time, chosen from the seed: a worker takes, polls, loads, runs one step, flushes, beats or
  reads events; a node balances; a store call executes; its answer arrives, arrives as "connection lost" although it
  executed, or the call never executes; an outsider adds a message, retries an old one, hands a start to an owner in
  memory, publishes an event, registers a start subscription, suspends, resumes, terminates, rebinds or deletes an
  instance; a request starts or resumes an HTTP workflow, or its caller goes away; the transport between nodes
  delivers, repeats or drops a reply; off-step work completes; time passes; a wall clock jumps; a node crashes and
  restarts, with or without its old process living on, sometimes at another model version; a node pauses or resumes;
  a projector runs a round; the sweep runs.
- A store call is on a wire: the request executes at one moment and the answer arrives at another. A worker's
  continuation runs inline when the simulation delivers the answer, so there are no threads.
- After every action the invariants are checked against the rows, the owners' memory and an oracle of what was sent.
- Then faults stop, the system runs until idle, and the end state is checked.
- A failing run is shrunk: actions are removed while the same invariant still breaks, and the remaining trace is printed.

All monotonic clocks tick at the database's rate; wall clocks are skewed and jump. Bounded drift of the monotonic
clock is not modelled.
