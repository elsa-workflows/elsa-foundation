# Final clean-source request-trace command accounting

**Accepted scope: 9 October 2026.** Request-associated EF commands are reconciled by root and independent review. The [final joined diagnostic account](final-joined-accounting.md) and [packaged-host proof](package-consumer/README.md) separately cover their broader scopes; [program acceptance](final-acceptance.md) records remaining attribution limits. The [separate timing report](final-timing.md) retains its own workload and evidence.

This is a bounded accounting of accepted EF command events for one successful HTTP workflow request and one valid companion REST request per cadence and source revision. Both requests in every case returned HTTP 200, completed with zero incidents, and matched the expected output. Every admitted command start had a matching terminal event.

| Cadence | Request | Before `e6faa568` | After `7357b91e` | Provider split before → after (Npgsql / SQLite) |
|---|---|---:|---:|---|
| Immediate | HTTP | 635 | 635 | 505 / 130 → 505 / 130 |
| Immediate | REST | 556 | 556 | 441 / 115 → 441 / 115 |
| Coalesced (50) | HTTP | 277 | 134 | 195 / 82 → 84 / 50 |
| Coalesced (50) | REST | 101 | 73 | 82 / 19 → 54 / 19 |

The Immediate counts are equal across these two source snapshots. The Coalesced request traces have fewer admitted command events after the integrated source changes. This paired observation does not isolate the effect of any one change. Same-cadence configuration objects were semantically equal across source snapshots and matched the accepted final-main cadence references.

The independent review passed with one nonblocking metadata erratum: all four metadata files retain a legacy top-level `postgresRetained: false`, while each nested PostgreSQL object and capture receipt says the owned container and volume were retained. The nested readbacks show the same container and attached data-volume identities running before stop and exited afterward, plus successful post-capture identity readback. The projection reports that stopped-and-retained state and preserves the metadata discrepancy; it does not describe PostgreSQL as still running.

This evidence covers admitted-trace EF command events only. It does not measure response-phase or full-settlement work, attribute durable checkpoints/outbox ownership, count SQL statements or physical network round trips, measure latency, estimate statistical workload rates, or prove individual-change causality.

Exact source, assembly, configuration, fixture/helper, per-case receipt, probe, archive, reconciliation, and independent-review hashes are in the [sanitized evidence projection](evidence/final-clean-ef-accounting-2026-10-09.json). The projection intentionally omits request and execution identifiers, raw trace contents, SQL, response bodies, cookies, credentials, container names, and local paths.
