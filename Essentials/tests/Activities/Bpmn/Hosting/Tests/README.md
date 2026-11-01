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

Each upgrade family qualifies its named actual captures. It does not complete T030 or #2898.

Existing Studio has a separate [actual browser upgrade receipt](../../../../../../specs/1017-direct-bpmn-hosting/evidence/studio/README.md): both captured versions/layouts, four completed Published/Test Runs, exact original views/pins/material and frozen authoring across owned restarts. This completes T032 at its executing source; direct-model Studio and the final-source repeat remain open. Reproduce with `tools/bpmn/run_owned_studio_upgrade.py`; verify with `tools/bpmn/verify_studio_upgrade.py --manifest specs/1017-direct-bpmn-hosting/evidence/studio/manifest.json`.

The native-parent upgrade family covers six actually committed fault notifications and twelve retained native fault/cancel terminals. The pending captures restore their original combined Runtime/Design database; the terminal matrix restores its original coupled separate stores. The standard Workbench has no recovery probe. Its normal resumption pump delivers the original callback, and a second process preserves the resulting projection/source without repeating it. Sequence retains its Faulted-workflow/Running-parent disposition; an empty queue is not called parent consumption. Flowchart records its native parent fault/incident, while CompensationScope completes Compensated and resolves the child incident. These are the actual pre-writer projections, not a normalization under #3242.

`LegacyFaultSettlementWorkbenchTests` restores three actual old final-fault checkpoints: an unadmitted native start, an admitted worker already terminal, and an admitted Flowchart worker still waiting on Event after its sibling faults. The waiting-worker profile also selects the ordinary Primitives, Flowchart and checkpoint-persistence features needed to drain native work. Automatic startup discovery queues the existing model cancellation command; the normal resumption pump must settle the original physical worker and result without changing the semantic fault, interpreter state, authority or pins. A second killed/restarted host must retain the completed projections with no queued work or owed sources. No Design store is composed.

```sh
python3 tools/bpmn/qualify-hosting.py resume-upgrade --family native-parents \
  --manifest specs/1017-direct-bpmn-hosting/evidence/baseline/manifest.json \
  --output /tmp/elsa-bpmn-native-parent-upgrade
python3 tools/bpmn/qualify-hosting.py verify-upgrade \
  --manifest /tmp/elsa-bpmn-native-parent-upgrade/manifest.json
```

The called-acknowledgment family restores the actual notice and terminal cuts after the caller committed acceptance but before the sender recorded delivery. The standard pump must acknowledge the original source under a fresh claim without repeating the caller's work. The existing checked EF reader verifies the exact source transport, full pins, committed inbox ordinal and ordinary terminal receipt against consistent stopped backups. Another process preserves the recovered views; the original finish/warning Events complete the pair, and a third process preserves the final views. Timestamp comparisons preserve exact instants while permitting the stored UTC offset rendering.

```sh
python3 tools/bpmn/qualify-hosting.py resume-upgrade --family called-ack \
  --manifest specs/1017-direct-bpmn-hosting/evidence/baseline/manifest.json \
  --output /tmp/elsa-bpmn-called-ack-upgrade
python3 tools/bpmn/qualify-hosting.py verify-upgrade \
  --manifest /tmp/elsa-bpmn-called-ack-upgrade/manifest.json
```

The broadcast family restores four actual cuts: before first selection, after cohort installation, after one callback/re-arm and an installed empty cohort. Its checked provider observations verify exact recipients, physical generations, original pins, completed callbacks, excluded listeners and deterministic keyed starts. A second process preserves the acknowledged source and recipient boundary. Selected finite work completes through normal Events. Original infinite loops and excluded listeners that explicitly return the existing generic HTTP `Ambiguous` result retire through native `CancelWorkflow`; the generic endpoint's ambiguity rule remains unchanged. A third process preserves the final views.

```sh
python3 tools/bpmn/qualify-hosting.py resume-upgrade --family broadcast \
  --manifest specs/1017-direct-bpmn-hosting/evidence/baseline/manifest.json \
  --output /tmp/elsa-bpmn-broadcast-upgrade
python3 tools/bpmn/qualify-hosting.py verify-upgrade \
  --manifest /tmp/elsa-bpmn-broadcast-upgrade/manifest.json
```

The controls family restores its coupled separate Runtime and Design captures. It checks all six escalation policies, both policies for two concurrent non-iteration tokens at the same call binding, and ordinary-task compensation paused in handler B. Existing checked readers verify the original committed inboxes, full pins, exact source transports and native fault/cancellation projections. Normal correlated Events finish the escalation paths, handle the second semantic call token before its sibling and replay compensation B then A. A process restart between sibling/handler continuations must preserve the completed work and remaining work exactly. Final recovery preserves all nineteen observed workflow views. The store's eight other active partial-fork runs have their own earlier qualification.

```sh
python3 tools/bpmn/qualify-hosting.py resume-upgrade --family controls \
  --manifest specs/1017-direct-bpmn-hosting/evidence/baseline/manifest.json \
  --output /tmp/elsa-bpmn-controls-upgrade
python3 tools/bpmn/qualify-hosting.py verify-upgrade \
  --manifest /tmp/elsa-bpmn-controls-upgrade/manifest.json
```

The multi-instance family restores the coupled captures with one completed child per parent. It covers sequential/parallel cardinality3 in Balanced/Durable mode. An exact checked reader verifies loop counters, native iteration/semantic-token identities, original child pins, dispatch sources, committed terminal inboxes and physical waits. Normal Events complete one additional exact child per parent, a restart preserves the last held iteration, and another Event finishes all three children. The original completed child remains unchanged; another process preserves all sixteen final workflow views.

```sh
python3 tools/bpmn/qualify-hosting.py resume-upgrade --family multi-instance \
  --manifest specs/1017-direct-bpmn-hosting/evidence/baseline/manifest.json \
  --output /tmp/elsa-bpmn-multi-instance-upgrade
python3 tools/bpmn/qualify-hosting.py verify-upgrade \
  --manifest /tmp/elsa-bpmn-multi-instance-upgrade/manifest.json
```

Actual Studio behavior remains a separate qualification. These legacy tests do not establish new direct-model MI/settlement/broadcast dispatch or the real native/ETL bridge.

```sh
dotnet test Essentials/tests/Activities/Bpmn/Hosting/Tests/Elsa.Activities.Bpmn.Hosting.Tests.csproj -c Release
```

The suite runs hosts sequentially. It never discovers or stops an existing listener and never uses a developer's host or data directory.

The actual ETL HTTP controller also qualifies [model-originated cancellation of an admitted worker](../../../../../../specs/1017-direct-bpmn-hosting/evidence/pipeline/model-cancellation/README.md): BPMN retains pending settlement, its native worker cancels ETL, exact downstream delivery precedes model settlement and another host stays stable beyond the saved retry deadline. Cancellation before worker admission and pending cancel/result crash cuts remain separate gates.
