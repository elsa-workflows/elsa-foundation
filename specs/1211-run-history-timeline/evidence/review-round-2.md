# Review round 2 — two actionable source findings; acceptance pending

Reviewed published head `25e652d9df0f250d905f43ff939581e5069a7636`, tree `0ede5e045914da958666e909fa4673dc2978a1f6`, against main `1ac995bbec4551abebdf4189ce05a3f9a7fa8c83`. This continues R1; the six-round cap is not reset. No clean acceptance or merge authorization is asserted.

## Independent correctness

The supervisor commissioned a fresh reviewer without author conversation/conclusions or another lens's output. Its [unaltered report](https://github.com/elsa-workflows/elsa-foundation/pull/3850#issuecomment-6655344397) is a source assessment; the reviewer ran no tests/builds/hosts and inspected no rendered evidence.

**P2: recheck `Eligible` before enqueueing a saved resume.** In BookmarkResumeDispatcher's final revalidation after actor activation, the preparation wrapper is tested for null even though PrepareResumeAsync now always returns a wrapper and represents ineligibility as `Eligible == null`. Retirement, rearming or a terminal owner during activation therefore still enqueues the old command and can record Dispatched. The later exact-consumption fence is intact; no unauthorized consumption is alleged. The finding is retained and independently verified against the actual producer contract and caller.

A scoped additive R3 correction checks Eligible and retains the already-known recipient attribution on DeliveryUnmatched. Eight deterministic activation callback cases cover eligible/removed/rearmed/terminal owner with history on/off, exact command, zero losing enqueue calls, preserved generation, receipt/disposition causation and no synthetic consumption. These changes are currently **uncompiled/unexecuted**, so expected causal counts are not passing evidence.

## Other lenses and prior findings

The independent [standards report](https://github.com/elsa-workflows/elsa-foundation/pull/3850#issuecomment-6655401187) is complete, likewise without author conversation, other lens output, test/build/host execution or rendered inspection. It retained a separate **P2: preserve the original malformed-JSON cause** in EfRuntimeHistorySupport.Checked. The current RuntimeHistoryIntegrityException discards the JsonException path/position/stack. The frozen R3 correction adds an exception overload, preserves the original cause at the interpretation boundary and retains the original raw syntax regression alongside typed JSON path/position/stack assertions in the same three operation cases. The existing two-argument constructor, hard integrity classification, sanitized API responses and retry policy remain. One exact public API overload is accounted for. This is source work, not an executed pass.

Independent current-head acceptance and downloadable rendered UX remain pending. The earlier scoped cancellation correctness receipt remains applicable only to its unchanged five blobs, as recorded in R1.

R1 P2 provider-failure classification is implemented in `032042d6a504dc9667f8baf5e122944fae61599d`. Actual old two-file EF source yielded19 predicted failures/5 preserved passes; restored25e passed24/24, then surrounding RunHistory64/64, zero skips. The separate causal receipt retains actual old/new DLL hashes and identical test-source identity. R1 P3 public Studio controls is implemented in the same commit, but its Minimal browser/native tests and current frontend qualification have not run. No source review substitutes for those checks.

The 25e whole Compensation431 and Scheduling27 passed with zero skips. Exact commands, manifests, original failures and remaining gates are in [implementation-progress.md](implementation-progress.md). No finding was dropped. A fresh round on the resulting exact candidate is required after fixes and affected qualification.
