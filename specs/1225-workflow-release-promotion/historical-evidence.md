# Historical evidence and unresolved qualification

These results belong to the unmerged #3911 branch or unpublished candidates. They are retained inputs,
not evidence that current main or this documentation slice implements release promotion. The old worktree
and raw artifacts remain intact; resubmitted production slices must qualify their own exact source.

## Published foundation checkpoints

- Immutable transport at `582248ac81d688a5a6db54f499ec09dd1ed15c55`: 51 focused tests and a clean packed consumer
  passed, with digest and I/O boundary controls. [Original receipt](https://github.com/elsa-workflows/elsa-foundation/blob/449d9cdaaa076e7a8fd7bb579bfeca33122eb02a/specs/1216-workflow-release-promotion/evidence/transport-582248ac8.json).
- Connections prerequisite at `f8caf0b6ad`: core/API 175 and SQLite 50 passed; PostgreSQL, SQL Server and MySQL
  each passed one owned-provider journey. This establishes historical resource identity enforcement, not deployment mappings.
  [PR receipt](https://github.com/elsa-workflows/elsa-foundation/pull/3911#issuecomment-6673900883).
- Clean Linux foundation at `2077ab11c33746fdb700429f48b70da1fc627d11`: 337 passed, zero failed/skipped
  (133 shared, 74 Runtime codec/profile, 86 ETL release/bridge, 44 archive). Full logs/TRX were independently checked.
  [PR receipt](https://github.com/elsa-workflows/elsa-foundation/pull/3911#issuecomment-6675468728).
- Retained source framework at frozen tree `8dafe4cb27241942b61e92ad66ecb21d0b7423e0`: 155 shared and
  37 capture cases passed, including actual durable SQLite archive commits. Controlled owner callbacks do not prove
  real native/BPMN/ETL acquisition/import. [PR receipt](https://github.com/elsa-workflows/elsa-foundation/pull/3911#issuecomment-6676810627).

## Unpublished integration and stabilization

The composed candidate `62200781a631ce0fd505a75a17c8a53b26037d44` failed overall. Six complete suites passed
225 cases; partial suites contained 534 passed and nine failed cases; six commands were compiler-blocked.
The full ETL suite reported 64 failures before its original timeout and produced no TRX. These are distinct outcomes,
not one passing total. [Original checkpoint](https://github.com/elsa-workflows/elsa-foundation/issues/3590#issuecomment-6682170839).

The 8 November stabilization at frozen tree `dbb84761ddb8a6cbc03c3a337aa6c966db99085a` qualified 15 causal
repair files plus one lockfile, excluding later feature work. Three complete projects passed 31, 118 and 399 cases;
Runtime EF passed 145 and failed four. Total: 693 passed, four failed, zero skipped. Seven later commands were not run.
The historical installed CLI's 15 cases were separately recovered and checked at their original `62200781` source;
they do not qualify the stabilization candidate or complete promotion commands.

The four EF failures were two reflection fixtures missing the protocol-router constructor, one query-count mismatch,
and a deployment-aware claim exhausting 16 compare-and-swap attempts. The claim failure occurs on the first drain,
before the test reaches deferral or database reopen. An earlier description placing the failure after reopen was incorrect.

## Minimal claim fix, 8 November

`ToClaimedEntity` mutates the loaded row and increments its revision. The new deployment-aware claim path passed
that changed revision as EF's expected original revision, so the conditional update could never match stored state.
Saving the original revision before mutation fixes that path. The existing legacy claim path already used this pattern.

Frozen tree `f31a656cb3725537230daaf85f9fefbaab9d8485` passed four targeted cases, zero failed/skipped:

1. First deployment-aware claim persists the revision and fencing-token increment.
2. Deferred work retains visibility and identity after reopening the actual SQLite adapter.
3. Deferral refuses a claim committed by another owner between its read and update.
4. A competing worker respects the live owner, takes over after expiry and fences the stale completion token.

Full raw artifact SHA-256: `c3001f90ffa8e39a51f3f55d7dfd97a1300fa50525e3980925c59d7bb52710ef`.
The complete logs/TRX were independently verified; the owned Azure worker was deallocated. The patch and two new
regressions remain in the original uncommitted worktree and isolated candidate. They have not landed on main.

The two constructor cases, query-count case and seven unrun commands remain unresolved historical qualification.
Do not relax their assertions or transfer these four passes to the whole EF suite. Refresh the owning integration
on current source when the deployment context/claim slice is resubmitted. No activation/recovery, two-target,
retained-run or Studio acceptance follows from these results.
