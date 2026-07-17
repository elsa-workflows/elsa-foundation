# DispatchWorkflow parent #674 audit

Date: 2026-07-17  
Remediation branch: `codex/dispatch-674-remediation`  
Original program base: `codex/dispatch-677` at `674b7125b6b41cc64ab0a9111886ef635c6d7ad7`  
Replacement implementation: PR #717, merge commit `164ff88e8`

## Outcome

The transport-neutral DispatchWorkflow program is complete for parent issue #674. The original
#678–#683 work-unit chain was preserved, its replacement implementation was merged by PR #717, and
this audit revalidated every reported parent-review finding against that replacement.

The remaining implementation issues were corrected:

- Runtime API failure evidence now exposes incident and dead-letter identifiers only when the
  complete deterministic dispatch/start-outbox relationship validates.
- Groundwork dispatch paging and post-commit outbox delivery/claim selection now use provider-side
  bounded, stably ordered routes. Store-level regressions prove a fixed number of admitted bounded
  reads with `Take` propagated to every read; SQLite and in-memory suites prove equivalent behavior,
  and SQL Server route admission proves the dispatch composites fit the provider's index-key limit.
- Null outbox availability remains immediately claimable through dedicated null-aware bounded
  routes, and unrestricted positive query limits no longer overflow internal collection capacity.
- Test-scope IDs and post-commit intent kinds now enforce the portable projection bounds needed by
  the largest dispatch composites, while Groundwork's ordinal document identity remains the stable
  outbox tie-breaker without duplicating the outbox ID in each physical index.

The other reported crash, redrive, retry, cancellation, retention, cleanup, and distributed
convergence concerns were already corrected by the replacement program and remain covered by
executable regression tests. The canonical spec 101 safe redrive disposition contract is
preserved; it intentionally supersedes the earlier draft suggestion to replace rejections with a
different error envelope.

`WorkflowDefinitionActivity`, Studio UI support, broker-specific transports, and activity-level
node, queue, priority, affinity, or transport-selection inputs remain outside this program and were
not implemented.

## Parent acceptance traceability

| #674 theme | Canonical work unit | Current evidence |
|---|---|---|
| Dedicated transport-neutral activity, fire-and-forget default, deterministic child identity | `096`, `097`, #678 | DispatchWorkflow contract and end-to-end suites |
| Durable lifecycle, parent/child linkage, safe inspection, readiness | #678 | Runtime, Runtime API, Groundwork, and architecture suites |
| Wait success and durable parent resume | #679 | DispatchWorkflow and Runtime resumption suites |
| Child fault/cancellation and cancellation propagation | #680 | DispatchWorkflow lifecycle and cancellation regressions |
| Retry, terminal delivery failure, safe incident, redrive | #681 | Runtime processor/store, Runtime API, DispatchWorkflow, and Groundwork crash/race regressions |
| TestRun inheritance and detached-scope cleanup | #682 | Run-kind matrix, hostile-scope, cleanup paging, and provider-recreation regressions |
| Distributed child execution, duplicate/fence convergence, restart/readiness | #683 | Two-node runtime and distributed Groundwork suites |
| Bounded operational persistence | Parent remediation | Provider-bounded dispatch keyset and outbox delivery/claim route tests |
| Safe failure projection | Parent remediation | Runtime API deterministic-identity, corruption, and list/detail parity tests |

## Finding dispositions

| Review area | Disposition |
|---|---|
| Final-failure replay and atomic projection | Verified fixed: final claim completion, dispatch failure, safe evidence, and waited follow-up commit atomically and converge across replay. |
| Redrive crash/concurrency behavior | Verified fixed: provider-atomic state/evidence changes, request idempotency, conflicts, fencing, and restart races converge. |
| Parent resume and cancellation retry | Verified fixed: deterministic work uses until-acknowledged retry with positive backoff. |
| Admission/cancellation race | Verified fixed: child admission and cancellation share durable lifecycle fencing. |
| Retention and cleanup progress | Verified fixed: snapshot-conditional deletion and keyset continuation prevent stale deletion and first-page starvation. |
| Runtime API rejection shape | Closed as non-actionable: spec 101 ratifies the authenticated safe-disposition response. |
| Runtime API failure identifiers | Corrected: arbitrary or mismatched persisted IDs are suppressed and cannot enable redrive. |
| Groundwork provider work bounds | Corrected: composite stable-order routes carry `Take` into every admitted dispatch/outbox provider query; store-level regressions bound query count and returned rows independently of retained history. |
| TestRun and distributed acceptance gaps | Corrected/verified: exact run-kind terminal matrix, provider recreation, hostile scope, resumption order, and integrated two-node restart scenarios are executable. |
| Task-ledger evidence drift | Corrected in spec 102; specs 101 and 103 were rechecked and already name existing evidence. |

## Verification evidence

| Command | Result |
|---|---|
| `dotnet test tests/Elsa/Workflows/Runtime/Tests/Elsa.Workflows.Runtime.Tests.csproj --no-restore --nologo -m:1` | Passed: 1155 |
| `dotnet test tests/Elsa/Activities/DispatchWorkflow/Tests/Elsa.Activities.DispatchWorkflow.Tests.csproj --no-restore --nologo -m:1` | Passed: 182 |
| `dotnet test tests/Elsa/Workflows/Runtime/Api/Tests/Elsa.Workflows.Runtime.Api.Tests.csproj --no-restore --nologo -m:1` | Passed: 62 |
| `dotnet test tests/Elsa/Workflows/Runtime/Resumption/Tests/Elsa.Workflows.Runtime.Resumption.Tests.csproj --no-restore --nologo -m:1` | Passed: 17 |
| `dotnet test tests/Elsa/Persistence/Groundwork/Tests/Elsa.Persistence.Groundwork.Tests.csproj --no-restore --nologo -m:1` | Passed: 526 |
| `dotnet test tests/Elsa/Workflows/Runtime/Distributed/Persistence/Groundwork/Tests/Elsa.Workflows.Runtime.Distributed.Persistence.Groundwork.Tests.csproj --no-restore --nologo -m:1` | Passed: 55 |
| `dotnet test tests/Elsa/Workflows/Runtime/Distributed/Tests/Elsa.Workflows.Runtime.Distributed.Tests.csproj --no-restore --nologo -m:1` | Passed: 45 |
| `dotnet test tests/Elsa/Architecture/Elsa.Architecture.Tests.csproj --no-restore --nologo -m:1` | Passed: 226 |
| `dotnet test tests/Elsa/Activities/Scheduling/Tests/Elsa.Activities.Scheduling.Tests.csproj --no-restore --nologo -m:1` | Passed: 24 |
| `dotnet test tests/Elsa/Persistence/Groundwork/SqlServer/Tests/Elsa.Persistence.Groundwork.SqlServer.Tests.csproj --no-restore --nologo -m:1 --filter FullyQualifiedName~Dispatch_physical_routes_fit_SQL_Server_index_limits_without_connecting` | Passed: 1 |
| `dotnet test Elsa.Server.slnx --no-restore --nologo -m:1` | Executed; failed only in the pre-existing `AspNetCoreIdentityProviderEvidenceTests.Checked_in_provider_artifacts_are_complete_sanitized_and_share_one_tested_code_candidate` evidence-version alignment check, outside the remediation diff. A focused rerun reproduced the same failure. |
| `rg -n -- "- \[ \]" specs/101-dispatch-delivery-recovery specs/102-dispatch-test-run-scope specs/103-dispatch-distributed-execution` | No unchecked tasks |
| `git diff --check` | Passed |

Groundwork suites emit existing `GW0001`–`GW0004` obsolete-API warnings in legacy manifest and test
adapter code. They are non-fatal and are not DispatchWorkflow regressions.

The SQLite unit fixture's legacy bounded-query adapter intentionally materializes documents before
emulating newly admitted physical routes. Consequently, the audit treats SQLite as functional
provider evidence, not as a measurement of physical rows scanned. Physical boundedness is established
by the admitted composite route declarations plus store-level assertions that every provider request
has a bounded `Take`; it does not claim that the legacy test adapter itself performs index-limited I/O.

The self-review loop converged after six iterations. It corrected null-availability selection,
limit-derived allocation overflow, provider-evidence wording, PostgreSQL null ordering,
logical/physical missing-value parity, and SQL Server composite-index width. The sixth iteration
found no remaining actionable issues.

## Generated-map freshness exception

Generated-map refresh was explicitly skipped by user instruction. No map generator was run and no
generated map snapshot was changed.

The checked-in `docs/maps/manifest.json` already reports `relevant_inputs_dirty: true`, advisory
`git_head: f0f8a51c230c8b1df3c704af3128dd52a90a1617`, and an input count of 3174. Those snapshots must
therefore not be treated as fresh evidence for this audit. Source, feature specs, task ledgers, and
the executable tests above are the verification sources used here.

## Scope and authority notes

- `/Users/sipke/.codex/worktrees/8258/elsa-foundation` remained read-only and was not modified,
  cleaned, reset, or overwritten.
- The original `codex/dispatch-677` branch and #678–#683 work-unit branches were not altered.
- Generated maps were not refreshed.
- Remote delivery is performed only under the user's explicit authorization and is reported in the
  final delivery handoff rather than used as implementation evidence in this audit.
