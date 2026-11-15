# Experimental caller keys and physical retirement

This is a local prototype for kernel program #4229, not approved product architecture. Run
`dotnet run --project prototypes/kernel-model/models/Recreate.Run -c Release` for memory proof.
Set `KERNEL_RECREATE_PG` to an empty, experiment-owned PostgreSQL 17 database for optional provider proof.
The harness creates its schema; database lifecycle belongs to its caller. Historical R5 source
`17d0c97f0d4b11b46394971fe5568c45530f12bd` and the earlier reviewed candidates remain unchanged.
The current composition preserves main `dbc716f3994863b64c8344a33a45090d95398367`, including landed Park,
Native/Statecharts final-outcome seams and the separate landed commit-clock batch experiment.
That experiment is preserved as main source, not adopted as Card4 architecture. Typed inbox keys, event acceptance, physical child
issuance, send ordering, provider fences/collations and viewer semantics remain authoritative.

## Physical runs and atomic commits

Owner Delete retains an empty marker at canonical generated child, event and keyed physical addresses.
The marker equals `CreationProtocol.Retire(previous)`, preserving the preceding ended status with revision + 1.
Memory validates the transition. Relational revision predicates and transactional triggers, and MongoDB
replacement predicates, reject running/already-retired predecessors and changed ended status. Marker
validation rejects payload, extra headers and arbitrary named addresses. Duplicate insertion and ordinary
Worker supplied starts cannot resurrect retained markers. Row-bearing turns name the same physical actor
in `TurnCommit.Instance` and `Row.Id`. EF raw updates also accept database-equal aliases through a second
Id predicate in the existing UPDATE; the database column supplies equality. Raw inserts also retain
provider-equal spellings. Their conditional same-command guard performs an indexed EXISTS check on
SQL Server or a MySQL DO/IF expression without a result set. Both add indexed reads on that conditional
path. A qualified MySQL same-value UPDATE variant was retained as a rejected alternative because it
invoked update triggers; DO removes that side effect. Neither guard adds a driver request; guard activation leaves the candidate
canonical SQL and parameter shapes unchanged. These are prototype measurements, not a selected latency budget.
An ordinal actor/row appears at most once per batch; database-equal spellings retain their per-update
revision predicates and irreversible retirement protection. Refusal rolls back
unrelated effects. Raw `TurnCommit(Delete:true, Row:null)` remains a separate trusted primitive with
a present Expected matching an existing row, including marker deletion. Stored revisions zero and negative
remain exact raw authority; a missing actor has no revision. A row-bearing Delete must instead carry the
canonical retirement marker at Expected + 1. Active replacement rows, absent Expected, wrong revisions
and missing actors refuse the entire mixed batch, including history/sends/waits/position. Ordinary named-address recreation remains available.

Positive `CreationIdentityAvailable` stays false on physical providers; no positive-generation propagation
is inferred. Optional `PhysicalRetirementAvailable` defaults false, forwards through transparent wrappers,
and guards generation-zero owner retirement independently of positive creation capability. All accepted relational INSERTs are active (Retired=false). The database
false default is used without generated readback; BeforeSave Ignore omits only that validated INSERT
value. Every requested first-retired header refuses atomically before it can be silently materialized
as active. Existing retirement UPDATEs still write true. The 16 measured reference cases retain observed driver/TCP counts,
including ordinary INSERT budgets (SQL Server owner 100 at 4 and owner 1000 at 13 TCP edges).
The added Retired property also participates in active EF UPDATE parameter shapes; universal UPDATE
parameter/batching identity and unmeasured batch thresholds remain unqualified. A rejected direct
Retired INSERT-parameter variant measured 5/14; it is not an adopted budget. Old schemas
and invalid historical rows are unsupported; fresh experiment stores are used.

## Acceptance-time caller keys

Memory holds its store lock. The optional PostgreSQL authority takes a per-key transaction advisory lock,
reads receipts/binding, samples database time, and writes physical target, message and accepted receipt
atomically. Starts also publish the binding. A checked, store-owned serial mints protected `g*/key/*`
addresses. Only that authority inserts the creator scope; ordinary ingress strips supplied scopes.
An unrelated existing or pending supplied start cannot claim the accepted physical target.

An accepted replay returns its original receipt after rebinding; changed immutable content conflicts.
A new operation starts a replacement only after owner deletion and the previous reuse boundary. Present
finished runs retain the binding. Ineligible attempts save no accepted receipt. Accepted direct commands
pin their physical target at acceptance; delivery never resolves the key again. Old accepted receipt
replay and distinct delayed command IDs cannot mutate the replacement. Event correlation is separate.

Keys and operation IDs retain their validated domain, including NUL, unpaired UTF-16 surrogates and long
strings. Base64 over exact UTF-16 code units supplies PostgreSQL text. Fixed hash buckets index bindings
and receipts; exact full encoded identities remain authoritative under the transaction lock and ordinal
collation. Deliberately shared buckets exercise collisions, distinct keys and operation IDs. Hash equality
never establishes identity. These tables have one admission authority; external direct writes are unsupported.

## Keyed ephemeral terminal profiles

Inline completion without a caller key retains its existing zero-store behavior. A keyed start already
has durable acceptance. Its otherwise unwritten terminal turn uses the existing inbox-consumption commit:

- A model without metadata, or an explicit `IStateFreeTerminalMetadata` opt-in, may retain only logical
  terminal identity: `IdentityOnly=true`, logical State `null`, no fabricated state type. Opt-in metadata
  receives identity and the commit clock; it must explicitly answer/refuse requests and protect deletion.
  The new deletion predicate receives only Available/Resolve through a non-castable observation facade;
  metadata-message turns retain the mutable clock. Existing typed deletion-predicate semantics are unchanged.
  Clock Capture/Release and existing actor-owned facts pass through the ordinary durable seal pipeline;
  replies leave after commit. Unavailable provider clocks refuse Capture/Require. The durable IdentityMetadataRequired header preserves that opt-in obligation across cold model changes.
  A model dropping either or both metadata interfaces refuses before controls or terminal drain can
  answer a query, accept owner deletion or permit caller-key rebinding. Ordinary plain identity does
  not acquire that metadata obligation and remains deletable without a metadata model.
- Existing `IModelMetadata` promotes to its ordinary typed checkpoint on this keyed durable path. This
  preserves value-type state, deletion protection and future metadata. It is metadata-required promotion,
  not an identity-only/no-model-state claim. A legitimate ordinary null checkpoint remains ordinary state.

Full and State history retain their revision/projection behavior; Minimal omits history. Other persistence
obligations retain their existing mode behavior. State-free support is explicit, not inferred from null.

The logical discriminator uses a versioned control envelope in the existing physical State cell, with no
new column, read, capability flag or altered model Type. Logical null occupies 29 UTF-8 bytes physically for plain active identity and 33 for the required
metadata protocol; a sample typed value checkpoint occupies 11. The two version-2 controls preserve
that distinction without a column/INSERT parameter/read. Ambiguous version-1 identity controls from
the rejected prototype cannot prove their obligation; they refuse with their exact bytes preserved,
without automatic rewrite. This is unsupported old prototype data, not a migration promise. Ordinary opaque values beginning with
any reserved `!kernel-state/` family/version prefix escape once and round-trip losslessly on both load
paths. Unknown/malformed physical controls refuse without rewriting bytes; this is not a mixed-binary
migration promise. Retired markers are distinct, canonical plain `null` with IdentityOnly false.

## Evidence, costs and remaining boundaries

The executable covers competing starts/retries, equality at the reuse boundary without a memory sweep,
retained replay, pinned delayed commands, children, owner takeover, held starts, crashes and unknown
terminal commits. Cold value-type/state-free metadata, deletion protection, all history profiles and
unkeyed zero-store controls run separately. PostgreSQL checks atomic inbox-collision rollback, a request
crossing expiry while waiting for its key lock, forced bucket collisions and concurrent long/NUL/surrogate
key/operation lifecycles. Store conformance exercises atomic split-identity/batch rejection, rollback/races,
retirement shape, nonreuse, typed inbox twins and supplied resurrection on all six providers.
The raw Delete case also covers 25 malformed/missing/wrong-revision mixed refusals and valid exact
stored zero/negative revisions. Cold metadata checks remove both interfaces and the state-free interface
alone; accepted key Deletes cannot retire a protected run or release its binding.

Warm PostgreSQL small acceptance/enqueue/rebind/replay/conflict samples used one observed TCP edge each;
terminal flush used two for typed promotion and state-free profiles. A separate isolated retirement sample
compares WAL and retained tuple width. These are bounded experiment samples, not storage or latency budgets.
A new IdentityOnly column variant was rejected because SQL Server's 1000-polled-start sample rose 24→25
TCP trips; the State-cell representation restored the matched SQL Server counts. PostgreSQL bulk TCP edge
counts vary with pipelined payload segmentation. Retained comparison logs distinguish these observations
from equal command counts and equal frontend Sync/Query/Flush/Execute frames (including Execute row limits
and portals). Equal protocol groups alone do not establish equal round trips.

The Retired column, retained markers, active identity control bytes, escaped reserved values, encoded full
keys/operations, bucket indexes and WAL have storage cost. Receipt/binding/marker counts grow indefinitely.
No expiry, erasure, retention/default/tenant/provider promise or larger round-trip budget is selected.
Other ingress providers, failover durability, contended latency and production write-size scaling remain
unqualified. Positive-generation physical provider propagation remains unsupported.

## Landed Park composition

Current classifiers, poison/trial rollback, exact worker-completion provenance, terminal Status/Fault,
and owed-job cleanup come from landed Park. Typed terminal seals retain current Jobs and use row.Status;
the state-free metadata seal also carries DurableTurn.Jobs with its clock/custom-handle snapshot.
Park source is not replaced with historical recreation foundation. Permanent physical identity is
separate from the bounded caller-key reuse window. The first-hot standalone SelfReference cost card
remains unanswered; this experiment changes no lookup/default/tenant promise.

## Closed a991 pair and protocol repair

The independent pair on a991 closed with two Major findings and one clean report; its pair was not clean.
This repair persists the missing metadata obligation and constrains raw Delete representations.
The byte-identical review probes reproduce the old failures and pass against the repaired implementation.
Source-identical qualified preparation records 54 provider filter groups, 90 invalid first-marker variants,
31 composed runtime modes and the full memory suite (85 passed, one declared skip). PostgreSQL's
expanded harness uses 172 admission client calls including deliberate rollback; these are proof calls,
not a changed per-operation budget. The qualified preparation retirement/WAL window is 512/656 bytes with
4 observed TCP edges each; wide-key WAL is 36624 bytes. Prior windows remain separate observations.
All 16 actual-current-main SQL Server driver/TCP rows match; retirement retains its separately recorded
+1 internal server statement. PostgreSQL bulk transport-edge variation remains explicit.
TWO NEW completed independent nonauthor clean reviews remain required. Architecture/product/default/
retention/budget choices and the later hardening A-F proposal remain unapproved/held.

The actual repaired delivery prefix6 repeat separately measured raw/marker WAL536/632 at4 observed
TCP edges each, tuple101/bool1, required metadata33 bytes and wide-key WAL36624. Earlier windows
remain retained observations; no fixed write-cost promise follows.
