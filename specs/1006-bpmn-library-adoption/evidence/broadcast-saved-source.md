# T049: compact saved-source contracts and physical read boundary

This increment qualifies compact provenance and read-only source checks. **Saved starts remain disabled
and T049 remains open.** Source parent: `3bd2920c027e5061222ab27539dea8fb7b2b6596`. The
[machine receipt](broadcast-saved-source.json) binds 14 files, including the unchanged coherent-context
boundary used by the joint owner/fence mutation. No publisher activation or recipient effect is enabled.

## Boundary

`WorkflowExecutableStimulusDeliveryAuthority` carries only source OutboxItemId, canonical intent digest,
actual scope, owner/fence and binding ID. It copies no cohort or stimulus payload. The existing start-authority
union gains a separate member; its original two-argument CLR constructor and legacy wire remain. The new
JSON constructor rejects mixed live/dependency/stimulus provenance. Request and command validators preserve
that separation, including selected publication versus legitimate reference-less data shapes.

The named `IStimulusDeliveryStore.ReadSavedStartSourceAsync` operation refuses unsupported legacy providers;
it cannot fall back to installation or live matching. Both built-ins reread exact physical source, actual
access scope, current publisher facts and accepted captured binding. Shared model validation preserves the
existing install checks and verifies owner/fence/current trusted-clock deadline, digest and scope. A renewed
claim uses its persisted deadline. Missing, unselected or uncaptured data refuses without delivery attempts.

EF rejects caller-owned transactions at entry and return. It rereads the source after the awaited publisher
lookup: a peer's completion/takeover cannot be authorized from an earlier claim snapshot, even while that
snapshot's visibility deadline remains future. In-memory reads likewise recheck current source under the
shared gate/lock. Unavailability retains its cause; requested cancellation propagates.

The returned context is provenance data, not reference eligibility or execution authority. Until T049's
separate reference/lease/retention admission is integrated, `WorkflowStartDispatcher` refuses every new
stimulus-delivery mode before even its early keyed-duplicate lookup. Existing live/dependency starts retain
their behavior. The catalog signature/default implementation were corrected to the actual current port.

## Verification

| Control | Result |
|---|---:|
| Whole Workflow Runtime project | 2842 passed, 0 failed, 0 skipped |
| Whole Runtime EF project | 1130 passed, 0 failed, 0 skipped |
| Focused Core/start controls | 38 passed, including 29 new cases |
| New provider read controls | 30 passed |
| Restored behavioral mutations | 10 detected, no compilation failures |
| Core API additions / removals | 14 / 0 |

Core controls cover compact strict wire/shape, preserved union constructor and exact legacy wire, mixed
provenance, request selection contradictions, carried command attribution, legacy-provider default refusal,
requested cancellation, and missing admission before duplicate return. An initial direct required-getter
serialization attempt failed existing legacy controls; the final strict private wire converter preserves
those existing paths. No such failing source is retained.

Provider controls use actual memory and EF stores with constructed source/cohort facts. They cover missing
source, digest, scope, binding, unselected/uncaptured state and contradictory publisher; owner/fence loss;
expiry without takeover and renewal beyond the original deadline; local caller transaction refusal with
committed positive twin; IO/unrequested provider cancellation and requested cancellation. A real EF command
interceptor lets a peer retry/reclaim during publisher lookup while the old deadline is still future. The
final physical reread rejects that old authority. These are provider controls, not workflow host recovery.

The ten mutations allow mixed authority, allow live selection with saved data, bypass the pre-duplicate
refusal, ignore current expiry, erase owner/fence at both the source and coherent-context boundaries, omit
presented digest, omit current publisher scope, omit EF's final source recheck, alter legacy union wire and
allow uncaptured bindings. Every mutation failed behaviorally; source bytes were restored before final
whole-project runs. The owner/fence mutation deliberately removes both independent guards.

Reproduce through the existing Runtime and Runtime EF projects; target `StimulusDeliveryStartAuthorityTests`,
`WorkflowExecutableStartContractTests`, `Saved_stimulus_authority_without_admission` and
`Saved_start_source_read` for focused controls. Existing warnings remain. Frozen authoring SHA256 stays
`41aa0bb979ea46fc2533b057e3677f408ab54c3411fecfafaa238a2adae38c92`.

## Review and remaining gates

Correctness, standards and acceptance component/receipt reviews are clean. Full-PR review remains
at zero rounds.

T049 must still reuse ordinary source resolution, validate captured reference eligibility, implement actual
saved-start policy/admission with per-artifact keys and actual provenance, and integrate acceptance under
root leases with executable-GC/source/execution/dedup retention and race controls. This data/read increment
does not close any of those gates.

T050 activation and T051 actual host/fresh-provider recipients/retry/restart/retention remain open, together
with T045, other story evidence, duplicate-semantics deletion/guards, final current host/Studio, full review/
required checks, resulting-main verification and the third ADR0063 PR. #2898/#2907 and program production
acceptance stay open. Orleans/task-runner remain deferred.
