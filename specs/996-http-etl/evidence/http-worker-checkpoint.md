# Shared HTTP/file worker checkpoint

Issue [#3151](https://github.com/elsa-workflows/elsa-foundation/issues/3151), single draft
[PR #3156](https://github.com/elsa-workflows/elsa-foundation/pull/3156). This checkpoint composes the existing
connector storage and transport into the actual shared worker. It addresses T008/T009; full native, Studio,
operator and process acceptance remain separate tasks.

## Implemented boundary

The admission service validates the retained v2 artifact with runtime-only services, resolves current trusted
bindings once, and admits through the optional connector store. A file-only provider still runs v1 work and
returns a deliverable v2-unavailable refusal. Replacing/decorating the common store does not bypass that choice
or require the replacement to implement connector persistence.

Preparation retains only declared CSV source/lookup inputs, verifies their pinned digests and schemas, and
writes a bounded source index. HTTP sources require no synthetic file. Partition attempts re-open verified
inputs, decode HTTP records directly into typed batches, apply optional lookup/filter/mapping and hash an exact
canonical v2 range. Caller-owned shard IDs are copied. The v1 canonical writer remains unchanged; shared
bounded stream implementations preserve its byte-level behavior. CSV indexes remain bounded by source bytes
but can be substantially larger than newline-dense source files; this is not a total disk quota.

The worker discovers configured file and HTTP scopes and rotates preparation, partition and terminal-delivery
work within one physical attempt pool. One connector attempt processes a batch or an empty page, then yields.
Delayed HTTP retries remain durable and do not sleep in a worker slot. Heartbeat renewal uses a separate scope;
process-local allowances remain held until the real attempt and claim cleanup unwind.

Before dispatch, output processing retains the complete main/error plan and exact immutable route bodies.
HTTP routes use JSON arrays and stable effect IDs; file routes use verified create-only JSONL publication.
Only newly allocated HTTP permits dispatch. Acceptance remains a separate transaction after every route has
durable evidence. Fully filtered input accepts zero effects. Terminal failures preserve unaccepted responsibility
and counters as evidence instead of pretending those batches were accepted.

The additive `PermanentBudgetExceeded` result distinguishes proven retained-resource exhaustion from temporary
outstanding reservations. The latter yields; the former settles a safe diagnostic. Metadata classification retains
irreversible history and credits only capacity that could be released, so a peer's live reservation is not mistaken
for permanent exhaustion. Request/page/body limits remain independent of process-local logical allowances.

## Executable acceptance

- Real `EtlRuntimeWorker`, SQLite and loopback HTTP execute HTTP-to-JSONL, HTTP-to-HTTP, CSV-to-HTTP and mixed
  HTTP/file routes with CSV lookup, filtering, a rejected row, multiple batches per page and an empty final page.
- A host with only HTTP scopes and one physical slot progresses another shard while the first waits for Retry-After.
- Fully filtered batches advance with no POST or output chunk. Permanent output retention failure sends no POST,
  retains owed evidence and prevents further claims.
- Direct output tests exercise exact combined route-body limits, 204 acceptance versus 202 rejection, and corrupted
  retained-body refusal before permit allocation. Dispatch without the retained plan is refused.
- Input tests cover canonical byte/record splitting, exact reconstruction, BOM-aware CSV headers, retained lookup
  corruption, bounded diagnostic failures and immutable admission shard identities.
- Existing v1 replacement/capacity and child-process interruption cases cover compatibility of the shared worker.

## Review and validation

Five bounded review/fix passes, including independent helper, coordinator and metadata reviews, ended without
remaining actionable findings. Corrections included exact batch splitting, frozen shard identities, optional
file-only provider composition, dispatch-plan verification, transient publication yielding and historical metadata
retention. An early full-suite run was stopped after the optional-provider regression also prevented child-worker
progress. Fixture corrections covered retained lease assignment and preserving owed counters on terminal failure.

| Final gate | Result |
| --- | --- |
| `dotnet test Extensions/Etl/tests/Tests/Elsa.Etl.Tests.csproj --no-restore` | 925 passed, 0 failed, 0 skipped |
| `dotnet test Essentials/tests/Architecture/Tests/Elsa.Architecture.Tests.csproj --no-restore` | 584 passed, 0 failed, 0 skipped |
| Maps `all`, `solution-filters`, `check`, `solution-filters-check` | Passed; generated maps and findings reports byte-identical |
| `dotnet build Essentials/apps/Elsa.Workbench/Elsa.Workbench.csproj --no-restore` | Passed, 0 errors; existing warnings remain |
| `pwsh -NoProfile -File e2e-tests/etl/Test-FiniteFileEtl.ps1` | Passed; owned rebuilt Workbench, isolated storage, persisted v1 Sequence/Flowchart continuation |
| Evidence links and `git diff --check` | Passed before commit |

The first architecture pass found only the newly linked evidence file missing from Git's index. After staging
all reviewed new files, the full tracked-file architecture pass was green. No architecture rule was relaxed.
Each Workbench run produced two accepted, one rejected and one filtered record across two partitions, with two
main chunks and one error chunk. Its owned server was stopped afterward. This REST result is v1 compatibility,
not HTTP-native acceptance.

Three separate negative controls removed the retained-output dispatch precondition, HTTP-only scope discovery,
and permanent-output-limit classification. Each failed its intended test, and byte/SHA-256 checks verified exact
source restoration before the final full suite. The historical-journal regression separately retains page-zero
capture, advances to page one, then verifies a one-byte-short irreducible metadata cap fails without mutation;
the peer-reservation test remains retryable. Fresh-head CI is pending after push.

T008/T009 are complete: **8/22 tasks** in this full leaf. This is not approval to merge the incomplete draft.

## Remaining acceptance

This does not prove HTTP continuation into persisted native Sequence/Flowchart, Studio authoring/inspection,
observed application telemetry, two-process HTTP interruption recovery or a frozen predecessor-worker resume.
Those remain T003/T010–T022. The task runner and workflow-scoped pause remain post-program work. External effects
remain at least once; lease expiry does not prove physical termination. Historical receipt/HTTP timeout causes
remain unproved.
