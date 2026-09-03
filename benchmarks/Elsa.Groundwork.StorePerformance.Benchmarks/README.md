# Groundwork store performance harness (#646)

This is the adapter-independent foundation for the thirteen reviewed Spec 094 store workloads. It has no
provider, EF, Groundwork, or connection dependency; Secret workload code uses only the public Secret
contracts. Adapter leaves must implement
`IBenchmarkAdapter` in their own projects and use `ProcessMeasurement` to write `ProcessArtifact` files.
`workload-vectors` prints deterministic contract definitions for future adapters; it does not execute the
named public operations. Every workload carries an explicit ready/blocked benchmark-admission status. The
executable Secret v1.1 successor is driven by the sibling adapter host: SQLite has a real EF oracle and a
Groundwork public-repository comparator, while the historical v1.0 hashes remain retained as immutable
blocked history. Its executable source is the separate
specs/094-harden-groundwork-stores/workloads/secret-create-read-list-v1.1.json successor file;
iam-secrets.json remains byte-for-byte historical input. Diagnostics keeps comparison and ratio-gate
admission blocked until numeric absolute budgets are independently ratified, but once its native-plan
prerequisites are complete it may produce one strict, provenance-bound four-process measurement set.
That set is ungraded evidence for deriving reviewed budgets; it is not a performance verdict.

The thirteenth workload, `diagnostics-durable-history`, is the program-owner-ratified 2026-07-25
extension. It keeps Structured Logs and OpenTelemetry as separate suboperations under one reproducible
input/result vector. All four Groundwork providers require workload-specific numeric absolute budgets
plus correctness, native-plan, physical-form, and provider-work evidence. The retained SQLite EF adapter
is correctness-only and is not a timed ratio oracle. The policy shape is independently reviewed in PR
#1514, but the numeric budgets are not yet ratified; `gate.diagnostics.absolute-budget-required`
prevents the default ratio gate from being silently applied. The generic no-comparand evaluator exists,
but it accepts only an explicit independently reviewed policy and has no diagnostics defaults.

`matrix <scale>` executes one untimed adapter-host warm-up process followed by three independent measured
adapter-host processes. A measured artifact carries the frozen seed/input fingerprint, provider-native plan
identity/evidence reference/content digest, provider server version/topology/material settings, an opaque
OS-machine fingerprint, safe machine architecture metadata, the exact harness-assembly SHA-256, and every
operation latency plus an exact provider-native round-trip count for every latency sample and observer identity.
Both the public matrix runner and the adapter child refuse a requested commit that is not the clean repository HEAD,
and the child refuses a different harness assembly or missing exact observer before adapter preparation. The schema-v2
artifact is valid only after its observed correctness digest equals the frozen workload digest and every
required native route carries an admitted content digest, cardinality, scope/route predicates, finite limit, and
materialized-count fact. Each route also names a distinct retained raw provider-plan JSON/text/XML artifact whose
SHA-256 is verified; secret-bearing or oversized raw plans fail closed. The safe top-level summary JSON must exist,
match the requested SHA-256, and bind its workload input, target, provider metadata, source provenance, host fingerprint,
harness assembly, and structured route evidence. The cohort manifest binds the expected commit, host, harness assembly,
process artifacts, summaries, and raw-plan files. Manifest creation and the in-process delegate runner are test-internal;
production callers must use the source-validating process runner.
Place the artifact directory outside the worktree (or in an already ignored path) so the second target can prove
the same clean source snapshot.

Each `matrix` invocation contributes one unique four-process measurement set. The first set creates a comparison
cohort; the second may join only that same cohort and must have a distinct measurement-set identity. Planned paths
must be fresh, every child artifact is validated against its complete request immediately, and the manifest is
rebuilt over both complete sets. Stale same-set paths, mixed cohorts, partial sets, extra files, and evidence
tampering fail closed. The cohort directory may otherwise contain only the default `comparison.v1.json`,
`comparison.from-gate.v1.json`, `gate.v1.json`, `measurement.v1.json`, and `budget-gate.v1.json` result files;
write custom result paths outside it.

`measure` validates one complete four-process target without an oracle and emits a distinct no-comparand
measurement result whose schema-v2 `EvaluationStatus` is explicitly `ungraded`. Diagnostics uses this same one-set path for budget-derivation evidence;
`compare` and the ratio `gate` continue to refuse diagnostics artifacts. `budget-gate` derives a fresh admitted measurement from that manifest-bound artifact
directory and applies an independently reviewed, provider/workload-scoped
absolute policy with p95, p99, and throughput bounds for every budget-bearing operation. A reviewed class map
may share a budget across operations and must explicitly mark non-timing operations `NotHotPath`. It never
falls back to ratio gates; missing, extra, ambiguous, or double-defined entries fail closed. No default or
numeric diagnostics policy is shipped here — those values remain a program-owner ratification decision.

`compare` rejects incomplete or internally inconsistent targets (including a missing operation, changed input,
different commit, different physical host, changed machine environment, or provider/form outside the frozen
workload). Capture timestamps are excluded from machine equality; adapter-specific package, composition, provider
version, and material provider-configuration metadata remain exact and stable within each target. Stored
p50/p95/p99/throughput summaries must reproduce within serialization tolerance from
finite positive raw samples before comparison or gating. `gate` retains raw samples by measured process and uses
the median of the three measured p50/p95/p99/throughput values. Its capped paired-independent percentile-
bootstrap *ratio* intervals resample within each process, then take the median process percentile; they
never flatten samples across processes.

The composition fingerprint is generated by the AdapterHost from the selected registry declarations and the
request's safe workload/provider/package metadata. It is not a physical-target fingerprint and does not hash
binaries or connection strings. The Python runner generates it automatically; the runner's operator-supplied
`--composition` is optional and, when present, is an expected-value check. Direct harness `matrix` invocations
must still provide the generated value through their required `--composition` option.

Default gates derive their class from the exact workload id. Runtime hot paths use the Spec 094
performance-handoff ratios (p95 <= 1.10x, throughput >= 90%, p99 <= 2.0x) plus a 150 ms p95 ceiling for
durable writes or the #1176-adopted 40 ms backstop for bounded reads (pending explicit acceptance).
Ordinary workloads use p95 <= 1.25x, throughput >= 80%, and
p99 <= 2.0x without a default absolute ceiling. A replacement must use the workload-derived class and
carry non-empty, distinct proposer/reviewer identities, a review reference, and the exact workload
id/version; it cannot be self-authored.

The protocol/statistics shape was recovered from the retired Spec 093 design harness at commit `30ec15491`.
No design targets, SQLite implementation, EF references, or its superseded absolute-budget amendment are
retained here.

```text
dotnet run --project benchmarks/Elsa.Groundwork.StorePerformance.Benchmarks -- host-fingerprint

dotnet run --project benchmarks/Elsa.Groundwork.StorePerformance.AdapterHost -- \
  describe-composition --request '<request-json>'

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

dotnet run --project benchmarks/Elsa.Groundwork.StorePerformance.Benchmarks -- \
  measure --out <artifact-directory> [--result <measurement.json>]

dotnet run --project benchmarks/Elsa.Groundwork.StorePerformance.Benchmarks -- \
  budget-gate --out <artifact-directory> --policy <absolute-budget-policy.json> \
  [--result <budget-gate.json>]
```

Provider and physical-form identities are admitted by the frozen workload catalog. Adapter identities remain
leaf-defined because the Spec 094 workload documents do not yet provide an adapter allowlist. Exact adapter
admission therefore remains an open contract decision; this checkpoint does not infer an allowlist or claim that
gap complete.
