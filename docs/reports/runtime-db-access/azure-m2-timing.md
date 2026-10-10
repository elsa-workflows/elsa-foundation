# Bounded Azure M2 timing observations

Root and independent review reconstructed one canonical sequential HttpEndpoint before/after pair. The after revision was faster in this pair. An earlier pair showed the opposite direction and failed comparison gates. These observations do not establish a repeatable general speedup, causal latency attribution, or final T17/T18 integrated acceptance. The separate [query-accounting evidence](azure-settled-accounting.md) and delivered M2 pagination reduction retain their own scope.

The [sanitized timing ledger](evidence/azure-m2-timing-2026-10-06.json) preserves all four measured sequences, source/configuration hashes, recalculated statistics, failed-wrapper status and the review boundaries. Private raw logs, databases and credentials remain outside the repository.

## Workload and controls

Both revisions ran on the same owner-approved Skywalker ISP Azure VM: Standard_D8ds_v5, eight logical processors, 32 GiB RAM, Ubuntu 24.04 x64, .NET SDK 10.0.401/runtime 10.0.12, and PostgreSQL 16.15 pinned by image digest. Each case used a new disposable database. The preserved before revision is `e6faa5689814c33353009b13f5b9e44d0e9982a4`; the M2 after revision is `b49065bbc8141a44e11c6aa01c8c3557d12d51cd`. This pair isolates the pagination correction; it does not include the later T19 scope correction or represent final current-main integration.

The fixture revision is `87698620370ca2b7e9e682b1693c4f3296ec3741`, with timing script SHA-256 `67830a5b25eb5de645c2c5a7e5b03badd5e9a6ab8b29db1af1b22c44b286bed6`. `Http4Sequential` starts with HttpEndpoint, performs a deterministic Set computation, and returns Alice Smith. Readback verified Coalesced cadence, a segment limit of 50 and boundary-level inspection. Persisted diagnostics remained enabled. The valid REST companion has separately accepted accounting evidence; this timing pair measures HTTP only.

Each case had one preflight, 25 warmups and 60 sequential measured requests, with no retry or replacement sample. Timing starts immediately before `HttpClient.SendAsync` and stops after the complete response body is read. Authentication, publication, preflight, warmups, readbacks and terminal settlement are excluded. Two load observations 30 seconds apart had both one-minute and five-minute averages below the eight logical processors. All 86 responses per case matched; all 86 distinct execution readbacks were Completed with zero incidents and the expected settings.

## Canonical pair

The final pair ran **after, then before** on 6 October 2026, with byte-identical `appsettings.json`, `appsettings.Development.json` and `shells.json`. Root independently checked the raw statistics, source/build/fixture hashes, configuration hashes, load observations and correctness readbacks. Per-case cleanup passed; the final runner check found no owned process groups or PostgreSQL containers remaining.

| Revision | Measured requests | Median | p95, nearest rank | Mean | Sample standard deviation | Range |
|---|---:|---:|---:|---:|---:|---:|
| Before | 60 | 221.72305 ms | 458.2877 ms | 301.94209 ms | 121.94193 ms | 192.7224–837.8766 ms |
| After | 60 | 157.74575 ms | 396.1703 ms | 216.94433 ms | 99.38298 ms | 141.9083–579.3692 ms |

Configuration and launch metadata specify Warning for EF Core, database command and transaction logging. Root parsed all 1,240 retained JSON log records from each host and observed no EF record below Warning. This is configuration and observed-output evidence; effective runtime filter introspection was unavailable. The fixture's `hostLoggingLevelVerifiedByScript:false` remains false, and raw audit receipts retain `measurementAccepted:false` and their pending-review labels. The separate reviews accept the bounded observations and arithmetic, without rewriting those receipts or treating the unavailable check as passed.

## Earlier pair and harness qualifications

The first pair ran **before, then after**, with these retained results:

| Revision | Measured requests | Median | p95, nearest rank |
|---|---:|---:|---:|
| Before | 60 | 281.53305 ms | 479.1542 ms |
| After | 60 | 347.3331 ms | 1,251.4613 ms |

All measured responses matched, but the before wrapper failed its post-run output inventory. The original 520 files and 84 directories were unchanged; the runtime added an owner-only `.nuplane` directory, a 523-byte empty-package state file and an empty lock file. A separate review qualified those additions without changing the failed receipt. In addition, configuration byte hashes differed between sides, despite a duplicate-key-rejecting semantic comparison finding identical values. The original byte-identity gate did not pass. This pair remains excluded as a validated comparison and is not a replication of the canonical result.

Before the one final reverse-order pair, a prospective harness change canonicalized JSON serialization without changing configuration values. A narrow post-run check accepted only the three expected runtime-state entries, empty package/request state, exact original file hashes and directory modes; it rejected other changes. Both canonical wrappers passed those checks. Earlier failures remain failed. These were capture-harness corrections, not product performance fixes. No additional run was selected to seek a better result.

The opposite directions and substantial within-case spread constrain interpretation. The final pair provides useful bounded evidence for this single-client fixture, while repeatability, final integrated candidate measurements, concurrency, interruption/replay correctness and complete attribution remain open. There is no 30 ms budget adoption, cold-start claim, or general latency guarantee.

## Retention and teardown

The canonical evidence archive SHA-256 is `58d23dfb06c5bc0c88d8259e6fb5b9cfc323b5bc17e62db2cc969af639478995`. Both earlier archives are retained separately. All evidence was exported and hash-verified before deleting the runner. The seven resources in the dedicated resource group had matching program/owner/nonce tags; the deletion request succeeded and Azure confirmed the group absent on 7 October 2026 at 00:06:28 UTC. This confirms teardown of the owned resource group, not an actual billed-cost total. Existing databases and unrelated resources were outside its scope.
