# Orleans execution-provider witness

This isolated experiment compares Orleans 10.4.0 with direct built-in `CompositionCoordinator` hosting for complete durable executions. Both run the same WorkProof `SequencePolicy` and `FlowchartPolicy`, `DurableWorkStore`, and independent worker CLI. One Orleans grain owns one complete composition; the built-in case calls that same coordinator directly. Both write owed invocations through the shared store, and separate OS worker processes claim and complete them. The runner compares final policy state, status, pinned work, continuations, effect keys, and effect payloads. No invocation actor or second workflow engine is added.

The execution grain key uses versioned, length-prefixed UTF-8 string fields for the required policy capability, tenant, persistence scope, authority, and execution ID. It excludes the proof credential. The provider boundary also rejects CR/LF in scope fields before calling WorkProof's experiment-only newline-delimited HMAC authorizer. A deliberately constructed pair of differently segmented scopes has the same legacy HMAC input; the provider rejects it before durable access. A custom Orleans placement director filters `IPlacementContext.GetCompatibleSilos` using capability metadata published through Orleans membership. It maintains no separate liveness list. The proof also rejects an unsupported capability, aliased grain keys, a different tenant, and a different execution before durable ownership can change.

## Reproduce

From the repository root:

```bash
dotnet build tools/experiments/UnifiedExecution.ProviderProbe/UnifiedExecution.ProviderProbe.csproj
python3 tools/experiments/UnifiedExecution.ProviderProbe/preflight.py
python3 tools/experiments/UnifiedExecution.ProviderProbe/provider-proof.py
```

`preflight.py` preserves the earlier Ping-only bootstrap check on ports 19111–19212. `provider-proof.py` uses separate ports 19311–19412 and runs Sequence and Flowchart in isolated two-silo clusters. For each policy it first runs the direct built-in reference and then the same execution ID and pinned input through Orleans. Flowchart workers in both providers enter a durable SQLite process barrier together; the runner observes both distinct live worker PIDs before release. It then kills the Orleans owner silo with SIGKILL and calls the same execution grain key through the survivor. The proof verifies a new owner and incremented fence, rejection of the old owner, rejection of identity aliases and ambiguous scopes without changing the fence or state revision, recovery of pending receipts, component-owned completion, and equal final semantic outcomes including destination effect payloads. It records exit status for each managed silo and worker process, archives logs, and removes its temporary database/log directory after the archive is written. Source hashes cover both the provider harness and the shared WorkProof source/project inputs. Each silo pair advertises both required capabilities so a compatible survivor remains after failover. The bounded proof does not simulate a network partition or loss of the local development membership seed.

## Operational and dependency tradeoffs

The built-in path calls `CompositionCoordinator` directly and avoids Orleans packages, cluster membership, activation lifecycle, and capability placement setup. A host using that path must provide its own execution routing and failover. Orleans adds those framework and operational surfaces, while offering a cluster-addressed execution grain, capability-aware placement, and activation recovery. This is a qualitative dependency and operational comparison from the witness setup, not a performance or cost benchmark.

The latest run is retained in [provider-proof.json](evidence/provider-proof.json), with hashed raw process logs in [provider-proof-logs.zip](evidence/provider-proof-logs.zip). [sha256.json](evidence/sha256.json) includes hashes for the provider proof and its log archive. The original Ping evidence remains in [preflight.json](evidence/preflight.json) and [preflight-logs.zip](evidence/preflight-logs.zip).

This is local experimental evidence, not production provider conformance, a production tenant identity provider, or a claim of exactly-once external effects. The proof uses SQLite WAL on one machine and a fixed experiment-only HMAC key. WorkProof's separate sink and replay evidence describe idempotency at that sink boundary; Orleans placement and activation recovery do not provide an atomic transaction with arbitrary external systems.
