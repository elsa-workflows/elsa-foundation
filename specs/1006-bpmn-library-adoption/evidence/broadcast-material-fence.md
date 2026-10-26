# T049: actual grants and atomic material commit fences

**T049 remains open, and saved starts/publisher activation remain disabled.** Source parent:
`4aa4d9e8288e3842fbfa59ef8e022173997362d0`. The [machine receipt](broadcast-material-fence.json)
binds implementation, tests, catalog/API and the unchanged dispatcher refusal. This qualifies the material
boundary required by cohort acceptance; it does not install or authorize a cohort.

## Commit boundary

The additive `ExecuteFencedAsync` contract exposes a read-only copy of actual complete root/closure grants
through the existing acquisition, preflight renewal, deadline monitoring and cleanup loop. Legacy managers
refuse without using their single-root fallback. Complete-set paths reject a provider's wrong material/owner
or empty token before callback, and release only their earlier valid grants. An unrelated grant returned by
a faulty provider is never used or released. All original CLR signatures remain.

The grant DTOs are not independent authority. Native memory fencing checks real tokens, material and
trusted current time under the artifact gate, then performs synchronous publication under that same gate.
No awaited work or recipient effect belongs inside that callback. Internal EF fencing requires the caller's
owned acceptance transaction. It reads physical untracked artifact/coordination data, takes sorted actual
coordination revision write locks, checks current grant tokens/incarnations/expiry, takes lease revision
locks, then rereads physical grants and samples trusted time after the last lock. Staged tracker revisions
alone cannot provide that boundary. Caller commit/rollback includes its new root and those revision writes.

An expired guard's acquirer coordinates through the same artifact revision. Source/cohort integration must
hold the transaction through its source CAS and must dispose it inside the managed callback, allowing
renewal/cancellation to settle. The helper grants no permission to skip that integration or authorize an
unrelated provider/context. Helpers remain internal; the new EF friend is only its own tests assembly.

## Verification

| Control | Result |
|---|---:|
| Whole Workflow Runtime | 2866 passed, 0 failed, 0 skipped |
| Whole Runtime EF / listed | 1182 passed / 1182 listed, 0 failures/skips |
| New Runtime cases / manager total | 12 / 41 |
| New actual EF cases | 14 |
| Restored behavioral mutations | 13 detected, no compilation failures |
| Core API additions / removals | 1 / 0 |

Runtime lists 2855 entries. Three unchanged deferred theories expand to 14 cases, so
2855 - 3 + 14 = 2866. A restored TRX audit maps every result to discovery; its three expansion names/counts
are in the machine receipt. Four existing truncated display-name collisions are recorded separately as
unique display names, not incorrectly subtracted from the listing.

Runtime controls validate the immutable actual full union, release after callback, legacy refusal/cancellation,
malformed provider grants without touching unrelated ownership, and native token/expiry/release/guard/
cancellation publication refusal with a valid twin. EF controls use actual native leases and stage a real
source reference in the same transaction. They exercise commit versus rollback, missing owned transaction,
expiry/release/forged token/replacement, orphan incarnation, contradictory live grant plus deletion guard,
requested cancellation, stale tracked lease/coordination positive twins, and deterministic expiry after the
**last** coordination and lease updates. They do not claim an actual cohort or cross-provider peer race.

An initial mutation removing the post-lease-lock time check escaped because the delay fired on the first grant;
a later grant's validation masked the missing final check. The control was strengthened to delay after the last
matching update. The final mutation now fails behaviorally. Additional stale-tracker controls established why
fencing uses physical untracked authority rather than scoped context snapshots. One synchronous default-
cancellation test initially asserted outside its invocation delegate; that fixture was corrected.

The final 13 mutations expose a mutable grant array, fall back to a single root, trust malformed grants,
ignore native memory token/expiry, allow EF without an owned transaction, stage rather than lock coordination,
ignore EF token/incarnation, omit the last time check, ignore a deletion guard, and trust tracked lease or
coordination rows. All final mutations fail without compilation errors. Source bytes were restored before
whole-project runs. Existing warnings remain; frozen authoring SHA256 is unchanged:
`41aa0bb979ea46fc2533b057e3677f408ab54c3411fecfafaa238a2adae38c92`.

## Review and integration

Correctness, standards and acceptance source/control reviews are clean. All three final receipt verifications are clean;
full-PR rounds remain zero.

Next is actual provider cohort installation using these grants/fences, canonical captured-source validation,
recipient/publisher ownership-row fencing, and an immutable versioned retention-acceptance witness. Older
data-only winners must never gain saved-start authority or be restamped from a new proposal. New pending/
checkpoint writers must not mint the verified witness. Unknown commit outcomes must reconcile the exact
persisted winner. Then wire saved-start policy/shedding/per-artifact duplicates with actual provenance.

These T049 gates, T050 publication, T051 actual host/fresh-provider recovery, T045/other stories,
duplicate-semantics deletion/guards, final current host/Studio, full review/required checks, resulting-main
verification and the third ADR0063 PR remain open. #2898/#2907/program production acceptance stay open;
Orleans/task-runner remain deferred. Project #62 continues to mirror issue/PR checkpoints.
