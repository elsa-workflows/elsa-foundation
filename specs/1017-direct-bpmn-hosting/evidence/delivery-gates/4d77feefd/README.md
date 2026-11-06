# Whole affected-project gates at4d77feefd

The twelve complete test projects below pass **8,061/8,061 with zero skips**, compiled and executed at **4d77feefdd55132f167c072f2163f10201cad886**. The two historical runners, raw command/results, losslessly compressed TRX, test output and discovery output are retained. [Summary](summary.json) records commands and counters; [discovery reconciliation](discovery-reconciliation.json) accounts for every discovered test and the five theories whose data expands at execution. No missing discovery or extra unexplained execution is counted as coverage.

| Whole project | Passed |
|---|---:|
| BPMN |456|
| BPMN Interchange |130|
| Runtime |3078|
| Runtime EF |1779|
| DispatchWorkflow |380|
| ETL |1009|
| BPMN actual-host qualification |28|
| Runtime native providers |209|
| Publishing |61|
| Cluster readability |117|
| Activity behavioral contracts |220|
| Architecture |594|

Native-provider execution requires `ELSA_RUNTIME_PLACEMENT_EF_REQUIRE_NATIVE_PROVIDERS=1`; all three owned PostgreSQL, SQL Server and MySQL engines ran. External bookmark-provider connection overrides were removed. These provider tests and the separate [model-storage matrix](../../providers/model-storage/README.md) do not substitute for actual non-SQLite upgraded-host recovery.

The locked CI project-graph restore, generated solution-filter check and123-project tier/shard guard also pass; their raw output is retained. Portable ETL passes240/240. Portable BPMN discovers176 and passes173 with **three optional historical live-fixture probes skipped**: they lacked the owned membership database, exact publisher/source identity or actual-host cut marker/checked reader. The frozen partial-fork capture controls were explicitly configured and ran. Those three skips are not reported as passes; retained actual model/upgrade/broadcast receipts qualify their own declared cuts separately.

The four newer paired partial-fork cases and the subsequently expanded hosting suite retain their own source and receipts in [parallel recovery](../../embedding/parallel-native-recovery/README.md). These gates precede final PR review and subsequent main changes; they are not relabelled as current-head CI or broader program acceptance. Each manifest entry hashes the stored bytes. A `.trx.gz` expands to the original TRX hash recorded in the summary.
