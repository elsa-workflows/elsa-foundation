# Write cost, 14 November 2026

This is an authored finite experiment on the scoped-inbox foundation. It is not independent acceptance,
provider capability evidence, product qualification, a tuning recommendation or a new benchmark utility.
See the [WRITES supplement](../../WRITES.md#14-november-scoped-inbox-foundation).

- `cost-cases.jsonl`: 64 complete case records, 16 actual generic cost cases × four variants; setup is separate.
- `tuples.jsonl`: eight snapshots (end/after per variant), with Instances/Journal/Messages/Partitions counters,
  exact tuple scans and Instances TOAST; timestamps describe observation times, not performance.
- `provenance.json`: source/build pins, settings/options, checks, public artifact hashes and all 38 frozen local
  evidence hashes. Raw logs, DDL/schema receipts, full tuples, runner and receipt are retained locally;
  their digests identify them but do not make their contents publicly downloadable.
- `adapter.cs.txt` is the exact 49-line measured external reflection adapter, archived as text.
  `adapter.csproj.txt` substitutes a caller-supplied `KernelRunOutput` for the original private build path;
  it is not the byte-identical original project. No build project is added to the repository.

## Reproduce safely

Reproduction is a new experiment and can produce different bytes. Use a clean checkout pinned to the source
commit in provenance, the ordinary build-slot wrapper, and a newly owned isolated PostgreSQL 17 server.
Use the recorded image/settings, random loopback port and private 0600 credential input; do not use a shared
server, existing database, old retained container, fixed port 5095 or a connection containing public secrets.
Before any invocation, independently check the server's exact container ID/ownership label, loopback port,
and `SELECT count(*) FROM pg_database WHERE datname = '<fresh random kwr22_ target>'` returning **0**.
Then set `KERNEL_WRITES_CONNECTION` privately, `KERNEL_WRITES_PORT` to that exact owned port, and
`KERNEL_WRITES_SNAPSHOT` to a new output file. The adapter checks absence again before invoking
`EngineRun.RunAsync`; its existing `EnsureDeleted` must only ever encounter an absent target. **Refuse any
existing target, including empty databases; never reset or delete it to make the check pass.** Keep DDL logging
on, verify zero `DROP DATABASE` statements, and retain every target. The measured populated-target negative
control refused before invocation; do not bypass that guard. Stop only the exact owned container after
checking its ID and ownership label. Never remove earlier containers or databases.

Materialize the two text fixtures in an external scratch directory as `Program.cs` and `WritesAdapter.csproj`.
Build `prototypes/kernel-model/ef/Kernel.Ef.Run/Kernel.Ef.Run.csproj` at the pinned checkout, then the external project with
`-p:KernelRunOutput=<pinned Kernel.Ef.Run/bin/Release/net10.0 directory>`. Both targeted Release builds use
`GIT_CONFIG_COUNT=1 GIT_CONFIG_KEY_0=core.fsmonitor GIT_CONFIG_VALUE_0=false` and
`-m:1 -nr:false -p:UseSharedCompilation=false`; do not bypass the normal wrapper or build the whole solution.
Run the adapter DLL with `dotnet exec --depsfile <pinned Kernel.Ef.Run.deps.json> <adapter DLL>` and each
variant's arguments from provenance plus `fence=batch`, once per independently absent fresh database. It supplies the unchanged
`cost postgres <private connection> writes=1 sizes=1 many=100,1000` workload through reflection; it also
closes workload pools, scans tuples, waits 70 seconds without forced vacuum, and scans again. Retain incidental
timing output without using it as speed evidence. Capture settings/schema/indexes and DDL separately.
No proxy/wire counter was enabled. Driver commands, transaction ends and derived `sent` are not measured wire.
