# Runtime concurrency evidence — in progress

The operation boundary is implemented at production source `eaeec794dbc9bca3fc1a88eda356ca7279adc884`. [Receipts](operation-boundary.json) keep exact counters, raw-result hashes and observed native error codes. The native fixture was executed as a working-tree test file against that production source and is committed unchanged with these receipts; its hash is recorded. This is not published-head CI or full feature acceptance.

- Initial save-conflict regression: **1 failed**, then **1 passed** with retry. The failure is an intentionally injected concurrency failure on a real SQLite transaction.
- Admission failure/ownership controls: **7 passed**, zero failed/skipped.
- Existing SQLite store suite plus those controls: **266 passed**, zero failed/skipped on committed production source eaeec794d.
- Required-provider focused verification: **27 passed**, zero failed/skipped on real PostgreSQL 16, SQL Server 2022 and MySQL 8.4. Of these, **six are genuine concurrent admission races**: matching pins yield Applied+Replay; different pins yield Applied+Conflict; one run and no terminal result remain.
- Observed native failures: PostgreSQL **23505**, SQL Server **1205**, MySQL **1213**. No injected provider exception is used in those six races.

The server activation guard remains. Remaining evidence includes native retry removal/restoration, other transition races, immutable inputs across a retry, multi-save preparation rollback, set-based compaction rollback, the bounded correctness workload, actual both-store Workbench journeys, whole projects, API/binary/computed-version checks, independent implementation review, required CI and resulting-main verification. Tasks T006 onward remain unchecked.

A first native-fixture build stopped on two wrong helper/property names before test execution. They were corrected before the 27-case run; the diagnostic is distinct from a failing behavioral run. Existing package audit warnings are tracked separately in #3707. No suppression, migration change or literal version is added.
