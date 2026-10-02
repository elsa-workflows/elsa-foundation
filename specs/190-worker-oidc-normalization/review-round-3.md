# PR2315 review round 3 and failed-gate investigation

Reviewed head: `0229f9a269a49d3518b562d10b034d8d0c8c6408`. Actual Copilot review **5393079263** reports no findings on that exact commit. Actual CodeQuality review **5393118711** adds the repeated exception-filter suggestion below. A clean review is not a passing CI gate.

## Comment ledger

- [4166781354](https://github.com/elsa-workflows/elsa-foundation/pull/2315#discussion_r4166781354) — **explain**. AuthenticationFailed first observes the actual request cancellation token inside its catch. A filter that skips the catch when that token is canceled would rethrow an arbitrary callback exception instead of the observed cancellation, changing the required cancellation/sanitization contract. [Direct reply 4166804508](https://github.com/elsa-workflows/elsa-foundation/pull/2315#discussion_r4166804508) precedes resolution. No source change was required; the unchanged-head review remains applicable.

## Actual failed Linux gate

[CI37019576266](https://github.com/elsa-workflows/elsa-foundation/actions/runs/37019576266) failed Build & test at the reviewed head:

- Runtime: 1932 passed, 1 failed, 0 skipped. The existing feature tracing test's process-global ActivityListener captured three same-name-source spans rather than its own one. Independent source inspection identifies parallel RuntimeEngineTracingTests as another same-name emitter. Filter this test's observations by its unique execution ID, retaining the real DI/source/operation assertions. An unrelated same-name-source emission in the existing test makes isolation explicit.
- Runtime EF: 834 passed, 1 failed, 0 skipped. The Worker actor received HTTP401 as expected but recorded one claim-mapping read during an adverse scope probe where zero is required. The original assertion does not name the probe. Do not infer refusal-before-lookup from the status code or classify this as a flake.
- Existing EF container aggregate passed. Downstream Architecture guards and Core-only jobs were skipped, not passed. Other reviewed-head hosted gates were terminal and successful; actual review/check outcomes remain recorded on the public PR.

Focused Debug Worker actor2/0/0 and the full local Release Runtime EF835/0/0 did not reproduce the Linux mapping-read failure. The full Release result belongs to the reviewed source before the new main integration. Logs: `/tmp/runtime-2308-ci-failure-actor-repro.log`, `/tmp/runtime-2308-ci-failure-runtime-ef-release.log`; Linux failure log `/tmp/runtime-2308-pr0229-build-linux.log`. These local passes do not resolve the failed Linux acceptance claim.

## Combined-source investigation boundary

Normal integration `874b640b9` includes main `d33b092cdaf92b193cb749c2e0ae0429f5be26f4` (peer PR2319 sensitive/credential inputs). Only generated maps conflicted; root regenerated all maps, reviewed the findings, and explicitly staged the changed map files including manifest. No OIDC production behavior changed in this integration.

Safe instrumentation of the existing child/accessor/interceptor and parent assertions is intended to distinguish a missing probe context, an unexpected mapping read, and an unrelated operation. Receipts may contain only fixed context categories; raw SQL, arbitrary header values, claims, connection values and credentials remain excluded. The zero-read/zero-runtime-effects acceptance remains strict. Instrumentation alone is not a causal repair.

T027/T028, resulting-main qualification, and Worker profile publication remain open. Issue #2308 remains the sole active delivery item. The full program is not complete.

## Executed investigation and restored-source controls

All processes below are terminal and reaped. Root reviewed both bounded delegated patches; no new test project, EF suite, provider matrix or cadence was introduced.

| Control | Actual result and scope |
|---|---|
| Combined main + tracing correction, macOS arm64 SDK10.0.300 Release | Runtime1949 passed,0 failed,0 skipped; `/tmp/runtime-2308-review3-runtime-release.log` |
| Disable only listener execution-ID filter | Compiled; existing feature test failed Assert.Single with2 spans (1 failed,0 passed,0 skipped). `/tmp/runtime-2308-review3-tracing-mutation.log` |
| Restore listener bytes and rebuild tracing controls | 6 passed,0 failed,0 skipped; original/restored SHA256 `fa686eb2e45f197c87ab30730d80d84f7c198e9e6a62ed7019973451e78b31d0`; `/tmp/runtime-2308-review3-tracing-mutation-receipt.json` and `...-tracing-restored.log` |
| Isolated Linux arm64 Ubuntu24.04 SDK10.0.401 Release, new accessor/counter observations | Worker actor2 passed,0 failed,0 skipped. Each adverse probe was observed and had zero real mapping reads/runtime effects. `/tmp/runtime-2308-review3-linux-actor.log` |
| Combined maps and solution filters | Both exit0; `/tmp/runtime-2308-review3-maps-check.log`, `/tmp/runtime-2308-review3-filters-check.log` |

The Linux run used a separate working-source snapshot rooted at merge `874b640b93a5d745dcb89aea672fc337d565980d`, plus the reviewed test corrections. All8,317 tracked working files were copied without Mac bin/obj assets; projection SHA256 `90421cf1145d7d39961aacfb3a4e43d437cf23edb969d5a379124a578c7e2b70` is recorded in `/tmp/runtime-2308-linux-snapshot.json`. The container used the machine-wide build-slot wrapper and a3CPU cap; it did not bypass the shared queue. It matches the failing CI's SDK10.0.401 and Linux OS, but is arm64 rather than CI's x64 and is a focused actor run rather than the full Linux835 context.

The original mapping-read failure remains unexplained, not classified as a flake or claimed fixed. New receipts are diagnostics: snapshots themselves add `no-request`, and mapping-read categories identify the last accessor observation visible in that AsyncLocal flow, not an independently proven request identity. The real SELECT counter remains authoritative. The stronger probe/zero assertions require a fresh exact-head full Linux CI gate. Main `d33b092` CI37023262854 is terminal green; that baseline result does not prove this PR's next head.
