# Witness review checkpoint — 17 October 2026

This is an intermediate research checkpoint for Program #2892, not final architecture acceptance. Experiments are isolated from production code. Source hashes in the evidence bind the runs to their exact local source; the repository HEAD recorded during these initial runs precedes their first commit.

## Shared work (#2896)

Root independently built the experiment with zero warnings/errors and ran `dotnet run --project tools/experiments/UnifiedExecution.WorkProof --no-build -- proof`: **95 assertions, 35 child process identities, four detected negative controls**. A delegated read-only review inspected the concrete assertions and boundaries. The initial optional concurrency test failed because it expected identical commit replay to lose a revision race. Root corrected it to race distinct result commits at one state revision: one is rejected, its result remains pending, and reapplication preserves both results. The final run passed.

The experiment proves explicit composition-owned completion, durable owed dispatch and receipts, bounded batch work, two partition workers under one pipeline execution, independent destination deduplication, stale result rejection, and substantive commit replay. Sequence and Flowchart retain different composition policies. No actor is created per activity or partition.

Review identified limitations that remain material:

- The source record limit bounds each batch, not aggregate admission under arbitrary extra workers. The variable/schema were renamed from `maxInFlight` to `maxBatchRecords` to avoid overstating that contract. **The stronger aggregate buffering/backpressure acceptance in the issue matrix remains open.**
- Diagnostic in-flight measurements can survive a worker death before `EndBatch`; they are not recoverable admission reservations.
- Stale workers cannot commit a new result after reclaim, but the independent sink does not consume the ownership fence. Deterministic-key/payload deduplication protects this witness. Connectors with mutable or non-idempotent effects need their own safety contract.
- The HMAC scope is an experimental credential, not production authentication. SQLite on one host is not multi-host storage evidence.

These are recorded gaps, not grounds to silently narrow the program acceptance.

## BPMN (#2898)

Root independently ran `dotnet run --project tools/experiments/UnifiedExecution.BpmnProof --no-build -- verify`; it passed against clean upstream source `be9c6eaa963b846104d5aebf38d337bafab08710`. The delegated implementation also ran the upstream semantics suite (171/171); root's independent verification here covers the host witness, not a second execution of that upstream suite.

The host atomically persists interpreter state, handle ledger and owed command batches. Fresh processes resume a fork/join; timer/human races and incidents follow actual interpreter dispositions. Changed definitions and foreign/stale handles are rejected. A persisted Sequence-style parent runs the same BPMN child and advances to another child; its first-child trace matches direct hosting.

This corrects the earlier comparison with a nested BPMN subprocess, which did not establish native-style embedding. The new adapter is still a custom experimental host, not current Elsa Sequence integration. Modeled message delivery is validated against a live listener; it is not arbitrary token alteration. Upstream build reports NU1902 for pinned `Microsoft.Build.Tasks.Git` 8.0.0. No conformance, broker, worker lease or external-effect guarantee follows from this witness.

## Next acceptance work

Complete the aggregate work-bound witness, actual provider comparison over the same definitions, native agent composition and separately packaged independent model. Then independently review the combined recommendation before producing the interactive HTML presentation. The Orleans preflight only proves same-key activation recovery after a silo kill; it is not yet the full provider comparison.

## Microsoft Agent Framework package probe (#2913)

Root independently ran the Release executable for the real `Microsoft.Agents.AI.Workflows` 1.25.0 package; all three probes passed. Two receiver handlers overlap while each receiver processes its queue serially. A fast branch cannot start its next round before a slow branch completes. A partial fan-in checkpoint retains two distinct A messages across restore into fresh executor objects; B then releases all three arrivals, without reexecuting A. These are multiple messages from one source, not evidence of deduplication of the same message identity.

The assembly reports informational version `1.25.0+95511ff025d81cc546c44e653d4be65de5bd7f35`. This is stronger package provenance than the nuspec alone; the separately inspected source revision `44fe223...` is still not equated with the package. Package-download manifests pin lengths and SHA512 hashes. The checkpoint store is in-memory and restore remains in-process. Native composition over durable work is a separate pending witness; no LLM or production Elsa agent integration is proved here.
