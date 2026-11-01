# Actual native parent delivery and terminal upgrade

Executing host/runner source: `02602bad34602496314b5cbf7a84a6b2f549124a`. Inputs are the actual [pre-writer matrix](../../baseline/manifest.json): six pending fault-notification stores from `b7cfe195d6792367b3df31ff11eb291210d255c5`, plus coupled terminal-history stores from `438001406d980051584234e5d656eb8e6a67f47f`. Original capture bytes remain unchanged. The standard production Workbench contains no recovery probe; its real resumption pump delivers the committed work.

Six pending cases cover both Balanced and Durable modes. Each preserves the full executable pin, original BPMN/native invocation IDs, committed callback intent, applied migration history and native material. The resulting projection matches the actual pre-writer behavior:

| Parent | Workflow | Native parent | Incident projection |
|---|---|---|---|
| Sequence | Faulted | Running | Original child incident remains blocking; parent consumption is not claimed |
| Flowchart | Faulted | Faulted | Original child incident plus the native parent incident |
| CompensationScope | Completed | Completed / Compensated | Original child incident becomes resolved |

Four parent outcomes are applied; the two Sequence cases retain their existing terminal disposition. A Delivered enqueue source and empty queue are not alone treated as parent consumption. All six complete without running the forbidden successor. A second process preserves the full execution view and the original source's exact ContentJson hash/revision, with no repeat. [Pending process observations](legacy-pending-parent/process.json) include the original and delivered sources, physical snapshots, process identities and source-bound binary hashes.

The [terminal-history observations](legacy-native-terminals/process.json) separately preserve all twelve Fault/Cancel cases across all three composites and both persistence modes. They retain the original pin, invocation identities, outcomes and incident projection; another process preserves the complete recovered views. All 84 original executable payloads/incarnations remain unchanged in that Runtime store. This is terminal-history preservation, not active token recovery.

Storage topology is part of the input: the six pending captures use their combined Runtime/Design store, while terminal history restores its coupled separate Runtime and Design captures. Reconciliation stays enabled. Eight stopped upgraded stores pass integrity/hash checks; Identity/configuration are excluded. The [frozen-authoring receipt](frozen-contract.json) verifies the two actual recommended published BPMN descriptors in the upgraded Design store.

[Qualification](qualification.json): whole hosting suite 6/6; capture/upgrade verifier checks 23/23 with the actual baseline supplied; zero skips. Six native evidence controls reject false parent-consumption claims, an undelivered physical source, changed committed intent, a foreign full pin, relabeled Compensated and revived terminal history, even after the altered bundles are rehashed. Both native tests also pass through their normal copied-fixture path.

```sh
python3 tools/bpmn/qualify-hosting.py resume-upgrade --family native-parents \
  --manifest specs/1017-direct-bpmn-hosting/evidence/baseline/manifest.json \
  --output /tmp/elsa-bpmn-native-parent-upgrade
python3 tools/bpmn/qualify-hosting.py verify-upgrade \
  --manifest specs/1017-direct-bpmn-hosting/evidence/upgrade/native-parents/manifest.json
```

This advances T030 for these native families. It does not resolve #3242 or establish new direct-model settlement, real native/ETL bindings, other-provider upgrade or clustered placement. Called-workflow acknowledgments, broadcasts, active multi-instance/compensation/control recovery and actual Studio remain open. T030, #2898, #2907 and the program stay partial; full-PR review rounds remain zero.
