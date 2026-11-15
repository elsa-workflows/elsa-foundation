# Data and state

No persisted entity, schema version, key, authored artifact, native address, batch identity or wire discriminator changes in this slice. Existing spec 1203 migrations are reused.

A logical database operation captures immutable inputs once, opens one transaction per attempt, reads current authority/accounting, commits one established outcome, then returns. A retryable failed attempt disposes its transaction and abandons tracked changes before a new attempt re-reads state. An exhausted or nonretryable infrastructure failure yields the documented ETL exception; it creates no successful terminal result.

`EtlStoreException` is an in-process failure contract, never persisted: Operation, nullable Scope/RunId and original InnerException. Cross-scope operations leave unknown identities null. See [storage failure contract](contracts/storage-failure.md).

Physical connector effects stay outside the database unit. No cross-engine data migration or stronger physical-effect guarantee is introduced.

Preparation's multiple saves and compaction's set-based deletes/updates remain inside one attempt transaction. Controls must prove rollback of earlier successful writes, including retained page/request predecessors and their accounting, not only an empty change tracker.
