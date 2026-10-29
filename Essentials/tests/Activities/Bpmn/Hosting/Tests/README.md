# Production BPMN hosting qualification

Integration-tier tests launch the built `Elsa.Workbench` as an owned child process. Its configuration is copied to a new temporary content root; Runtime and Design stores are isolated there, and the host chooses its own loopback port. Build-order references locate the actual host assembly even with `--artifacts-path`. Host composition and semantic implementations are not reproduced in the test process.

The native witness publishes and executes through normal authenticated APIs, kills only its owned process, restarts against the same stores and verifies the retained execution/pin. The direct witness opts into BPMN hosting with the built-in single-node mailbox (removing the three clustered placement/transport features from its isolated configuration). It publishes immutable material, starts, observes a human wait, kills/restarts the actual process, verifies the full pin/incarnation/attempt, completes and verifies replay and absence of native workflow/activity/bookmark rows.

The runtime-only witness selects an explicit runtime/authentication feature set, including the existing file-system lock provider needed by Tasks. It configures no Design/publication services or stores. A genuine parallel process has two model human waits, completes one, exits/restarts and completes the surviving wait. One modeled human wait after the join makes the join observable; replay cannot add a second wait, and its completion produces one terminal result. The test inspects all owned SQLite table catalogs for Design storage, preserves pins/incarnation and verifies four input receipts/checkpoints with zero native workflow/activity/bookmark rows. Workbench explicitly discovers both BPMN implementation and API assemblies, so the profile does not rely on legacy authoring features being enabled.

Set `ELSA_BPMN_HOST_EVIDENCE_DIRECTORY` to an output directory to retain public HTTP observations, exact binary product versions/hashes and consistent SQLite backups of the stopped host at wait and completion. The runtime-only parallel evidence is written to its `runtime-only-parallel/` subdirectory. `ELSA_BPMN_HOST_SOURCE_REVISION` additionally checks that the host's product version contains the expected committed source revision. The direct witnesses export Runtime only, without Identity/Design databases or full configuration; runtime-only evidence includes feature names and SQLite table catalogs. These are same-source process witnesses. Clustered model placement, timer/broadcast/cancellation/settlement, native/ETL bindings and Studio remain separate gates.

The legacy upgrade witness copies the actual pre-writer partial-fork Runtime and Design snapshots into a new owned content root before starting the changed production host. It verifies additive migration history and all original executable bytes/incarnations, recovers the eight retained execution/wait identities, and sends the remaining normal Event stimulus. Legacy roots and Sequence/Flowchart/CompensationScope must each finish once in Balanced and Durable mode. It checks that the original captures remain unchanged. This witness also exports the stopped upgraded Design store for the frozen published-authoring verifier; Identity/configuration remain excluded.

The repeatable runner checks the original capture bundle before and after recovery, requires committed source, rebuilds/runs the actual host test, checks the upgraded frozen authoring contract and writes a separately verifiable evidence manifest. Use a new output directory for each run:

```sh
python3 tools/bpmn/qualify-hosting.py resume-upgrade \
  --manifest specs/1017-direct-bpmn-hosting/evidence/baseline/partial-forks/manifest.json \
  --output /tmp/elsa-bpmn-legacy-upgrade
python3 tools/bpmn/qualify-hosting.py verify-upgrade \
  --manifest /tmp/elsa-bpmn-legacy-upgrade/manifest.json
```

This qualifies only the actual partial-fork capture family. Pending parent callbacks, broadcast cohorts, multi-instance/compensation/called-control captures, other providers and actual Studio behavior require their own changed-host verification. It does not complete T030 or #2898.

The native-parent upgrade family covers six actually committed fault notifications and twelve retained native fault/cancel terminals. The pending captures restore their original combined Runtime/Design database; the terminal matrix restores its original coupled separate stores. The standard Workbench has no recovery probe. Its normal resumption pump delivers the original callback, and a second process preserves the resulting projection/source without repeating it. Sequence retains its Faulted-workflow/Running-parent disposition; an empty queue is not called parent consumption. Flowchart records its native parent fault/incident, while CompensationScope completes Compensated and resolves the child incident. These are the actual pre-writer projections, not a normalization under #3242.

```sh
python3 tools/bpmn/qualify-hosting.py resume-upgrade --family native-parents \
  --manifest specs/1017-direct-bpmn-hosting/evidence/baseline/manifest.json \
  --output /tmp/elsa-bpmn-native-parent-upgrade
python3 tools/bpmn/qualify-hosting.py verify-upgrade \
  --manifest /tmp/elsa-bpmn-native-parent-upgrade/manifest.json
```

Called-workflow acknowledgments, broadcast cohorts, multi-instance/compensation/control recovery and actual Studio behavior remain separate qualification families. These native tests do not establish direct-model settlement or the real native/ETL bridge.

```sh
dotnet test Essentials/tests/Activities/Bpmn/Hosting/Tests/Elsa.Activities.Bpmn.Hosting.Tests.csproj -c Release
```

The suite runs hosts sequentially. It never discovers or stops an existing listener and never uses a developer's host or data directory.
