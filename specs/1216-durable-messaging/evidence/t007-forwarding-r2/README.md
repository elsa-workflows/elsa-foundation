# Typed forwarding, exact discovery context and additive migration evidence

This is component evidence for T007/T009, not composed Message acceptance or delivery.
The source baseline is `5ed3d1e7865c6752d89b48071c5c58fb40466c4f`, tree
`08068863a07c252f5a0182a92b26450b50e05d61`. Generation adds exactly twelve files,
producing tree `cd15cc7ad5f18f54c5647a4c9ef6b01d67f30685`.

The original Azure archive is retained byte for byte. Its 117 regular files include
116 individually hashed manifest entries, raw TRX/logs, the immutable runner packet,
original systemd observations, generated migration patch and independent source/cleanup checks.
The preceding [R1 failed harness result](../t007-forwarding-r1/README.md) remains failed.
R2 consumes that exact original result and does not repeat its 55 cases or eight-case mutant.

| Check | Observed result |
|---|---|
| R1 focused processor/context cases | 55 passed |
| R1 deliberate dropped-finalization mutant | Exactly one expected `Assert.Same` failure; seven passed |
| R2 restored forwarding cases | 8 passed |
| R2 actual SQLite full-context discovery cases | 16 passed |
| R2 complete Runtime project | 3,595 passed |
| Additive migration generation | Four new migration/designer pairs and four snapshots; no historical migration changes |
| Rebuilt Release tooling and four offline pending-model checks | Passed for SQLite, SQL Server, PostgreSQL and MySQL |
| R2 complete Runtime EF project | **FAILED / INCOMPLETE:** stage timeout 124 after 480 seconds; no TRX or test summary |

Every green TRX has zero failed, skipped, error, timeout, aborted or pending cases.
The EF timeout log contains the test banner but no result summary; it proves neither
1,909 executed cases nor a product assertion failure. The original R2 primary verdict
is **FAILED**, with source and cleanup separately **VERIFIED**. Source verification
reconstructs the generated tree and compares all twelve generated files and 25 frozen
source files. No generic source mirror of C2 ingress or native outbox was introduced.

The dedicated `elsa-dev-18` process ran under the recorded codex UID, two-CPU quota,
6,500 MiB memory cap and zero swap. R2 used 955.55 seconds; the conservative cumulative
active debit, including R1, is 1,161.55 of 3,600 seconds. Its own unit was stopped;
the cgroup and owned processes were absent. Source, cache and unrelated work were retained.

These are offline model checks and SQLite component tests. They do not qualify live
PostgreSQL, SQL Server or MySQL engines, migration upgrade preservation, full physical
participation, producer acceptance, recipient delivery, registration or product journeys.
The subsequent local header and read/claim/renew drafts are outside this frozen source.

The later [R3 continuation](../t007-forwarding-r3/README.md) finishes only the unfinished
EF check and retains this original failed/incomplete result unchanged.
