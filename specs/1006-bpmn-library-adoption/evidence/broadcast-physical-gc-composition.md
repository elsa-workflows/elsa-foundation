# Physical retention and default collector composition

Source parent `d39a99298` (full commit in the [machine receipt](broadcast-physical-gc-composition.json)).
**This qualifies structural collector composition and conservative maintenance over native memory and actual
SQLite. Legacy ambiguity, old-writer/quiescence, incompatible restart and publisher activation remain open.
No upgraded-host, process-kill, final Studio or production acceptance is inferred. T049 stays open, saved
broadcast publication stays disabled, and the legacy execution-checkpoint writer remains present.**

Runtime and the repository's recurring GC feature now use one internal registration factory. It selects
cohort retention only when template maintenance, source publication and delivery retention qualify the
actual selected outbox/material/reference/execution tuple. Only known coalescing decorators are normalized.
The two built-in material cache decorators explicitly expose their physical boundary; normal reads and
conditional deletion still go through the selected decorator, preserving shared-cache invalidation. A general
decorator stays opaque unless it explicitly cooperates. The same-domain friend grant lets the separate Runtime
pump assembly use the internal factory; third-party schedulers consume the registered public collector.

Native executable material is shared across scopes. Its new physical-root query therefore includes all
committed source scopes sharing that material; the original delivery query remains scoped. EF maintenance
requires one actual context and matching scopes, a clean context and a committed access boundary. It discovers
rows through either scope projection, validates their payload/projections, and traverses finite pages ordered
by unique physical row ID. A contradictory projection or unreadable authority retains material. Delivered
sources still retain selected artifacts after publisher deletion while deterministic targets have no sticky
retirement proof. Actual target retirement releases their roots; absence alone does not.

An unsupported tuple selects the ordinary collector, but cannot treat lack of cohort capability as proof of
safety. Its new provider port must prove that the same physical outbox/material/reference/execution boundary
has no protocol marker, source-scope witness or saved cohort. The collector repeats this proof on each sweep
and guarded final read. Legacy implementations refuse by default. Native proof covers its global material;
EF also checks actual context/scope and committed clean caller state. This closes two findings from independent
review: a collector cached during temporary scope failure, and an empty foreign outbox incorrectly authorizing
collection of another database's material. The proof deliberately keeps marked data even for a delivered empty
snapshot. It does not classify ambiguous unmarked legacy sends or establish absence of old binaries.

Fifty new executed controls, plus six existing controls, cover:

- Actual native/EF coherent and foreign tuples, cache enabled/disabled, known coalescing stores, repeated
  registration, both Runtime/pump registration orders and real ordinary template cleanup.
- Shared native material rooted by two source scopes, with unrelated orphan cleanup, publisher deletion,
  absent-target retention and release only after actual deterministic target retirement.
- Real fenced source publication and conditional deletion through caching/direct-read invalidating stores,
  including another warmed reader sharing the cache; opaque decorators refuse.
- A collector resolved during invalid scope access, then reused after recovery: ordinary orphan cleanup is
  allowed before protocol data exists, but later accepted obligations retain material and retired references.
- Native/EF empty foreign outboxes cannot authorize another material boundary. Four native/EF interleavings
  install a cohort between initial discovery and the final artifact/reference guard read; the same ordinary
  collector retains material and releases its guard.
- Dirty tracked EF state survives actual qualified and ordinary sweeps without clearing, flushing or deleting;
  clean positive twins collect the orphan. Actual local and ambient transactions refuse maintenance reads.
- Contradictory EF scope/hash/order projections, a root beyond the first 500-row page, access failure causes,
  requested cancellation, legacy default refusal and protocol data before selection or after an empty delivery.

The restored affected suites pass **5,654 cases, zero failed/skipped**: Runtime 2,971; Runtime EF 1,555;
Publishing EF 176; Publishing API 891; Publishing 61. Discovery and TRX counts reconcile. The focused set passes
56/0/0; six public-API/friend architecture guards, generated-filter validation and Runtime Core locked restore
pass. Sixteen valid behavioral mutations are caught, including omitting only the final ordinary proof,
foreign-boundary acceptance, scoped-only roots, truncated paging, corrupt-projection hiding, coalescing/cache
boundary loss and stale-cache deletion. No compile-error mutant is counted; exact source is restored.

Core adds four default interface members and removes none; existing constructor signatures and Publishing
API ledgers remain unchanged. Runtime schema and the frozen authoring fingerprint are unchanged. Three
independent component source/catalog/receipt reviews are recorded separately from full-PR review rounds,
still zero. Current host/fresh-provider execution and Studio verification, T050/T051, remaining semantic
stories, duplicate-semantics deletion/guards, final PR review/checks/resulting-main and ADR0063 remain open.
#2898/#2907 and program production acceptance remain open; Orleans/task-runner remain deferred.
