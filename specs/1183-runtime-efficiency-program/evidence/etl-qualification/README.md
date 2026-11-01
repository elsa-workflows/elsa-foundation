# Finite ETL qualification

A bounded, one-off Runtime Efficiency #2531 fixture for the existing finite file ETL runtime and SQLite provider.
It uses public compilation/admission services, the real worker, immutable input snapshots, scoped file bindings,
lookup, partition/batch limits, immutable effect-addressed output with separately checked content hashes and durable acceptance. It creates a fresh absolute output
directory and refuses an existing one. Inputs are synthetic unique names with a pinned one-row lookup.

The sample measures admission through durable terminal acceptance, including snapshot preparation, worker wakeup
and 100 ms status polling. Dataset generation, compilation, host migration/startup, output validation and native
callback delivery are outside that interval. Host startup is reported separately. Each result records actual rows,
CPU, allocations, GC, peak working set and configuration. `CollectCommands` enables separate physical EF command/
transaction attribution; such runs are excluded from timing. The standalone host has no log/trace exporter.

Output validation checks every chunk's length/hash, every JSONL name/category, exact distinct row count, all completed
partitions and no outstanding records/bytes. No native result adapter is composed: the actual durable callback
remains owed and is explicitly required/reported as one. This is ETL acceptance throughput, not an end-to-end native
workflow result, clustered ETL, HTTP connector throughput or a comparison with a native activity. The real native
bridge and crash/replay behavior have separate suite/process qualification. Do not present an owed callback as a
drained workflow. No persistence mode, production default or diagnostics fidelity is changed.

Build `Probe.csproj` against the qualified repository source and pass one JSON configuration path. Bounds are
1–100,000 rows, 1–8 live attempts, 1–16 partitions, 1–1,000 batch records and 1–1,024 payload characters. Use equal
inputs, actual independent hosts/storage placement, fresh outputs and retained failures for comparisons. The local
128-row/two-attempt/four-partition smokes with and without SQL attribution pass accounting; shared-machine timing is
excluded. This fixture is not a benchmark service, SLA or CI timing gate.
