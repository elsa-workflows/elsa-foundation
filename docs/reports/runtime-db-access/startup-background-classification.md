# Retained startup and background log classification (T02)

This classifies the seven accepted [before timing windows](before-timing.md). It adds no host run, command reduction or performance result. The [evidence digest](evidence/startup-background-classification-2026-10-06.json) pins every retained log and metadata file, the source packet and the two source files. Root and independent review reconciled the counts and timestamp boundaries.

Every window contains three warnings and four EF command errors before its `measuredBatchStart`: **49 pre-measurement records across seven windows**. These records are separate from the workload's measured concurrency failures. Their timestamp establishes only that they precede measurement, not their exact startup subphase.

| Retained category | Per window | Classification and limit |
|---|---:|---|
| Workbench OpenIddict pruning, Warning/EventId 0 | 2 | Each warning contains the unsupported `ExecuteDeleteAsync` pattern, `InvalidOperationException` and an `InMemory` token. It has no request/trace/span scope. Effective provider options were not captured, so provider identity is not established by this token alone. |
| EF migrations, Warning/EventId 20410 | 1 | Pre-measurement migration warning; no additional causal claim. |
| EF database command, Error/EventId 20102 | 4 | `SELECT` reads referencing `__EFMigrationsHistory`, with empty logging scopes. The retained structured rows supply no exception type or provider code. Whether these are expected absent-history probes or provider failures remains unresolved. |

The pinned Foundation source shows the pruning background service yields, then immediately invokes token and authorization pruning. It catches and logs each operation's failure independently and continues on its configured interval. The vendor registration selects InMemory for the development/demo branch and SQLite otherwise, with the Workbench vendor DbContext supplying the EF store. This is source behavior, not a captured effective-provider selection for the retained runs. The source hashes are recorded in the digest; the indexed graph was navigation only and the exact isolated source was verified.

The failed HTTP 4/concurrency 4 Coalesced window separately contains **23 measured-phase EF Query/EventId 10100 error records**, with an `InvalidOperationException` token. They remain part of the retained failed correctness control. Do not attribute them to pruning, combine them with the migration-history reads, or infer that the separately reviewed Coalesced lifetime correction repaired each one.

No command text, parameters, raw IDs, credentials, endpoints or exception messages are published. No retained diagnostics database copy was read. This narrows the startup classification while preserving unresolved command causes, request attribution, effective provider selection and final concurrency acceptance.
