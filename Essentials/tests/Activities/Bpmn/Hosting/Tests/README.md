# Production BPMN hosting qualification

Integration-tier tests launch the built `Elsa.Workbench` as an owned child process. Its configuration is copied to a new temporary content root; Runtime and Design stores are isolated there, and the host chooses its own loopback port. Build-order references locate the actual host assembly even with `--artifacts-path`. Host composition and semantic implementations are not reproduced in the test process.

The native witness publishes and executes through normal authenticated APIs, kills only its owned process, restarts against the same stores and verifies the retained execution/pin. The direct witness opts into BPMN hosting with the built-in single-node mailbox (removing the three clustered placement/transport features from its isolated configuration). It publishes immutable material, starts, observes a human wait, kills/restarts the actual process, verifies the full pin/incarnation/attempt, completes and verifies replay and absence of native workflow/activity/bookmark rows.

The runtime-only witness selects an explicit runtime/authentication feature set, including the existing file-system lock provider needed by Tasks. It configures no Design/publication services or stores. A genuine parallel process has two model human waits, completes one, exits/restarts and completes the surviving wait. One modeled human wait after the join makes the join observable; replay cannot add a second wait, and its completion produces one terminal result. The test inspects all owned SQLite table catalogs for Design storage, preserves pins/incarnation and verifies four input receipts/checkpoints with zero native workflow/activity/bookmark rows. Workbench explicitly discovers both BPMN implementation and API assemblies, so the profile does not rely on legacy authoring features being enabled.

Set `ELSA_BPMN_HOST_EVIDENCE_DIRECTORY` to an output directory to retain public HTTP observations, exact binary product versions/hashes and consistent SQLite backups of the stopped host at wait and completion. The runtime-only parallel evidence is written to its `runtime-only-parallel/` subdirectory. `ELSA_BPMN_HOST_SOURCE_REVISION` additionally checks that the host's product version contains the expected committed source revision. Identity/Design databases and full configuration are not exported; runtime-only evidence includes feature names and SQLite table catalogs. These are same-source process witnesses; changed-binary/legacy recovery, clustered model placement, timer/broadcast/cancellation/settlement, native/ETL bindings and Studio remain separate gates.

```sh
dotnet test Essentials/tests/Activities/Bpmn/Hosting/Tests/Elsa.Activities.Bpmn.Hosting.Tests.csproj -c Release
```

The suite runs hosts sequentially. It never discovers or stops an existing listener and never uses a developer's host or data directory.
