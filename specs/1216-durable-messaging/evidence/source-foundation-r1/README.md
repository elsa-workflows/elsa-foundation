# New Message foundation — first Azure qualification attempt

**FAILED / INCOMPLETE.** Frozen source was `215227b46c6fa51c80c2246b056aa7ff2fc554eb`,
tree `b2e96656c40f004b44ad2f4cc7313e8823b8f814`, including main `fd0fdb21`.
This is separate from the completed [older source qualification](../t007-forwarding-r3/README.md).

The actual Runtime focused TRX records 86 distinct passed executions: 29 accepted-header
cases, 17 real native publication-interest integration cases and 40 progress/work codec
cases. All other counters are zero and there is no RunInfo Error.

The next EF stage failed compilation with exactly one CS1061 diagnostic:
`EfRuntimeDeliverySourceStore.cs` called `GetEnlistedTransaction` without importing
`System.Transactions`, the owning namespace shown by the installed EF 10.0.10 XML documentation.
No EF TRX was produced; the 37 EF cases, deliberate repair mutation, restoration and
complete Runtime/EF projects were not executed in this attempt. The subsequent source
correction adds that import and preserves the transaction ownership check.

The original 2,639,067-byte archive is retained unchanged. Root and independent audit
verify all 91 regular members and 90 per-file manifest hashes, the 20 frozen source files,
clean source and own-unit/cgroup/process cleanup. Primary remains **FAILED**; source and
cleanup are separately **VERIFIED**. The source checkout, caches, fixtures and other work
were retained. No Message producer, registration or successful physical capability was enabled.

This new source cohort used 312.05 seconds, conservatively charged as 313 of 1,800 seconds.
The earlier source cohort's separate 1,652.89-second debit and failed R1/R2 history are
preserved. A causal continuation must debit this attempt, bind the corrected exact source,
and finish the remaining stages within the existing limits; it must not rerun the unchanged
86-case cohort as new evidence. There is no full feature, provider, UI or full-PR acceptance claim.
