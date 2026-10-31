# Implementation comparison evidence

Companion to the [implementation report](../../native-runtime-performance-implementation-2026-10-31.md) and [spec 1180](../../../../specs/1180-native-runtime-performance/spec.md).

The original [assessment harness and instructions](../native-runtime-performance-2026-10-31/README.md) describe the workload, machine/runtime, provider setup and correctness checks. This follow-up deliberately reuses the frozen original `Perf.dll`. `binary-comparison.json` records every DLL in both directories; exactly three Elsa assemblies differ. The baseline hashes were checked against the original assessment provenance before running.

- `audit.json`: accepted/excluded process totals, completion count, binary comparison and expected checkpoint/transaction checks.
- `summary.json`: medians and observed throughput ranges by mode, leaf count, callers and variant. These are two fresh-process repetitions per variant, not confidence intervals.
- `binary-comparison.json`: SHA-256 of all baseline/candidate DLLs. The candidate production sources are on `adf1929371c77c68589f22b0ba4b8ed9ec96c3ea`; the three replacement assemblies were built on `54c9b8dc673e0baf3bd134ffcc9916bfa70c20e5`, whose production sources are identical.
- `test-accounting.json`: whole-project TRX totals mapped to `--list-tests`, including execution-time theory expansions. The optional SQLite stress test is skipped, not passed.
- `causal-controls.json`: original-code and injected-fault failures. The mutations are temporary controls, not retained source changes.
- `raw-evidence.tar.gz`: measured/rejected results with raw latency samples, exact configurations, environment ledgers, validation logs, causal controls and reproduction scripts. Temporary absolute paths preserve the original run provenance; adjust them before reproducing elsewhere.
- `database-environment.txt`, `cleanup.json`: disposable PostgreSQL configuration and owned-resource cleanup.

Run `compare.py` only after builds, tests and profilers finish. It waits for five two-second external-CPU samples at or below 400%, and rejects two consecutive samples above 500% within the timed window plus a two-second margin. It excludes the probe process and OrbStack. This is a shared-machine exclusion rule, not a claim of dedicated hardware or isolated VM activity. Every accepted interval must have process exit zero and `Correct=true`; the runner never selects on throughput. It reverses baseline/candidate ordering in repetition two. Use a fresh output directory for a new independent experiment.

The SQL/transaction accounting is one separate untimed dispatch per process. Commands are EF interceptor events, not physical network round trips or connection opens. Checkpoint counts are derived from checkpoint INSERTs in that accounting sample. Background schema maintenance may overlap; retain the SQL text and interpret overlaps separately. Runtime completion/state/leaf counts, incidents, pending queue and outbox checks are validated independently of timings.
