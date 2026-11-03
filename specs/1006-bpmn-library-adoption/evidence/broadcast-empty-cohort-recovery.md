# Actual recovery of a durably empty recipient decision

Source parent `fe7918bf590914ae748bf7400e96324af07054a0`. The [machine receipt](broadcast-empty-cohort-recovery.json)
binds the [empty host observations](broadcast-empty-cohort-host.json) and
[delayed](broadcast-empty-cohort-before-selection.json)/
[post-cohort](broadcast-empty-cohort-after-cohort.json)/
[partial callback](broadcast-empty-cohort-after-resume.json) regressions. **This is a bounded empty-decision
recovery case, not a competing-proposal, upgraded-legacy-host, Studio or production acceptance claim.**

The first source-built Workbench has a two-catch listener and signal-start binding for a different event.
It publishes the source event and actually commits a **nonnull empty cohort**, with zero starts/resumes
and the provider's version-one retention acceptance. The probe pauses only after the real provider call.
The runner then creates two matching waits and a signal-start binding through normal APIs, after the cut.

The observer reads the actual committed intent and traverses all pages of the checked active binding
index. It proves that the original waits have a different stimulus hash, while both later physical waits
and the later active binding match the source's actual type/hash and were created after the marker.
These are observed persisted facts; authored XML alone is insufficient. The observer only reads these
fields and grants no dispatch authority.

After killing its owned probe process, the runner starts a standard host with no probe feature on the
same private SQLite database. It waits for the real persisted deadline and captures the actual distinct
successor claim, fence **1 → 2**, through the checked provider. Empty delivery can finish quickly; missing
that claim is a failed proof rather than an inferred owner from the final state.

The source becomes Delivered with the identical empty cohort. Both the original unmatched and later
matching listener's complete checked workflow observations remain unchanged: four exact bookmarks,
generations and activity facts stay Suspended at attempt one. The entire three-workflow inventory is
unchanged and neither start artifact creates an execution. The publisher completes with zero incidents.
Thus an installed empty decision remains distinct from an absent decision and is not recomputed when
new matches appear.

| Verification | Passed | Failed | Skipped |
|---|---:|---:|---:|
| Actual empty-cohort crash → standard-host recovery | 1 | 0 | 0 |
| Actual delayed/post-cohort/partial-rearm regressions | 3 | 0 | 0 |
| Explicit observer controls with expected zero starts/resumes | 8 | 0 | 0 |
| Separate probe/standard and checked reader builds | 3 | 0 | — |

All four final runs verify identical source/build manifests and **1,371 current source bindings** each,
before and after execution. The regression receipts reference verified identical manifests in the main
host receipt; content hashes bind them. A preliminary regression exposed PowerShell singleton-array
coercion in the generalized expected-artifact assertion; explicit array initialization fixed it, then
all four final modes passed. The test failure was not a host delivery failure and is not counted as green.

Three independent component source/actual-evidence/portable-receipt audits are clean. Production source,
package dependencies and the frozen authoring fingerprint are unchanged. Only separately owned case
hosts were stopped; the existing user backend/Studio remain running. Whole databases and identity
configuration stay private. [Runner instructions](../../../e2e-tests/bpmn/RecoveryProbe/README.md)
cover the four modes and explicit zero-count observer controls. No full-PR round is consumed.

Complete/empty competing proposals, real renew/stale-owner and partial-start cuts, ACK loss/redrive,
same-artifact shedding, retention/GC/republish races and remaining scopes remain T051 gates. T045
semantics/native-composite, actual historical upgraded-host recovery, final current Studio, legacy
writer/duplicate semantics deletion/guards, full-PR review, green exact-head required checks, protected
merge/resulting-main verification and separate ADR 0063 acceptance stay open. #2898/#2907/program
production acceptance remains open; Orleans/task-runner deferred. The three public-source-digest
GitGuardian incidents still require legitimate resolution; no protection or check is bypassed.
