# ETL migration and provider evidence

T027, 2026-10-20. This slice supplies SQLite design and runtime modules. Both registrations explicitly reject
other providers with `NotSupportedException`; the parent connector program remains broader than this slice.

| Case | Executed evidence | Result / limit |
|---|---|---|
| Design schema creation, publication and restart | `EtlDesignHostTests.Host_auto_migrates_then_reopens_under_validate_and_reads_the_published_artifact` | AutoMigrate in one actual host, stop, then Validate in a separate host/provider; retained draft version, artifact ID, hash and pipeline match. |
| Runtime schema creation and isolation | `EtlRuntimeStoreTests.Runtime_module_migrates_only_its_tables_and_registers_no_design_services` | Actual EF migrator applies SQLite migrations; no pending migrations/model changes; runtime tables present, design tables and design services absent. |
| Runtime accepted state and output reopen | `EtlRuntimeJourneyTests.Two_hosts_execute_a_published_artifact_and_reopen_accepted_output_without_design_services` | Two hosts verify accepted manifest/output and settled cursors; after both stop, a new Validate host reopens the terminal run and owed delivery without design services. Hosts in this test share one OS process; separate process recovery is recorded in the interruption evidence. |
| HTTP host module composition | [Workbench HTTP acceptance](workbench-http.md) | Fresh owned SQLite databases and actual API/worker/native continuation in Sequence and Flowchart. |
| Upgrade from a previous ETL schema | Not applicable | Each ETL module has its initial migration only. No prior ETL schema/release exists in this unit; initial migration/reopen must not be described as a historical schema-upgrade test. |
| SQL Server, PostgreSQL, MySQL and other ETL persistence providers | Not executed; unsupported by this slice | No ETL provider implementation/migration fixture exists for these providers. No container test or provider qualification is claimed. |

The three named ETL tests ran in the restored Release suite of **349 passed, zero failed, zero skipped** after
the scoped publication-approval mutation was removed and the source restored. The log is
`/tmp/elsa-program-20261020-reprioritization/studio-etl-restored-full.log`. Unsupported providers in this table
are not xUnit skipped tests and are not passing checks. Subsequent edits still require final affected validation.

The design and runtime migration histories remain separate. Rolling back application capability requires
retaining compatible readers/workers and accepted state, as described in the [module guide](../../../Extensions/Etl/README.md).
No destructive down-migration, downgrade compatibility or power-loss durability is demonstrated here.
