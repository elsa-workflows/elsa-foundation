# T048: exact durable source and canonical flush context

T048 completes the generic source-boundary increment of [FR-015](../contracts/broadcast-recipient-snapshot.md)
on existing draft #3338. Source parent: `7ee54692d7644ad8441859210ec60102efbc4f40`.
The [machine receipt](broadcast-source-context.json) binds source, tests, catalog and contract hashes.
This component does not activate recipient-cohort publication.

## Implemented boundary

- Additive keyed contribution metadata declares required durable source. The new handler context
  signature is explicit; its intent-only default refuses. A legacy aggregate's context default also
  refuses. Ordinary intent signatures, registrations, constructors and handler cardinality remain.
  The processor independently honors contribution metadata and persisted scope/protocol witnesses,
  including old-constructor combinations; a replacement cannot silently erase those requirements.
- The original committer records actual persistence scope for required sources. A generic scope-only
  row declares no recipient protocol and grants no cohort authority. EF requires family `2.0.0` for
  this witness; ordinary held-v1 writes retain their prior format.
- Named `FindDurableAsync` bypasses coalescing overlays. Its legacy interface default refuses.
  The processor validates full physical source/immutable intent, committed access scope and current
  claim owner/fence/deadline using provider time. Renewed claims use the current persisted deadline.
  Lost/expired claims produce no handler effect or stale acknowledgement. Missing/contradictory
  context refuses before handler, failure projection, acknowledgement or attempt increment.
- Unknown legacy in-memory scope ownership and caller-owned local/enlisted/ambient EF transactions
  refuse physical-source authority. Unavailability retains its original cause; requested cancellation
  propagates. The source context is never independent domain authorization.
- Required/scoped sources are excluded before the overlay claim limit. Scheduler continuations retain
  their existing delivery priority. A real public quiescence flush creates the canonical durable row;
  repeated cap folds preserve an existing physical identity, peer-installed cohort and peer delivery.
- An uncertain quiescence flush retains its commit ID, timestamp and envelope within the drain scope.
  A later checkpoint decision in that active session refuses until resolution. Retrying after commit
  acknowledgement loss and peer delivery reuses the original source rather than minting a second one.
  Successful resolution clears the guard and permits the later decision.

## Verification

| Control | Result |
|---|---:|
| Whole Workflow Runtime project | 2766 passed, 0 failed, 0 skipped |
| Whole Runtime EF project | 1089 passed, 0 failed, 0 skipped |
| Source-boundary Runtime controls included above | 22 passed |
| Actual EF public-flush controls, ordinary and lost acknowledgement | 2 passed |
| Restored behavioral mutations | 14 detected; no compilation failures |
| Additive Core API entries / removed entries | 13 / 0 |

Reproduce the whole runs with `dotnet test` against
`Essentials/tests/Workflows/Runtime/Tests/Elsa.Workflows.Runtime.Tests.csproj` and
`Essentials/tests/Workflows/Runtime/Persistence/EntityFrameworkCore/Tests/Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests.csproj`.
Target `RuntimePostCommitDurableSourceTests`, `StimulusDeliveryStoreTests` and
`StimulusDeliverySchemaGateTests` for the focused source/provider cases. Existing repository warnings
remain; these results do not claim warning-free builds.

Controls include missing/contradictory physical rows, original scope capture, legacy constructor and
replacement fallbacks, registered requirements, unavailable causes, expiry and actual takeover,
continuation-before-required-source selection with limit one, real canonical quiescence identity,
cap-fold preservation of an authoritative empty cohort and peer Delivered status, transaction
rollback then committed retry, and generic scope-only gate finalization on the same host.

Both memory and actual EF flush controls lose acknowledgement after the durable commit, allow a raw
peer processor to deliver the canonical row, advance time, retry the same scope and assert one source,
one handler invocation and identical flush commit IDs. The EF fixture uses an in-memory queue with no
scheduled work. It proves physical EF checkpoint/outbox context; it does not prove fully EF-backed
host scheduling, process restart or retention.

The 14 restored mutations remove context refusal, row witnesses, independent metadata, explicit handler
refusal, physical-only lookup, exact intent binding, current deadline, pre-limit overlay exclusion,
cap-source preservation, original committed scope, caller-transaction refusal, family-v2 witness writes,
prepared-flush identity reuse and the later-decision guard. The remint mutation removes both the cache
and its secondary identity guard; it exposes a second source after peer delivery. Every mutation failed
behaviorally and all source bytes were restored before the final whole-project runs.

Frozen authoring SHA256 remains
`41aa0bb979ea46fc2533b057e3677f408ab54c3411fecfafaa238a2adae38c92`.
Original public CLR overloads remain; existing Core API lines were not reordered or removed.

## Review and remaining gates

Correctness, standards and acceptance component reviews are clean after source-scope, replacement/default-context,
legacy-constructor and uncertain-flush corrections. All three final receipt verifications are clean.
Full-PR review remains at zero rounds.

T049 retained start authority/GC and T050 publisher selection/execution/stamping/dormancy precede
activation. T051 must prove actual host/fresh-provider retry, restart, recipients and retention.
This receipt does not close T045, other story evidence, deletion/guards, final current host/Studio,
required checks/full review, resulting-main verification or the third ADR0063 PR. #2898/#2907 and
program production acceptance remain open. Orleans and task-runner remain deferred.
