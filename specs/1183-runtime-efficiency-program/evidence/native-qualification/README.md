# Native qualification driver

Prepare the frozen project/lock templates as described in the [manual fixture entrypoint](../README.md) before building.

One-off, bounded owner-requested qualification for Runtime Efficiency #2531. This updates the frozen native
[assessment driver](../../../../docs/reports/evidence/native-runtime-performance-2026-10-31/harness/Program.cs.txt) with the production identifier registration and stronger accounting. It is not a benchmark
service or a timing CI gate. Build against the repository revision being qualified; retain source/binary hashes,
configuration, host/storage placement, all failed attempts, raw results and sample-window times.

Each process receives one JSON configuration path. PostgreSQL receives its base connection through the protected
`ELSA_EFFICIENCY_POSTGRES_CONNECTION` environment variable. The output does not serialize that variable. Use only
fresh task-owned databases; the optional existing database is for coordinated starts against the same fixture.
Existing result files are refused. A file start gate expires after three minutes; an optional UTC start must be
within five minutes of readiness. Record actual sample-window overlap and guest clock synchronization for coordinated
starts; the timestamp does not prove clocks are synchronized. An optional validation gate writes a measured-window
receipt and waits at most three minutes, allowing all nodes to finish before aggregate database checks add load.
The reported process peak is captured at sample end (including cold/warmup high water), before final validation. Operator configuration remains bounded to 1–100 leaf activities, 1–64 callers, 1–120 measured seconds and at most
10,000 minimum completions. Cold, warmup, one command-attribution request and timed windows remain distinct.
Commands are counted only in that separate attribution request. Keep diagnostics/tracing out of timed windows.

The `Noop` and `DeclaredNoop` leaf bodies are no-ops, declaring ReplaySafe or External semantics. Payload profiles
also verify the hydrated input. None performs external business I/O; `External` selects the engine's side-effect
boundary contract. These establish runtime and payload-handling overhead, not business/ETL work throughput.
Execution state, exact leaf count, distinct generated IDs and drained queue are
required. Relational stores additionally check every owned execution's activity rows, incidents and non-Delivered
outbox rows. Memory also counts actual activity/incident rows and checks physically undelivered responsibility through the actual bound lookup
capability. Its outbox row-count field is null; a separate field counts executions with any undelivered source.
A failure invalidates the sample and remains in the ledger. Do not substitute later passing retries silently.

A shared database with different native starts on several physical hosts measures independent execution capacity.
It does not prove distributed placement, cancellation transport, concurrent same-execution ownership or failover.
Those require the separate real-host and failure qualification witnesses on this program. Do not multiply one-node
rates to predict multi-node capacity. Record the actual model, provider, node CPU and database host for every result.

The local one-second Memory/Balanced/10-leaf/four-caller, Memory/EphemeralNoRecord/one-leaf/four-caller and SQLite/Durable/one-leaf/two-caller runs are correctness
smokes only. All three pass their completion/accounting checks. Shared-machine load excludes them from throughput evidence.

## Input profiles under qualification

The new profiles below are source preparation: compilation, their positive/rejection controls and measured execution
remain unproved. The historical smoke results above do not qualify these changes. Keep this distinction in every
result and report until actual evidence is retained.

`InputProfile` defaults to `Noop`, preserving the legacy executable with no declared workflow input contract.
`DeclaredNoop` adds an explicitly empty version-one contract, so the cost of that contract can be observed separately.
Both require `PayloadBytes: 0`. The result records the actual nullable `InputContractVersion`.

`Literal` uses an empty version-one workflow input contract and a real activity literal binding. `WorkflowRequest`
declares one required String input named `payload` and hydrates it through a real workflow-request binding. Both
accept exactly `PayloadBytes: 1024` or `PayloadBytes: 65536`. Values contain only ASCII, so these are logical UTF-8
payload bytes before serialization, not wire, database or allocation bytes.

Literal values contain repeated `x` characters. Each workflow request instead starts with its current workflow
execution ID and fills the remaining bytes with `x`. Both payload profiles verify the entire value through actual
CLR input hydration; `WorkflowRequest` additionally verifies the current execution ID. The singleton fixture configuration is immutable; it does
not cache per-execution values or activity instances. Measured verification and logical-byte counters must match
completed measured executions times activity count and payload length, alongside the existing state invariants.
For `WorkflowRequest`, per-invocation input construction and serialization occur inside the published-start timing
boundary, along with runtime persistence and body verification. Literal construction, serialization into its binding
and publication happen before timed starts; runtime input hydration, persistence and body checks remain timed.

Before collecting rates, compile the frozen fixture project against the intended source and prove legacy `Noop`
parity, `DeclaredNoop`, and the four Literal/WorkflowRequest size cases. Retain a wrong/stale-request-value negative
control and actual required/type/unknown-input rejection through the fixture's real start composition. A plausible
byte length is insufficient. Keep cold, warmup, command-attribution and timed windows separate.

These profiles do not qualify JavaScript expression providers, sustained arrival load, real HTTP/ETL work, clustered
actor participation, owner crashes, duplicate delivery, takeover, database interruption or recovery. They do not
complete M5. Read the [program acceptance](../../../../docs/program-goals/runtime-efficiency.md) and
[topology declaration controls](../topology/README.md) before preparing an independent-host run.
