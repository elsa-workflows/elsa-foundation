# Actual partially completed multi-instance upgrade

Executing host and checked-reader source: `645d59aa6990df001d218af055154092c841cc41`. The actual [pre-writer matrix](../../baseline/manifest.json) supplies coupled separate Runtime/Design captures from `438001406d980051584234e5d656eb8e6a67f47f`. Each of four real cardinality3 loops has already completed one child. The changed production Workbench restores both stores, applies additive migrations and continues through normal correlated Event commands. Original captures remain unchanged; no positive state or clock is edited, and the standard host contains no recovery probe.

| Policy | Modes | Original completed / live calls | After one more Event | Final |
|---|---|---|---|---|
| Sequential | Balanced, Durable | 1 completed; iteration2 held, nextIndex2 | 2 completed; original-pinned iteration3 held, nextIndex3 | 3 completed; no live semantic loop |
| Parallel | Balanced, Durable | 1 completed; two original iterations held, nextIndex3 | 2 completed; one exact original iteration held | 3 completed; no live semantic loop |

The [losslessly compressed process receipt](legacy-multi-instance/process.json.gz) retains actual HTTP views, four checked provider observations, four host process identities, source-bound binary hashes and five upgraded whole-store snapshots. Both compressed and decoded receipt hashes are verified. The checked reader correlates loop counters, original loop ID, semantic token/native iteration IDs, native call/dispatch/child ancestry, full parent/child executable pins, immutable keyed-start and terminal sources, committed inbox acceptance and exact physical wait generations. Sender acknowledgment alone is insufficient.

A restart after the first additional child preserves completed work and the remaining iteration exactly. The original pre-upgrade completed child remains unchanged throughout. Eight normal correlated Event commands finish the remaining children across all four cases. All twelve native iterations and all sixteen final parent/child workflows complete without incidents or waits, and another process preserves the final views. All92 original executable payloads/incarnations and applied migration history remain unchanged. Both actual published BPMN descriptors match the [frozen authoring contract](frozen-contract.json).

[Qualification](qualification.json) records exact-source whole hosting10/10 and capture/upgrade verifier54/54 with the original baseline supplied. Eight new rehashed copied-evidence controls refuse forged loop progress, a borrowed semantic token, a foreign pin for a new child, replay of the original completed child, changed physical loop state, absent physical terminal acceptance, changed held-wait generation and an Event targeting a completed iteration. Physical verification uses actual native table layouts.

```sh
python3 tools/bpmn/qualify-hosting.py resume-upgrade --family multi-instance \
  --manifest specs/1017-direct-bpmn-hosting/evidence/baseline/manifest.json \
  --output /tmp/elsa-bpmn-multi-instance-upgrade
python3 tools/bpmn/qualify-hosting.py verify-upgrade \
  --manifest specs/1017-direct-bpmn-hosting/evidence/upgrade/multi-instance/manifest.json
```

The input folder name also mentions Studio; this receipt qualifies the four saved MI cases only. Actual Studio open/edit/export/publish/run/inspect remains a separate required journey before native/ETL integration. No new direct-model MI, RunBpmn/native worker settlement, native/ETL bridge, other-provider or clustered acceptance is inferred. T030, #2898, #2907 and Program#2892 remain partial; full-PR review rounds remain zero.
