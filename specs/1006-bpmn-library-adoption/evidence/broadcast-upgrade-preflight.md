# Current-format upgrade preflight

Source parent `a86c5d08c` (full commit and source hashes in the
[machine receipt](broadcast-upgrade-preflight.json)). **This is read-only checked-format inventory.
It does not qualify recipient dispatch, quiescent upgrade admission, a recovery migration, current-host
execution or Studio compatibility. T049 remains open and saved broadcast publication remains disabled.**

The original inventory classified every PublishStimulus row as legacy. It now distinguishes truly
unmarked legacy sends, scope-only legacy broadcast sends, declared sends awaiting first selection,
retention-stamped complete/empty cohorts and prior delivery. The real Runtime provider still validates
each physical row, identity, projections, family and state before classification. Every checked result
explicitly records `DeliveryActivationQualified=false`.

An owed legacy or scope-only legacy send remains recovery-required, even at attempt zero. A valid
Delivered legacy/scope-only send preserves its prior settled result. A declared Delivered send without
an accepted cohort and a historical data-only cohort, including Delivered, remain recovery-required.
Recorded cohorts are format observations, not proof that their pins exist, recipients ran or a current
claim can dispatch. Other generic scope-only handlers stay outside this producer-specific inventory.
No ordinary durable lookup, runtime handler or recipient selection policy is changed.

Fourteen [closed SQLite witnesses](broadcast-upgrade-preflight/fixtures.json) exercise those boundaries
through actual provider writes with constructed publisher/source state. The payload is a fixed test value;
these are not actual PublishStimulus producer executions. Historical data-only and scope-only controls
remove only their newer wire marker, preserving all provider-written identities and projections. Each
witness includes raw/compressed hashes and an explicit expected classification. Consumer controls use the
real tool and checked provider against every compressed witness; export success alone is insufficient.

The [six actual captured stores](broadcast-upgrade-preflight/captured-stores.json) were inventoried again
through the normal PowerShell entrypoint. All original raw/compressed capture hashes still match;
33 outbox rows contain zero PublishStimulus rows. A fresh coherent WAL-aware backup of the
[owned running development host](broadcast-upgrade-preflight/owned-host.json), at its recorded historical
writer revision, contains ten outbox rows and one valid Delivered legacy PublishStimulus. No unresolved
legacy source was observed in these supplied stores. The host's private database is not committed and
it remained running; this observation is explicitly not stopped-writer or production-wide evidence.

Independent design review rejected an online row-only recovery hold: cleanup could already have passed
its final absence check. The [reviewed contract](../contracts/broadcast-recipient-snapshot.md#quiescent-upgrade-admission-and-recovery-holds)
requires stopped actors, dispatchers, publishers, GC/retirement and other writers throughout admission
and any required migration, until qualified new binaries resume. Schema/fleet family selection or an
SQLite exclusive transaction alone cannot establish that quiescence. An actual residual source would
require a separately qualified recovery migration and, only then, a concrete owner decision if necessary.
No online hold API, status writer, new delivery ledger or fabricated source-scope authority is introduced.

Final affected Runtime EF suite: **1,569 passed, zero failed/skipped**, including fourteen new format
witness cases. Discovery and TRX reconcile. All **16** Python/provider inventory controls pass, including
the fourteen-witness matrix and the existing privacy, malformed row, family, paging and WAL controls.
Five valid classifier mutations are caught: classifying current protocol as legacy, allowing declared
Delivered without a cohort, accepting historical data-only cohorts, ignoring owed scope-only rows, and
treating an empty recorded cohort as unselected. Exact source is restored; compile errors are not counted.
Generated-filter validation and tool locked restore pass. Runtime production source, API ledgers, schema
and frozen authoring fingerprint are unchanged; other suites' parent results remain historical.

Three independent component source/catalog/receipt reviews remain separate from full-PR rounds, still
zero. Actual quiescent admission, T050/T051, remaining semantic stories, duplicate-semantics deletion/guards,
final current host/Studio, full-PR checks/resulting-main and ADR0063 remain open. The legacy checkpoint writer
is present. #2898/#2907 and program production acceptance remain open; Orleans/task-runner remain deferred.
