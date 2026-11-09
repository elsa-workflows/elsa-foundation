# Retained connector output plans (partial T005)

An owed connector batch can now retain its complete transformation result before route dispatch.
The result contains accepted/rejected/filtered counts and fixed main/error slots. Positive route
counts require an immutable body; a fully filtered nonempty batch has no bodies. Counts must account
for every reserved input record. Route kind must match the pinned destination, and a versioned effect
identity binds invocation, batch, route, destination revision and exact content hash, bytes and records.
Worker ownership and physical attempts do not enter that identity. The worker must verify actual bodies
before calling the store; a reference is not physical publication or remote acknowledgement evidence.

Retention compares the current typed position and owed-batch pointer under a serializable transaction.
It records the canonical plan, its digest and byte accounting together, and rechecks unexpired authority
after saving. Identical replay does not charge again; conflicting plans and stale/cancelled writes are
refused. Retention neither settles outstanding input nor changes accepted counts or source position.
Release/reacquisition returns the original plan after validating its contents, destination identities
and accounting. The same contract supports CSV and retained HTTP-page input ranges.

## Separate resource bounds

`EtlConnectorLimits.MaxRetainedOutputBytes` bounds referenced route-body bytes across the run, with an
independent configurable runtime host ceiling. Defaults are 1 GiB and 4 GiB respectively. Every route
is charged, including two routes referring to identical content. `MaxOutputBatchBytes` still bounds
the sum of route bodies in one batch. Canonical output-plan JSON contributes to the existing retained
metadata budget alongside descriptors, requests, pages and positions. Logical retention is separate
from live memory, source bytes, unreferenced staging objects and database overhead; worker publication
and cleanup integration remain required.

The default new authored setting is omitted from canonical v2 JSON. A frozen default artifact without
that setting is readable and retains its bytes/hash on writing; explicit values serialize and pass
through compiler/runtime validation. Existing default v2 publication pins are not restamped.

## Schema and review

`20261021130000_ConnectorBatchOutputs` adds nullable plan/digest fields, output byte accounting and
checks in place. It does not rebuild or disable the existing SQLite foreign keys. Structural upgrade
coverage seeds request/page state at migration 1100, applies reservations at 1200, retains a reserved
batch, then applies output columns at 1300. It checks retained rows, owed pointers and foreign keys.
This is not the still-required retained-old-worker execution proof. Released v1 migrations are unchanged.

The first focused run found two test setup failures: the old seed helper omitted a non-null reservation
column when called against the newer schema. The setup now seeds at its intended version before upgrade.
A suspected null-document decoder gap was disproved by the existing shared runtime decoder; no redundant
guard was retained. The integrated recovery test verifies that malformed null plans are refused.
Root review also strengthened direct store admission to reject nonpositive output batch limits.
The first full run passed 662 cases and exposed one older migration test querying the current EF model
against migration 1200, where output columns do not yet exist. That test now uses historical-column
projections and explicit historical SQL writes, preserving its exact migration boundary. It does not
silently upgrade to the latest schema to hide the failure.

The bounded review covered schema/model agreement, stable identity, immutable output replay, quota
accounting, malformed-state recovery and expiry/cancellation. Two negative controls failed for the
intended reasons: removing post-save expiry validation retained an expired output, and removing pinned
effect-identity validation accepted an incorrect effect. Source was restored byte-for-byte before the
full suite. Tests assert retained state and unchanged positions/counters, rather than merely return codes.

## Validation

On the final reviewed source, the full ETL suite passed **663 tests** and the architecture suite
passed **584 tests**, both with zero failures/skips. The historical migration repair passed four
focused upgrade/model checks before the full run. Maps, solution filters, 51 relative links and diff
checks are recorded in the PR receipt.
Focused schema/budget/identity/output checks passed 31 cases before the final direct-limit additions.
Existing build warnings are not claimed resolved. The source commands are:

```sh
dotnet test Extensions/Etl/tests/Tests/Elsa.Etl.Tests.csproj --no-restore
dotnet test Essentials/tests/Architecture/Tests/Elsa.Architecture.Tests.csproj --no-restore
dotnet run --project tools/maps/Elsa.Maps.Generator -- check
bash tools/solution-filters/generate-solution-filters.sh --check
git diff --check
```

## Remaining acceptance

T005 remains open; **3/22 tasks are complete**. Durable HTTP source/delivery attempts, deadlines,
not-before eligibility, acknowledgements and atomic batch acceptance remain owed. Acceptance must
validate accepted ranges and route receipts, release outstanding input exactly once, advance the typed
position and settle terminal/outbox state atomically. Runtime connector execution remains explicitly
unavailable. Complete HTTP/native/API/Studio and two-process interruption journeys remain required;
these storage tests do not claim them. Physical external effects remain at least once unless the
receiver's stronger behavior is demonstrated. Historical timeout causes remain unexplained.
