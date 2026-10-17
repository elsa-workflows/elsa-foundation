# Unified execution work proof

This isolated .NET 10 experiment tests whether durable worker invocation and result delivery can serve both workflow activities and partitioned dataflow while leaving completion and checkpoint meaning with their composition. It changes no Elsa production code and makes no claim of BPMN or Elsa runtime conformance.

From the repository root, run:

```bash
dotnet restore tools/experiments/UnifiedExecution.WorkProof/UnifiedExecution.WorkProof.csproj
dotnet build tools/experiments/UnifiedExecution.WorkProof/UnifiedExecution.WorkProof.csproj --no-restore
dotnet run --project tools/experiments/UnifiedExecution.WorkProof/UnifiedExecution.WorkProof.csproj --no-build -- proof
```

The runner writes `evidence.json` and a compact `evidence.zip` beside the project. It prints the temporary SQLite scratch directory as well. The report contains the repository commit, SHA-256 hashes of the experiment source files, `Microsoft.Data.Sqlite` assembly and native SQLite versions, assertion outcomes, child process IDs and exit markers, sanitized durable audit entries, and results of four separate-process negative controls. It does not package source payloads or the local capability key.

`DurableWorkStore` owns scoped atomic state, durable owed work, fenced claims, accepted result receipts, continuation receipts, semantic commits, and lost-response replay. Its `ICompositionPolicy` supplies state transitions and explicitly decides what to dispatch, join, or complete. The Sequence witness waits for its predecessor result; the Flowchart witness persists one join continuation after both branches arrive; the partition pipeline stores multiple cursors under one execution and submits each bounded batch through the same work claim, fence, result, and commit path.

The worker executable starts fresh child OS processes. Database barriers hold concurrent workers at known boundaries, so the overlap proof observes both process IDs inside their barriers before release. The independent sink uses a second SQLite database and an idempotency key per scoped activity or source record. One child exits after its sink transaction and before its work-result transaction; the replacement attempt repeats delivery and is deduplicated by the sink. The matrix also covers cancellation/result ordering, atomic initial dispatch, a coordinator exit before notification, result and semantic-commit replay after process exit, conflicting receipts, incompatible handler versions, and stale fencing. Negative controls run mutations that bypass completion ownership, scope filtering, commit fingerprint validation, and commit fencing; each must exit with its named invariant-failure marker.

Lease expiry makes a work item eligible for a newer claim. The old worker may still commit while its owner and fence remain current; after a newer claim advances the fence, the prior lease is rejected. The runner uses a logical expiry intervention, not elapsed-time waiting.

The scope capability is a local HMAC credential for this experiment, not Elsa authentication or a production tenant-security contract. SQLite WAL and local child processes prove cross-process coordination on one machine only. The worker is at least once, so external effects require their own idempotency. There is no broker, network partition, multi-host failure test, Elsa workflow/activity registration, or actual BPMN engine invocation here; those need separate evidence.

## Independent review boundaries

The configured partition limit bounds one fetched batch. A separate optional execution record budget now performs admission in the same immediate SQLite transaction as the bounded source read and accounting insert. With two held three-record batches filling a six-record budget, an additional worker is refused before fetching. After an actual worker kill, its reservation remains conservative until the host confirms process termination and releases that attempt; another worker can then use the capacity. The killed partition is retried and all records finish. This is an execution-scoped record bound, not a byte-memory quota, global tenant quota or distributed failure detector. The budget must admit at least one full pinned batch; validation rejects incompatible configurations in either registration order, including an empty source. The host confirmation is trusted. Lease expiry alone cannot safely reclaim physical buffers from a still-running process; a multi-host design needs a termination/fencing policy or must retain conservative reservations.

A reclaimed worker is fenced at result acceptance. The external sink does not receive that fence, so an old worker can still attempt an effect. This witness relies on deterministic payloads and destination idempotency; mutable or non-idempotent destinations require a stronger connector-specific contract.
