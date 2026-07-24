# Groundwork store performance harness (#646)

This is the adapter-independent foundation for the twelve frozen Spec 094 store workloads. It has no
provider, EF, Groundwork, connection, or secret dependency. Adapter leaves must implement
`IBenchmarkAdapter` in their own projects and use `ProcessMeasurement` to write `ProcessArtifact` files.

`matrix <scale>` executes one untimed adapter-host warm-up process followed by three independent measured
adapter-host processes. A measured artifact carries the frozen seed/input fingerprint, provider-native plan
identity/evidence reference, safe machine architecture metadata, and every operation latency. It is valid only
after its observed correctness digest equals the frozen workload digest and it includes the provider prerequisite
and all required native routes. The matrix writes an integrity-bound artifact manifest after all children finish.
`compare` rejects incomplete or internally inconsistent targets (including a missing operation or changed input)
and writes a versioned, hashed comparison result. `gate` retains raw samples by measured process and uses
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
  matrix <scale> --workload <id> --provider <provider> --adapter <adapter> \
  --form <physical-form> --commit <40-hex-sha> --composition <64-hex-fingerprint> \
  --package <name=version> --native-plan <identity> --native-plan-evidence <safe-reference> \
  --out <artifact-directory> --child-command <adapter-host>

dotnet run --project benchmarks/Elsa.Groundwork.StorePerformance.Benchmarks -- \
  compare --out <artifact-directory> --oracle <provider/adapter/form> --target <provider/adapter/form> \
  [--result <comparison.json>]

dotnet run --project benchmarks/Elsa.Groundwork.StorePerformance.Benchmarks -- \
  gate --out <artifact-directory> --oracle <provider/adapter/form> --target <provider/adapter/form> \
  [--class runtime|ordinary] [--replacement <reviewed-gate.v1.json>] [--result <gate.json>]
```
