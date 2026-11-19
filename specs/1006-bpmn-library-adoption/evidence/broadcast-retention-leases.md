# T049 initial increment: complete root-set leases

This finite increment adds the generic lease capability needed before one cohort installation can
retain several selected executable pins. **T049 remains open.** Source parent:
`1979b2fa052a0413b399f7f3f7de3147ee5521f7`. The [machine receipt](broadcast-retention-leases.json)
binds the five source/test/catalog files. No publisher or cohort installation consumes this path yet.

## Boundary

The additive collection overload on `IWorkflowExecutableRootWriteLeaseManager` snapshots the caller's
root collection, validates every complete immutable pin and its dependency closure, and protects the
distinct union with ordinal leases through one callback. The existing graph loader checks artifact
ID/hash; this path separately checks all pinned identity metadata. It finishes all graph validation
before acquiring any lease. Each root currently loads a finite store snapshot through the existing
graph loader; no throughput improvement is claimed.

Legacy managers refuse the capability before invoking an old overload or callback. Original
single-artifact/identity overload signatures and behavior remain. The collection path revalidates
all acquired tokens and records their renewed deadlines before the callback. An independent timer
checks initial grants and monitors every current deadline while periodic renewal runs. It cancels
the active write even if a provider renewal is blocked; a late response cannot revive expired authority.
Completion samples current deadlines before retiring that guard, including when timer dispatch is late.
Partial acquisition and completion release acquired leases; release errors do not replace the write result.

Renewal exceptions cancel the active collection callback. Unrequested provider cancellation preserves
its original failure rather than being confused with a later normal loop shutdown. Throwing cancellation
registrations are contained and observed through the awaited operation. Independent renewal, expiry,
callback and registration failures retain distinct causes without duplicating guard-originated instances.
The contract documents these outcomes. Invocation lease IDs must remain unique per attempt.

Callbacks must honor cancellation and fence actual durable acceptance. The coordinator cannot roll back
durable effects; eventual cohort installation must handle acknowledgement loss and return its persisted
winner. This capability adds no second retention ledger and does not by itself prove provider integration.

## Evidence

| Control | Result |
|---|---:|
| Whole Workflow Runtime project | 2793 passed, 0 failed, 0 skipped |
| Lease controls included above | 29 cases, including 23 new cases |
| Restored behavioral mutations | 11 detected, no compilation failures |
| Additive Core API / removed entries | 1 / 0 |

Controls use actual in-memory artifact stores and lease/deletion guards. They cover shared dependencies,
duplicate roots, sorted acquisition, hash/full-pin metadata refusal before acquisition, partial cleanup
when deletion wins, acquisition and preflight-renewal latency with positive twins, periodic renewal of
every grant, non-first grant loss, actual paged discovery with caller-array mutation, legacy capability
refusal and requested cancellation before acquisition/fallback.

A real multi-timer fake clock proves cancellation while a renewal provider deliberately ignores its
cancellation token, a valid before-deadline twin, provider IOException and unrequested cancellation,
and callback completion after expiry with timer dispatch deliberately suppressed. Adversarial
cancellation registrations cover both timer and provider paths. A concurrent expiry/throwing-registration/
delayed-provider-outage control verifies all three distinct causes survive the awaited result.

The 11 mutations remove the root union, preflight renewal refusal, legacy refusal, complete-pin metadata
check, active deadline guard, provider-outage cancellation, completion-time deadline sample, timer
registration containment, unrequested-provider-cancellation classification, independent cause combination
and caller-array snapshot. All failed behaviorally and were restored before the final whole run.

An initial caller-array mutation escaped because its first hook watched individual lookup while graph
discovery actually used paged reads. The corrected actual `ListPageAsync` hook detects it. A standalone
preflight-expiry check later became redundant when the independent monitor checked initial grants;
that duplicate was removed and is not counted as a final detected mutation. Disabling the monitor
fails both preflight-latency and blocked-active-write controls. Review findings about blocked renewal,
completion sampling, cancellation containment and concurrent causes were corrected before qualification.

Reproduce with `dotnet test Essentials/tests/Workflows/Runtime/Tests/Elsa.Workflows.Runtime.Tests.csproj`;
filter `WorkflowExecutableRootWriteLeaseManagerTests` for the 29 cases. Existing warnings remain.
Frozen authoring SHA256 stays
`41aa0bb979ea46fc2533b057e3677f408ab54c3411fecfafaa238a2adae38c92`.

## Remaining integration

Correctness, standards and acceptance component/receipt reviews are clean after the corrections.
Full-PR review remains zero rounds.

T049 still needs saved-start authority, owed cohort executable roots and dependency closure, provider
acceptance under leases, source/execution/dedup retention and both deletion/acceptance race orders.
Simply sorting new cross-workflow write locks is insufficient: existing called-workflow deletion reads
another root under its candidate lock and can form a cycle. Integration must reuse safe ownership-revision
CAS coordination or eliminate that conflicting read order, preserving T043 deletion controls. This receipt
does not select or prove that implementation.

T050 publisher activation and T051 actual host/fresh-provider recipients, restart and retention remain
open, together with T045, other story evidence, deletion guards, final current host/Studio, full review,
required checks and resulting-main verification. ADR0063 remains proposed; #2898/#2907 and program
production acceptance stay open. Orleans/task-runner remain deferred.
