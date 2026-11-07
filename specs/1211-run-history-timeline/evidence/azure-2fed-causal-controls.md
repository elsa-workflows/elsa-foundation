# Source-bound backend controls on the dedicated Azure worker

Status: **all nine frozen backend controls completed and assessed by the author;
whole-suite, UX, review and merge acceptance remain open**.

This follows [implementation progress](implementation-progress.md) and the
[hosted CI follow-up](ci-6e-follow-up.md). The source under qualification is
published commit `2fedfa596cc039a5e0c840eba4457bf149409dee`, tree
`d7db77f51d28be5a4d940c65a12a21f7b88fb186`. Later evidence edits do not change
the source identity of these executions.

## Environment and method

The dedicated `elsa-dev-06` lane was actually admitted, then independently
checked for its assigned VM UUID, machine/boot identity, UID/GID 1001, tool
hashes and .NET SDK 10.0.300. Source preparation produced the exact clean
detached checkout, verified all candidate and historical implementation hashes,
and copied only the three original manifest-pinned canonical fixture archives.
No project binaries, `obj`, dependency trees, or `node_modules` were borrowed.
Environment/source preparation passed separately from product tests.

Each cohort runs in its own systemd unit with an actual 2-CPU quota and
7,516,192,768-byte memory limit. The original controls retain their 600-second
operational ceiling, 540-second helper work budget and controlled closeout
reserves. Canonical hydration, locked restore, normal builds and analyzers remain
enabled. The original accepted C06 process/source-restoration functions are
unchanged. An unexpected result stops progression; no product retry is inferred.

The author reads the complete transferred archive, original TRX `ErrorInfo`,
same-case/name multiplicities, actual implementation DLL inequalities, source
manifests, restoration receipts and process-closeout records before proceeding.
The archive transfer is checked against its actual byte count and SHA-256.
Negative/restored source manifests are additionally checked against the declared
historical/candidate Git objects, including both Git blob IDs and SHA-256.

## Actual results

“Negative” means the declared historical implementation with the same current
tests; “restored” means the frozen candidate implementation. Counts are
failed/passed/skipped. These are nine paired controls covering 49 case variants,
not one surrounding test suite.

| Cohort | Negative | Restored | Assessment |
|---|---|---|---|
| Compound Runtime | 8/2/0 | 0/10/0 | Full raw evidence assessed; intended primary-error, aggregate, invocation-diagnostics and stale-control failures. Runtime DLL changed. |
| Compound publication | 3/0/0 | 0/3/0 | Full raw evidence assessed; intended permanent/hard-source classification and capacity-shed aggregate failures. Primitives DLL changed. |
| Compound dispatch | 3/0/0 | 0/3/0 | Full raw evidence assessed; intended child permanent classification and parent current-invocation diagnostic-boundary failures. Dispatch Runtime DLL changed. |
| Bulk Runtime | 3/3/0 | 0/6/0 | Full raw evidence assessed; actual candidate/source-fact caps reject valid native cleanup/drain in the old implementation. Three disabled-history cases preserve their prior pass. Core and Runtime DLLs changed. |
| Bulk EF | 1/1/0 | 0/2/0 | Full raw evidence assessed; actual 4,097-bookmark native cancellation hits the old candidate cap, while disabled capture preserves its prior pass. Restored SQLite cleanup, one physical commit and fresh-context replay pass; Core, Runtime and EF DLLs changed. |
| Owner child | 2/8/0 | 0/10/0 | Full raw evidence assessed; the old attempt/disposition integrity cases incorrectly report unavailable. Eight I/O, timeout and cancellation cases preserve prior passes. Dispatch Runtime DLL changed. |
| Owner publication | 2/8/0 | 0/10/0 | Full raw evidence assessed; old attempt/disposition integrity cases incorrectly report unavailable. Eight other cases preserve prior passes. Primitives DLL changed. |
| Fault attribution | 2/0/0 | 0/2/0 | Full raw evidence assessed; old delivery-fault observation loses the exact bookmark identity after activation failure or lost acknowledgement. Runtime DLL changed. This proves emitted attribution, not persistence/query or callback effects. |
| JSON integrity cause | 3/0/0 | 0/3/0 | Full raw evidence assessed; old head/record/purge integrity errors lose their original JSON cause. Restored cause/path/stack, safe-summary and cancellation checks pass. EF DLL changed. |

All nine assessed controls have exact negative/restored test-name inventories, no
skips, all four phases sampled-quiescent, clean/restored candidate source,
terminal outer/helper processes, and no incomplete/deferred-restoration flags.
The earlier finite ownership-sampling limitation remains: this is not proof of
complete process ancestry between samples. The original helper records actual
negative/restored DLL hashes but does not separately retain negative DLL bytes;
that limitation is preserved without replaying a completed control.

Total: **27 intended failures and 22 preserved passes in the historical variants;
49 restored passes; zero skips**. The 98 original TRX rows cover the same 49
paired case identities. Readback verifies 88 phase-source bindings against their
original Git objects and 12 required implementation DLL inequalities. All original
bounds are preserved and no product retry or duplicate local UI64 run occurred.

## Retained original evidence

Local complete evidence is under `/tmp/1211-azure-2fed/results/`. Original
Run Command API captures, submitted scripts, transfer/source preparation and
readback records are under `/tmp/1211-azure-2fed/`. Guest originals remain under
the Timeline namespace and original external `/tmp` cohort paths.

| Complete archive | Bytes | SHA-256 |
|---|---:|---|
| `compound-runtime-01.tar.gz` | 28,678 | `dcd8bac7963f2fcced15fe552c8a7481a705522950b8c509efb25668ca7cd03b` |
| `compound-publication-01.tar.gz` | 21,405 | `da6fe5625f7d484b689a01d4114fbcccf459dc517c8c32f8db4d8d18dac6449a` |
| `compound-dispatch-01.tar.gz` | 21,978 | `94103224bd30a10b207440e4d696e186965edbeafba73e4ee5aa3128e7bbcd76` |
| `bulk-runtime-01.tar.gz` | 26,685 | `11aa898e0ac0e9d57bf52054b97428c601b655e81ae0292f584e1ab08ac3ad33` |
| `bulk-ef-01.tar.gz` | 32,365 | `c7f065fa901482579ac1f0d4b55675c77ff0d4b30de5e1e3bc09e17c2044b709` |
| `owner-child-01.tar.gz` | 22,163 | `f6be8b2bb9cfae8154d961d524ff86dd2fd3e6c47b8aab216fa690a439c48d70` |
| `owner-publication-01.tar.gz` | 22,430 | `0598db83d10691ecfed37f851c7d0b1733f662d086235e0474708c4d66bc6ebe` |
| `fault-attribution-01.tar.gz` | 23,193 | `3d405bb6b1af52720b30689693f5ee0ba28048b63056858a3b2c52cd7989e2ce` |
| `json-integrity-cause-01.tar.gz` | 27,356 | `bbb2c9b1085ed7e32c0326dea9815686c639587c619a9cd60357a844c148196f` |

Completed command metadata can be removed after retaining its original API
capture and assessed full archive, to remain below Azure's resource limit. Only
this chat's matching, terminal command resources are removed; guest evidence,
source, product data and other owners' resources are retained. Subsequent actual
inventories distinguish completed removals from accepted deletions still pending.

## Following Studio work

After the frozen sequence completed, canonical PR #3938's commit
`75f5894cbf56236b1ef26b754a78d5abff307ad9` was faithfully cherry-picked as
`1dd53166720761a8d8e68a835bc8d042b41c46b2`. It adds one ordinary **Collapse
inspector panel** action to the short-editor QA setup before the unchanged node
click. Removing that line reconstructs the previous current file byte for byte,
including the Timeline-specific Diagram/data-tab checks. Its resulting blob is
`439a98344e77d5b0235e4560e50b635cf2a3212a`.

The canonical source's independent matched old/new browser-pair acceptance is
limited to that setup correction. It does not qualify this integrated head,
repair production minimap geometry, or replace normal-host/whole-suite/current
UX acceptance. Viewport, click, chooser/overflow/Iteration 10/accessibility
assertions and budgets are preserved. The integrated change is not yet executed.

The separate shuffled Fields75 Workbench failure still needs a source-causal
diagnosis. Its original seed-1 log and source are retained; seeds 11 and 29 were
not run in that original job. No delay, skip, timeout increase or weakened
assertion is treated as a fix. Normal current-head CI and the remaining Studio,
provider, independent acceptance and merge gates stay open.
