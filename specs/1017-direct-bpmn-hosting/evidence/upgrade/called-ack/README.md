# Actual called-workflow acknowledgment upgrade

Executing host and checked-reader source: `4bba8f266327369608733fb4ee484e07a33bbf6a`. The actual [pre-writer matrix](../../baseline/manifest.json) supplies notice and terminal cuts written by `bf83b23b4b3987d6ac3b7c31df98ee9133156763`. Each original combined Runtime/Design store is copied into an owned production Workbench. Original capture bytes remain unchanged; no stored row or deadline is edited.

Both cuts occur after the real caller committed acceptance and before the sender recorded delivery. The standard resumption pump reclaims each original source with fencing token 2 and records Delivered. The caller/callee API views preserve all values, comparing timestamp instants exactly across local-offset versus UTC rendering. The checked EF reader separately proves the original transport/full address, pinned material, actual committed inbox acceptance and ordinary terminal receipt. An acknowledged dispatch alone is insufficient.

| Original cut | Inbox after recovery | Normal finish commands | Final inbox |
|---|---|---|---|
| Notice accepted, sender pre-ACK | Next ordinal 1; nonterminal | Original child finish Event, then caller warning Event | Next ordinal 2; terminal consumed |
| Terminal accepted, sender pre-ACK | Next ordinal 2; terminal consumed | Original caller warning Event | Next ordinal 2; terminal consumed |

The original warning invocation completes and the normal successor occurs once. A second actual process preserves recovered views before continuation; a third preserves completed views. Both pairs finish without incidents. The two original executable payloads/incarnations and applied migration history remain intact in each store. [Process observations](legacy-called-ack/process.json) bind actual HTTP observations and checked provider proofs to four consistent upgraded snapshots, six process identities and binary hashes/product versions. Identity databases/configuration are excluded. The [frozen-authoring receipt](frozen-contract.json) verifies both actual published BPMN descriptors in the upgraded combined store.

[Qualification](qualification.json) records the exact-source whole-host suite and verifier controls. Six copied-evidence controls refuse a physically undelivered source, lost physical inbox acceptance, repeated successor, foreign full pin, unchecked acceptance and changed original transport, even after affected assets/receipts are rehashed. An initial comparison failure was traced to equivalent timestamp offsets; the comparison now permits only exact timestamp-instant equality for named API time fields.

```sh
python3 tools/bpmn/qualify-hosting.py resume-upgrade --family called-ack \
  --manifest specs/1017-direct-bpmn-hosting/evidence/baseline/manifest.json \
  --output /tmp/elsa-bpmn-called-ack-upgrade
python3 tools/bpmn/qualify-hosting.py verify-upgrade \
  --manifest specs/1017-direct-bpmn-hosting/evidence/upgrade/called-ack/manifest.json
```

This qualifies legacy native/BpmnProcess called-control acknowledgments, advancing T030 for those cuts. New direct-model/RunBpmn settlement and real native/ETL bindings remain unproved. Broadcasts, active multi-instance/compensation/control recovery, actual Studio and other-provider/clustered qualification remain open. T030, #2898, #2907 and Program #2892 stay partial; full-PR review rounds remain zero.
