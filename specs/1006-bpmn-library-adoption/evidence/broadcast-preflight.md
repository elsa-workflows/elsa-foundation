# T046 initial increment — legacy broadcast inventory

T046 remains open: strict cohort/source-context contracts are not implemented by this increment.
This utility performs read-only legacy inventory; it never dispatches, repairs, acknowledges or
resnapshots an event. It does not establish FR-015 recovery, mixed-writer quiescence or production readiness.

[Source hashes and controls](broadcast-preflight-controls.json) bind the helper, tests, checked-reader tool/project lock and PowerShell entrypoint. [Six captured stores](broadcast-legacy-inventory.json) contain 33 outbox rows and zero
PublishStimulus rows. Their compressed/database hashes match all original legacy manifests. The
[owned development host observation](broadcast-owned-host-inventory.json), at its recorded historical
source revision, contains ten outbox rows and one already Delivered PublishStimulus. Its coherent
read-only SQLite backup was inspected; the private host database is not included. These findings
apply only to those stores at observation and do not qualify a deployment elsewhere.

Run without an HTTP host or credentials:

```powershell
$captures = @(Get-ChildItem specs/1006-bpmn-library-adoption/evidence/legacy/*.sqlite.gz | ForEach-Object FullName)
./e2e-tests/bpmn/Test-BpmnLibraryAdoption.ps1 -BroadcastPreflightDatabases $captures `
    -EvidencePath ./broadcast-preflight.json -SourceRevision <actual-capture-source>
```

An unresolved legacy send remains recovery-required even at attempt zero. Only Delivered is counted
as settled by this conservative inventory; Pending, Delivering, FailedRetryable, FailedFinal and
Cancelled retain unresolved responsibility pending an explicit existing retirement/recovery proof.
A missing table, unsupported family, corrupt content or mismatched projection cannot produce a clean
inventory. The helper intentionally understands only the current legacy family; new-family validation
will require its declared reader. New-family or unknown snapshot presence never silently qualifies.

Compressed captures are immutable closed whole databases. Live SQLite sources use a bounded coherent
SQLite backup that includes committed WAL data, then inspect the private copy. They never use immutable
mode on the live database. The wrapper records observation times, whole-snapshot hashes and the reader's
four relevant assembly hashes. The native reader refuses live WAL sidecars or changed snapshot hashes.

The tool is registered in `Elsa.Server.slnx`; the generated solution-filter check passes.

The small `tools/bpmn/DeliveryPreflight` command reuses `RuntimeSqliteDbContext` and the public
`EfRuntimePostCommitOutboxStore.FindAsync` checked reader. That provider validates the declared family,
lossless identities, delivery-state invariants and all projections. The tool checks each enumerated
physical row is the same checked source; a forged duplicate cannot borrow another row's valid content.
It reads every row through offset pages on an immutable private snapshot, including malformed empty or
NULL IDs, and emits no successful prefix if any source refuses. There is no second delivery-state parser
or settlement policy inside the Python wrapper. A protected snapshot cannot appear under the legacy
family stamp. Unknown families refuse through the existing schema chain.

Review found two false-clean paths in the initial standalone classifier: an empty-ID row was skipped,
and incomplete Delivered data lacked its required timestamp/ownership checks. The revised command uses
the real checked reader and includes negative controls for both. Fourteen controls pass, covering valid
status handling, attempt-zero ambiguity, empty/NULL IDs, six malformed settlement fields, forged source
redirection, unknown family, unsafe legacy cohort, privacy, WAL/uncommitted data, closed-capture hashes,
and multiple pages. Four restored mutations fail: omit empty/NULL rows (two failures), omit the real Core
Delivered timestamp guard (one), omit enumerated-source identity validation (one), and ignore WAL (one).
Every source mutation and native build was restored before the passing controls/actual inventories.

The provider's existing UTF-16LE Base64/string-enum serialization is interpreted by its own reader.
Receipt identity hashes use UTF-16LE code units; raw IDs, scopes, payloads, credentials and provider
messages are omitted. Paths in receipts are portable basenames. The new preflight parameter set builds
the validator through the normal dotnet wrapper, then returns before connecting/authenticating; ordinary
Host parameter selection remains available. Existing NuGet/analyzer warnings are not a clean-warning claim.
Independent correctness, standards and acceptance component reviews of the revised reader are clean.

Remaining T046 contracts and all T047–T051 source/host gates stay open. There is no concrete residual
legacy row requiring an owner recovery choice in these inventoried stores. The existing matched-time
owner decision is unchanged. T045, final Studio proof, semantic deletion, full-PR review, required checks,
ADR0063 and production #2898/#2907 retain their separate gates.
