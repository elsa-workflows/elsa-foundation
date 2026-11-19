# Actual called-control and compensation upgrade

Executing host and checked-reader source: `0595851685e3c0ce3321d1a884ad18dbd8f00215`. The [pre-writer matrix](../../baseline/manifest.json) supplies coupled separate Runtime and Design stores from `438001406d980051584234e5d656eb8e6a67f47f`. Both are restored before the standard production Workbench starts. Original capture bytes remain unchanged. No recovery probe or runtime row/clock edits occur in the positive journey.

Nine original scenarios cover nineteen workflow executions:

| Family | Actual cases | Required result |
|---|---:|---|
| Escalations | 6 | Running, terminal and signal-with-authored-failure, each interrupting/noninterrupting; retain truthful child fault/cancel status, exact inbox/transport and original incident ownership |
| Concurrent same-binding calls | 2 | Select the second live semantic token, never an iteration/store-order substitute; handle that child while leaving its sibling held, restart, then finish only the remaining sibling |
| Ordinary-task compensation | 1 | Retain the original handler-B identity and reverse registration order; complete B, restart with A still held, complete A, observe settled replay, then finish the original hold |

The existing checked EF readers verify all three families at recovered, intermediate, resumed, settled and finished cuts. The losslessly compressed [process receipt](legacy-controls/process.json.gz) binds actual HTTP views and fifteen checked observations to five consistent Runtime backups, the final upgraded Design snapshot, four real host processes and source-bound binary hashes. Decode the receipt with any standard gzip reader; the verifier reads it directly. Backups use SQLite's consistent backup operation after all real sources acknowledge, including when the owned host remains running. The shared comparison preserves exact native timestamp instants across offset rendering, including incident-resolution appliedAt.

Original pins and all 72 executable payloads/incarnations remain unchanged; original migration history is retained. Both actual published BPMN descriptors match the [frozen authoring contract](frozen-contract.json). Full initial API and checked domain views match the original capture. Another process between continuations preserves both the held sibling/handler and already completed work exactly. Final API views remain unchanged after another process.

The final nineteen observed executions are thirteen Completed, three Cancelled and three Faulted, with their three original authored-failure incidents preserved. Those fault outcomes are required native behavior. All nineteen have no remaining physical waits. The whole captured store also contains terminal histories and eight active partial-fork runs already [qualified separately](../partial-forks/README.md); this receipt does not claim to finish those unrelated instances again. No new direct-model terminal settlement or native/ETL bridge is inferred.

[Qualification](qualification.json): exact-source whole hosting9/9; capture/upgrade verifier46/46 with the actual baseline supplied, zero skips; maps and tiers pass. Nine new rehashed copied-evidence controls refuse wrong sibling retirement, reversed compensation order, changed physical semantic state, missing physical inbox acceptance, altered immutable intent, relabeled authored failure, unchecked consumption and changed held-wait generation and a mismatched decoded receipt hash. Physical verification covers outcomes/full pins, private state, committed inbox ordinals, exact source payloads and raw bookmark identities/generations.

```sh
python3 tools/bpmn/qualify-hosting.py resume-upgrade --family controls \
  --manifest specs/1017-direct-bpmn-hosting/evidence/baseline/manifest.json \
  --output /tmp/elsa-bpmn-controls-upgrade
python3 tools/bpmn/qualify-hosting.py verify-upgrade \
  --manifest specs/1017-direct-bpmn-hosting/evidence/upgrade/controls/manifest.json
```

This advances T030 for these legacy native/BpmnProcess control and compensation states. Active multi-instance and actual Studio qualification remain before pipeline integration. Real direct-model/RunBpmn worker settlement, native/ETL bindings, remaining operational races, other providers and clustered placement remain open. T030, #2898, #2907 and Program#2892 stay partial; full-PR review rounds remain zero.
