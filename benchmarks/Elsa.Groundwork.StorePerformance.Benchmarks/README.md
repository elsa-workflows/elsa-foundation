# Groundwork store performance harness (#646)

This is the adapter-independent foundation for the twelve frozen Spec 094 store workloads. It has no
provider, EF, Groundwork, connection, or secret dependency. Adapter leaves must implement
`IBenchmarkAdapter` in their own projects and use `ProcessMeasurement` to write `ProcessArtifact` files.

`matrix <scale>` executes one untimed adapter-host warm-up process followed by three independent measured
adapter-host processes. A measured artifact carries the frozen seed/input fingerprint, provider-native plan
identity/evidence reference/content digest, safe machine architecture metadata, and every operation latency. The
schema-v2 artifact is valid only after its observed correctness digest equals the frozen workload digest and every
required native route carries an admitted content digest, cardinality, scope/route predicates, finite limit, and
materialized-count fact. Its safe top-level evidence JSON must exist, match the requested SHA-256, and reproduce
the admitted structured route evidence; the cohort manifest binds both process and evidence files.

Each `matrix` invocation contributes one unique four-process measurement set. The first set creates a comparison
cohort; the second may join only that same cohort and must have a distinct measurement-set identity. Planned paths
must be fresh, every child artifact is validated against its complete request immediately, and the manifest is
rebuilt over both complete sets. Stale same-set paths, mixed cohorts, partial sets, extra files, and evidence
tampering fail closed. The cohort directory may otherwise contain only the default `comparison.v1.json`,
`comparison.from-gate.v1.json`, and `gate.v1.json` result files; write custom result paths outside it.

`compare` rejects incomplete or internally inconsistent targets (including a missing operation, changed input,
different commit, changed machine environment, or provider/form outside the frozen workload). Capture timestamps
are excluded from machine equality; adapter-specific package and composition metadata remain exact and stable
within each target. Stored p50/p95/p99/throughput summaries must reproduce within serialization tolerance from
finite positive raw samples before comparison or gating. `gate` retains raw samples by measured process and uses
the median of the three measured p50/p95/p99/throughput values. Its capped paired-independent percentile-
bootstrap *ratio* intervals resample within each process, then take the median process percentile; they
never flatten samples across processes.

Default gates are the Spec 094 performance-handoff ratios: runtime p95 <= 1.10x and throughput >= 90%;
ordinary p95 <= 1.25x and throughput >= 80%; both require p99 <= 2.0x. A replacement must carry a distinct
reviewer, a review reference, and the exact workload id/version; it cannot be self-authored.

The protocol/statistics shape was recovered from the retired Spec 093 design harness at commit `30ec15491`.
No design targets, SQLite implementation, EF references, or its superseded absolute-budget amendment are
retained here.

```text
dotnet run --project benchmarks/Elsa.Groundwork.StorePerformance.Benchmarks -- \
  matrix <scale> --cohort <safe-id> --measurement-set <safe-id> \
  --workload <id> --provider <provider> --adapter <adapter> \
  --form <physical-form> --commit <40-hex-sha> --composition <64-hex-fingerprint> \
  --package <name=version> --native-plan <identity> --native-plan-evidence <safe-reference> \
  --native-plan-sha256 <64-hex-content-digest> \
  --out <artifact-directory> --child-command <adapter-host>

dotnet run --project benchmarks/Elsa.Groundwork.StorePerformance.Benchmarks -- \
  compare --out <artifact-directory> --oracle <provider/adapter/form> --target <provider/adapter/form> \
  [--result <comparison.json>]

dotnet run --project benchmarks/Elsa.Groundwork.StorePerformance.Benchmarks -- \
  gate --out <artifact-directory> --oracle <provider/adapter/form> --target <provider/adapter/form> \
  [--class runtime|ordinary] [--replacement <reviewed-gate.v1.json>] [--result <gate.json>]
```

Provider and physical-form identities are admitted by the frozen workload catalog. Adapter identities remain
leaf-defined because the Spec 094 workload documents do not yet provide an adapter allowlist. Exact adapter
admission therefore remains an open contract decision; this checkpoint does not infer an allowlist or claim that
gap complete.
