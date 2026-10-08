# Captured command and checkpoint finalization

## Disposition

The test-only lifecycle correction is accepted for focused verification at `17f6d4f64844f53192eb8497cf77212d2eb13953`. Four observer tests passed, an immediate-finalization mutation failed the intended pending-task assertion, and the restored source passed all four tests again. This does not accept the response annotation, the combined-source workflow comparison, or any database-access reduction.

## Original failed observation

At combined source `188c3c2a39306b77b4e0410dabd551fab93158ae`, the candidate comparison passed once, but the one-line External profile mutation failed `RunMeasuredChildAsync` because the returned snapshot contained two incomplete EF command attempts. Both were unattributed: one `Reader|LinqQuery` and one `Reader|SaveChanges`. All five child processes exited zero; the measured response was HTTP 200 with `Alice Smith`. These facts establish neither ownership of those commands nor a runtime correctness defect.

`EndCapture()` closed admission and immediately returned an immutable snapshot. Subsequent command callbacks updated the internal counters but could not update that returned snapshot. Retrying until no command happened to be in flight would not correct this mechanism. The original failed TRX is retained with SHA-256 `4df474f2d7278290b5ba5992bbd74839c46f808ee9a45b71633a4fe00375fb6e`.

## Corrected boundaries

The child retains three separate observations:

1. **Response received:** the client's fully buffered response, not an exact server socket-write instant.
2. **Capture stopped:** after the parent independently verifies the measured execution's terminal state, committed response and settled scheduler/outbox state. Admission of commands, checkpoints and engine events stops under the observer lock.
3. **Callbacks drained:** only command and checkpoint attempts admitted before capture stopped may finish. The child allows 30 seconds; timeout or cancellation returns no completed finalization. The parent retains its 45-second protocol timeout.

Command starts, checkpoint attempts, dispatches, drain cycles, activity executions, carry-in/orphan counts and recorded response boundaries must remain unchanged between the last two observations. Completion outcomes may increase. Final command starts must equal success, failure and cancellation outcomes, with zero pending command attempts. Logical and durable checkpoint wrappers also participate in the drain, because their completion can follow the last provider callback.

The fixture tracks repeated command IDs in FIFO order. It does not establish attribution for overlapping, out-of-order completion under a reused command ID. Unattributed work remains unattributed. Target settlement does not assert that all other executions' queues are empty. An EF reader-success callback means execution returned a reader; it does not prove result exhaustion, affected rows, SQL statement count, or physical round trips.

## Executed focused proof

| Stage | Result | Evidence |
|---|---|---|
| Corrected observer suite | 4 passed, 0 failed/skipped | Drain with late-start exclusion and immutable snapshots; bucket attribution; explicit timeout; explicit cancellation |
| Immediate-finalization mutation | Expected failure, 0 passed / 1 failed | Changed only the admission-stop guard from `PendingAttemptCount == 0` to `>= 0`; the drain test failed at `Assert.False(finalizationTask.IsCompleted)` |
| Restored observer suite | 4 passed, 0 failed/skipped | Rebuilt after exact source restoration; same four tests |

Root reviewed the exact mutation patch, actual TRX outcomes and source-conservation receipts. All tracked source/build inputs were unchanged after each stage; production annotation bytes and observer bytes were restored exactly. Root and independent source review found no material issue in the four changed HTTP fixture/test files. The private `2400-post2497-verification-v2/` journal contains logs, full input manifests, receipts, mutation patch and source-review memo.

| Artifact | SHA-256 |
|---|---|
| Observer source at tested head | `e03d4f19179fcf8c2f8374cffb245d45d492b160e72a0d9d951d3593d96adc7f` |
| Mutated observer source | `591135880e4ae0d2ed62db1e2f6c29847f69ad9e994ca0327a5d0a1163a09861` |
| Corrected observer TRX | `f813bfb57927b1d57450297a8c1da1f0707398d8605404c126081dc2895c69b8` |
| Deliberate mutation TRX | `c3e4d55db8a3e8dbec41680a327ca45bb2ce1e15be841b63e6682eb6b499d59c` |
| Restored observer TRX | `f99e59f4d5acafa21e58cba9a6c73cf6bff36dbc86092729332df93a2ff0d615` |
| Independent source-review memo | `0306b1fa93fd407fb4d9630c53639d8400c07bd6e6079e01d05cc758fc6dd057` |

Fresh candidate/profile-mutation workflow captures, full affected verification and T013/T018 acceptance remain required. This focused fixture proof supplies no new PostgreSQL or latency result.
