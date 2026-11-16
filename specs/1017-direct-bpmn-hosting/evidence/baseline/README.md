# Actual pre-writer inputs

Gate A / T002 is **captured and verified for the listed writer families**. This bundle retains actual execution stores before affected production writer changes. It does not establish direct hosting or upgraded-host recovery.

The [manifest](manifest.json) uses relative asset paths, content hashes and executing source identities. Whole stores are retained without runtime-row edits; `.sqlite.gz` files decompress to the recorded database hash. Original absolute paths inside observations are provenance only, not replay instructions. Build the reader and hosts in the current workspace and supply their actual paths.

| Input | Actual baseline observation |
|---|---|
| `partial-forks/` | Eight held partial forks: legacy root and three native parents × Balanced/Durable. Branch A completed, B held. Original capture remains unchanged. |
| `legacy-controls/` | Six called-control escalation cases, two concurrent same-binding call cases and one reverse compensation case held in Handler B; checked provider observations and 20 controls. Concurrent tokens are not multi-instance iterations. |
| `multi-instance-and-studio/` | Four genuine cardinality3 multi-instance calls, sequential/parallel × Balanced/Durable. One child completed, remaining work held. Independent physical correlation records loops, iteration IDs, dispatch→child links and bookmarks. The same consistent whole-store cut contains the saved Studio layout and second test run. |
| `native-parents/` | Twelve actual legacy fault/cancel projections across Sequence/Flowchart/CompensationScope and both modes. Existing composite-specific fault behavior is preserved; #3242 remains undecided. |
| `broadcast/` | Whole stores before recipient selection, after empty/populated first selection, and after one recipient accepted/re-armed before source acknowledgement. Ordinary-host restart controls passed. Identity/secrets stores were isolated before fixture authoring. |
| `called-ack/` | Actual nonterminal notice and terminal child result after parent inbox acceptance but before source acknowledgement; 18 reader controls passed. These are distinct from native-composite callback consumption. |
| `native-parent-pending/` | Six actual fault/incident checkpoints before native parent consumption: all three parents × Balanced/Durable. Exact Pending source, complete pin, faulted BPMN child, blocking incident and unadvanced native parent pass checked-provider and independent physical observations. Six invalid reader controls and a separately corrupted callback-pin input are refused. |
| `studio/` | Actual browser exports, publication, saved five-node layout, screenshots and two completed transient test runs on the owned existing Studio/host. The semantic artifact remains `artifact-cfadffc29096`; authoring fingerprint remains frozen. These test runs are not published production executions or new direct-model Studio acceptance. |

Successful native completion can fold into parent progression even in Durable mode. The partial-fork/wait stores are genuine earlier durable success precursors; they are not labeled already-persisted completed-BPMN callbacks. The separate fault/incident boundary supplies actual pending native-parent callback wire coverage in both modes. Independent review identified the initially missing MI and pending-parent inputs; the additional actual captures resolve those coverage gaps. Diagnostic-scope misses and initial mixed authentication/runtime-store attempts are excluded from positive evidence.

```sh
dotnet build tools/bpmn/DeliveryRecoveryRead/DeliveryRecoveryRead.csproj -c Release
python3 e2e-tests/bpmn/verify_hosting_baseline.py \
  --manifest specs/1017-direct-bpmn-hosting/evidence/baseline/manifest.json \
  --reader-assembly /absolute/current/build/DeliveryRecoveryRead.dll
```

The verifier checks 104 asset hashes, 22 decompressed/closed whole stores, absence of authentication/secrets tables, 15 actual provider observations, the native terminal and MI matrices and the source-bound Studio/frozen-authoring observations. Upgraded-host replay must later use copies of these inputs, fresh processes and normal continuation commands; successful baseline-to-baseline restart is not that acceptance.

If later work touches another existing writer family, extend the map and capture its actual inputs before that edit.
