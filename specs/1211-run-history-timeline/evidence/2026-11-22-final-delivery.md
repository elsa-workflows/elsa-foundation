# Timeline and playback qualification

This receipt binds the Timeline workspace (#3849, Spec1211) and recorded playback (#4366, Spec1226) to the coherent implementation PR [#4368](https://github.com/elsa-workflows/elsa-foundation/pull/4368). Runtime and Studio delivery are merged. The [resulting-main receipt](2026-11-22-resulting-main.md) closes T049/T050 and playback T020 with actual main verification, immutable downloads and issue reconciliation; original evidence below retains its own source.

## Runtime, timers and official migrations

| Unit | Merge | Actual resulting-main result |
| --- | --- | --- |
| [#4678](https://github.com/elsa-workflows/elsa-foundation/pull/4678) publication history | `5cdb7343cc5f6b369262d63c79c53a67ae924bb4` | Runtime4,409 / Activities891; zero skips |
| [#4984](https://github.com/elsa-workflows/elsa-foundation/pull/4984) semantic owners | `4c3e9aaaf4e54c4ec19bf04c6a288178cc5e936e` | Runtime4,553 / Activities895; zero skips |
| [#4988](https://github.com/elsa-workflows/elsa-foundation/pull/4988) memory timers | `7577a6e89f2e9343123edecf8c230e1e778f9fb8` | Runtime4,678 / Activities895; zero skips |
| [#5275](https://github.com/elsa-workflows/elsa-foundation/pull/5275) EF timers/migrations | `62092572693fedb738316befbcd410106eaa3e97` | EF2,937 / native providers360, all81 history controls and six fresh/retained upgrade paths; zero skips |
| [#5315](https://github.com/elsa-workflows/elsa-foundation/pull/5315) consumers, Delay, restart | `51c8197a97e31f8d5e4602627953225bacc942fc` | Scheduling141 / Delay54 / Activities895 / Runtime4,680 / API641 / EF2,943;17 distinct restart processes; zero skips |
| [#5364](https://github.com/elsa-workflows/elsa-foundation/pull/5364) native child, compensation, control/retry | `5479729406c068a3591193a69d7f492d76347d58` | Nine whole projects10,389 passed,17 distinct restart processes; zero skips |

The latest nine-project native count is Dispatch405, Compensation453, Runtime4,690, Activities895, API645, EF2,943, Scheduling141, Delay54 and Graph163. The actual [EF migration receipt](https://github.com/elsa-workflows/elsa-foundation/pull/5275#issuecomment-7071954763), [consumer receipt](https://github.com/elsa-workflows/elsa-foundation/pull/5315#issuecomment-7072627149) and [native semantics receipt](https://github.com/elsa-workflows/elsa-foundation/pull/5364#issuecomment-7074061199) retain precise source/test/failure attribution.

All four additive TimerHistoryOwnership migrations were generated through the official incremental path, including Designers/snapshots and predecessor history. MySQL `20261121222327`, PostgreSQL `20261121222329`, SQL Server `20261121222331` and SQLite `20261121222332` are published in main. Tests used owned fresh stores and frozen upgrade copies; user stores were preserved. Timer operational format1.2.0 and history schema1.0.0 are distinct from capture profile5.

New admissions pin `runtime-history-v5`/version5. Retained v3/v4 policies retain their semantics; absent-policy replay remains uncaptured. The native catalog includes actual timer scheduling/dispatch/retirement, child settlement/control, retries and nine compensation decisions. `TimerCancellationRequested` is not advertised without an actual native source. The optional Event match deadline is extracted from the actual suspension and staged through the existing durable CreateBookmark command for initial and replacement waits; no retained registration-family shape or creation guard changes.

## Studio behavior and exact qualification

Timeline is the primary workspace, with pinned Diagram navigation and recorded Previous/Next/Play/Pause/speed/ordinal seeking. Event and activity selection retain exact occurrence identity and back context. Each concrete activity has Journal/Bookmarks/Data views; bookmark history retains original creation, terminal disposition, winner/cause links and observation-time eligibility. Waiting time is disclosed separately from unrecorded active execution time. Partial, uncaptured, denied, unsupported, pending and stale states remain explicit.

At actual product source `c332f177bbf3a90ff24a77fb66f59bb1c836723f`, the whole Workflows suite passed5,058, selected browser regressions66, full28-workspace and browser/live/acceptance TypeScript checks, bundle budgets and lint. Browser skips/retries/flakiness are zero. The final definition chunk is412.92kB below415.10kB. Existing lint warnings remain distinct from errors. Hosting1,228 and CSS/stylelint retain their earlier actual2cb9cbd3/419b sources with unchanged owning inputs; they are not restamped.

The native deadline contribution additionally passed all899 Activities tests (including three persistence modes, default/legacy serialization and real rearm command staging), all4,690 Runtime tests, all624 Architecture tests after actual locked Release/isolated Debug restore graphs,64 whole runner controls and generated-map checks. Native executions retain their actual6e86422a/cb900105/e05cfe29 sources; subsequent changes through3982b1c6 are acceptance-camera-only. One invalid typed-owner restriction and an unversioned registration-field prototype were removed; the final retained model/projector/native creation handler match the qualified baseline. The initial architecture attempt's27 missing-graph failures were corrected by the required restore preparation and a complete624-case rerun.

## Immutable composed evidence

[Download the exact archive](https://api.github.com/repos/elsa-workflows/elsa-foundation/zipball/5e918524717c90df141ce2cd0a856d8c8e0ee23f) or [browse its case index](https://github.com/elsa-workflows/elsa-foundation/blob/5e918524717c90df141ce2cd0a856d8c8e0ee23f/CASE-INDEX.md).

The canonical selection has **54 cases,420 actual PNG/ARIA pairs,48 native oracles and54 traces**, across desktop,320px and390px in light/dark. Four supplemental typography images are isolated fixtures outside that composed count.

| Actual source | Selected cases | Capture pairs | Attribution |
| --- | --- | --- | --- |
| `c332f177bbf3a90ff24a77fb66f59bb1c836723f` |42 |282 | Original nontransport journeys |
| `1086a9dd93a9e0c84c5fb98b80dcf29855ed7e17` |6 |72 | Whole transport retakes after correcting two stale readiness text selectors |
| `3982b1c60a4029985272cf412a959eb20c28bc71` |6 |66 | Real approval restart, passive expiry, native duplicate and stale/recovery diagnostics |

All Studio product source and dependency inputs are unchanged across these heads. The archive preserves all60 actual cases/492 pairs from its three archived campaigns and explicitly supersedes the original six transport visual sets. Separate failed/held preparation attempts are not silently converted to passes. In particular, e05cfe29 passed six functional cases but held15 actual camera frames; the complete3982b1c6 retake corrects resource settlement and actual inspector/Console framing. Every selected actual PNG/ARIA/oracle/trace was independently inspected, including all66 new frames and their exact trace PNG bindings.

The new six diagnostic cases prove a real published User Task before suspension, after reopening the same owned store in a distinct backend process, approval through the ordinary UI, retained consumed history after a second relevant cut, and exact original event/handle/order/occurrence/task-journal identities. A separate pre-admission clock reset per case makes18 replacements total; all19 backend lifetimes plus Studio exit zero and all20 groups disappear. Source/driver/cleanup fences and stopped-store inspection pass with no inspection errors.

Passive expiry leaves the original handle Active but Expired and its activity Suspended, with no end event or wake-up. Two real native lookups, held only at genuine executable SQL reads, produce exactly one Dispatched and one Duplicate result, one consumption and one continuation while a second real wait keeps the actor nonterminal. A genuine renderer read abort retains51 exact ordered IDs and selection before normal Retry restores Live history. No renderer response body, runtime collaborator, original fact or persisted expiry is fabricated.

Archive commit: `5e918524717c90df141ce2cd0a856d8c8e0ee23f`. FILE-MANIFEST SHA256: `c0b4f73433ca462d38897558b145779b46a32726cf28837f07a8d4fb4dfa625e`. The actual GitHub API zipball download is1,764,264,901bytes, ZIP SHA256 `29dca3376014a08a7378409cd3782b012224c951ebd7ef654ca3f37c42299828`; all4,345 extracted files match the pinned manifest and local Git archive. Downloaded-byte independent privacy and actual report/viewer opening receipts are recorded on #4368.

The frozen V9 exporter SHA256 is `29611a1c2029a831e913a1249b93a76e5cd0303950afa3224147190a704b7752`. Credential scrubbing recursively validates and re-encodes the actual HTML report ZIP carriers with shared nested bounds. The earlier V7 bundle was held locally and never uploaded after an encoded-report credential was found. Final prepublication privacy reviewed all4,345 files,121,508 known credential variants,126 ZIPs including six embedded HTML reports, protected identities/timestamps, all image/font bytes and trace/report references. The native databases, sidecars, application binaries and raw credentials are excluded.

## Integration boundary

CI is deliberately skipped under the owner's explicit authorization; no skipped/missing/failed hosted result is called green. Independent source correctness, standards and actual visual acceptance converge on the recorded heads; the final documentation delta receives its own exact-head review. The feature uses owner admin self-merge only after those gates and retains the exact merge and resulting-main receipt.

Issue #3849 was already closed by its owner and is updated without reopening. Playback #4366 and superseded #4389/#4468 close only after the replacement is actually integrated and verified. Richer retained child-source attribution remains the disclosed follow-up [#5365](https://github.com/elsa-workflows/elsa-foundation/issues/5365); permitted unattributed fallback is truthful and does not invent source authority.
