# Groundwork store performance harness (#646)

This is the adapter-independent foundation for the twelve frozen Spec 094 store workloads. It has no
provider, EF, Groundwork, connection, or secret dependency. Adapter leaves must implement
`IBenchmarkAdapter` in their own projects and use `ProcessMeasurement` to write `ProcessArtifact` files.
`workload-vectors` prints deterministic contract definitions for future adapters; it does not execute the
named public operations. Every workload carries an explicit ready/blocked benchmark-admission status. The
Secret workload remains blocked until a real EF comparator exists.

`matrix <scale>` executes one untimed adapter-host warm-up process followed by three independent measured
adapter-host processes. A measured artifact carries the frozen seed/input fingerprint, provider-native plan
identity/evidence reference/content digest, provider server version/topology/material settings, an opaque
OS-machine fingerprint, safe machine architecture metadata, the exact harness-assembly SHA-256, and every
operation latency. Both the public matrix runner and the adapter child refuse a requested commit that is not
the clean repository HEAD, and the child refuses a different harness assembly before adapter preparation. The
schema-v2 artifact is valid only after its observed correctness digest equals the frozen workload digest and every
required native route carries an admitted content digest, cardinality, scope/route predicates, finite limit, and
materialized-count fact. Each route also names a distinct retained raw provider-plan JSON/text/XML artifact whose
SHA-256 is verified; secret-bearing or oversized raw plans fail closed. The safe top-level summary JSON must
exist, match the requested SHA-256, and bind its workload input, target, provider metadata, source provenance,
host fingerprint, harness assembly, and structured route evidence. The cohort manifest binds the expected commit,
host, harness assembly, process artifacts, summaries, and raw-plan files. Manifest creation and the in-process
delegate runner are test-internal; production callers must use the source-validating process runner.
Place the artifact directory outside the worktree (or in an already ignored path) so the second target can prove
the same clean source snapshot.

Each `matrix` invocation contributes one unique four-process measurement set. The first set creates a comparison
cohort; the second may join only that same cohort and must have a distinct measurement-set identity. Planned paths
must be fresh, every child artifact is validated against its complete request immediately, and the manifest is
rebuilt over both complete sets. Stale same-set paths, mixed cohorts, partial sets, extra files, and evidence
tampering fail closed. The cohort directory may otherwise contain only the default `comparison.v1.json`,
`comparison.from-gate.v1.json`, and `gate.v1.json` result files; write custom result paths outside it.

`compare` rejects incomplete or internally inconsistent targets (including a missing operation, changed input,
different commit, different physical host, changed machine environment, or provider/form outside the frozen
workload). Capture timestamps are excluded from machine equality; adapter-specific package, composition, provider
version, and material provider-configuration metadata remain exact and stable within each target. Stored
p50/p95/p99/throughput summaries must reproduce within serialization tolerance from
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
dotnet run --project benchmarks/Elsa.Groundwork.StorePerformance.Benchmarks -- host-fingerprint

dotnet run --project benchmarks/Elsa.Groundwork.StorePerformance.Benchmarks -- \
  matrix <scale> --cohort <safe-id> --measurement-set <safe-id> \
  --workload <id> --provider <provider> --provider-version <server-version> \
  --provider-setting <safe-name=safe-value> --adapter <adapter> \
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
