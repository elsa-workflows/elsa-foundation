# Runtime EF implementation

## Private history projection model

[Spec1211](../../../../../../specs/1211-run-history-timeline/plan.md),
[history inspection contract](../../../../../../specs/1211-run-history-timeline/contracts/history-inspection.md).

`RuntimeHistoryEntityConfiguration` and the five internal history entity types define the private
head, event receipt/content, bookmark, bookmark-count and known-parent projections. With the three
derived occurrence shapes they join `RuntimeDbContext` and the single declared `RuntimeHistoryEfModule`
owner chain. They are not an extension API and do not advertise capture support.
`RuntimeHistoryProjectionSchema` carries table names; assembly declarations own the family/content set.

Indexes use bounded scope/run/resource hashes and committed positions; complete lossless identities
and content must be checked by the following provider mapping. Head revisions are concurrency tokens.
Event receipts can survive content retirement; full run deletion and retention mutations remain with
their owning transactions. A known-parent projection does not establish frozen occurrence membership.

`EfRuntimeHistoryProjectionMapping` is a private helper for the same projections. The eventual provider
owner supplies its actual `EfSchemaChain`: every read settles compatibility before interpreting stamped
identity/content and upcasts the complete content column through that chain. The helper declares no
alternative family, registers no reader and does not qualify an atomic provider. Complete scoped identities,
filter projections, committed positions and receipt/purge acknowledgement are checked beside hashes.
Unknown event vocabulary remains recorded; protected payloads and unsafe metadata are refused. Purged
creation links do not upgrade unknown creation evidence. Counts distinguish run and exact activity owners.

`EfRuntimeHistoryCheckpoint` is the private staging seam under construction. It retains the checked native
admission choice, validates ordered logical receipts and cleanup sources, reserves the history head by revision
and stages event/bookmark/count/ancestry rows inside the caller's existing transaction. A first-head reservation
may save inside that same transaction; the caller still owns rollback/tracker restoration, final participant save
and marker-last commit. It returns only newly positioned events for the later derived occurrence participant.
Its head reservation is shared internally with forthcoming observation append; it owns no separate commit.
Missing Required contributions remain owed; missing Optional contributions produce a retained gap. The eventual
native writer must invoke that check for missing contributions too. This seam is not yet called by the production
writer and does not qualify any selected reader, deletion family or capture profile.

The checkpoint seam calls `EfRuntimeHistoryOccurrenceProjection.StageAsync` with only its newly positioned
events, in the same context/transaction. The three internal occurrence receipt/membership/counter shapes and
`RuntimeHistoryOccurrenceEntityConfiguration` preserve first-observation membership, bounded scope ordinals,
unknown-baseline/semantics evidence and receipts through prefix content retention. They copy no journal content.
`StageDeleteAsync` joins full run retirement; the actual native deletion adapter still needs that binding.
All append owners, including independent observations, must stage their new events so the projection's covered
prefix remains coherent. A lagging or unsupported projection must not be advertised as an empty playable run.
All eight shapes join the shared Runtime model and additive migration chain; actual selected-provider
registration and native write/observation/deletion binding remain separate qualification steps.

`EfRuntimeHistoryObservation` is a private independent-observation staging helper. The eventual owner
authorizes the source, owns its transaction/final save and acknowledgement reconciliation, and normalizes
provider failures. The staged result is not a durable acknowledgement. The helper requires a retained
captured head and matching native policy, uses the same head reservation and occurrence prefix, and preserves
the last covered native checkpoint/time. Receipt replay is immutable; acknowledged purge can return a receipt
without recreating content. Its purpose-separated semantic fingerprint remains64 hex characters for every
provider. No bookmark lifecycle or occurrence is inferred from a delivery observation, and no provider port is
registered by this helper.

The isolated schema/mapping/checkpoint tests cover private projections and relational staging without enabling a selected history provider. The model/migration unit adds normal Runtime targets;
actual native entry-point/marker integration, scoped readers,
observation append, occurrence projection, retention/deletion, generated migrations and production qualification
remain open.

`EfRuntimeHistoryRetention` stages explicit prefix content retirement and full run retirement in the
caller-owned transaction. Prefix purge locks/checks the native owner, preserves admission/checkpoint
coverage and all semantic/occurrence receipts, and reserves the monotonic head boundary before a
set-based content update. Full deletion uses eight completely scoped set-based statements rather than
loading a long journal. It is called only after the native owner admits/locks full deletion, never by
inspection or prefix purge. Both reject pending history tracker changes and recheck immutable actual
access before returning; neither commits, acknowledges, authorizes a retention policy or registers a port.
The normal native deletion and independent retention owner still need their production binding.

Prefix retention requires ReadCommitted or Serializable isolation: an earlier InnoDB repeatable-read
view may outlive the current native write lock, and a no-op request would otherwise return an old
boundary without a CAS. The independent owner must choose an admitted isolation explicitly. Full
set-based deletion retains its caller's existing native deletion transaction; this read-view restriction
belongs to prefix boundary reporting. The actual MySQL regression establishes the older read view,
commits retirement from another context, and requires explicit refusal rather than a stale result.

The schema-binding unit registers all eight shapes in `RuntimeDbContext`, the one assembly-declared
`RuntimeHistory` family at1.0.0 and its eight whole ContentJson columns. All four provider models retain
bounded indexed hashes/keys and map complete unbounded identities/content to their large text type.
Official additive `RunHistoryTimeline` migrations must accompany these authored source changes before
merge or deployment; this intermediate source boundary is not a migration-ready release. The saved-legacy
upgrade control preserves every original table digest/history ID and requires empty new history storage,
so schema installation cannot fabricate capture or activity occurrence evidence. No capture feature or
provider capability is enabled by the schema declaration alone.

The native checkpoint binding calls history staging after its fence boundary and before incoming native
workflow state staging on every new physical commit, including missing Required contributions. All
history reservations and derived projections join the existing final participant/marker-last save and
ambiguous-acknowledgement reconciliation. Allocation-only contention leaves as cause-preserving history
unavailability after rollback/marker reconciliation; corruption and stale fences remain distinct. Actual
selected reader/DI/profile admission remains unadvertised until the separately owned read adapter and
observation/retention/deletion binding qualify. This direct-store source does not enable the public feature.

After checking immutable native admission, a confirmed uncaptured/no-contribution checkpoint returns
before accessing any history storage. Required/Optional captured obligations remain independent of
current module switches. Captured provider failures retain run/operation/cause after native rollback
and marker reconciliation; failed reconciliation preserves both write and lookup causes. Requested
cancellation, hard retained integrity, exact admission, model and stale-fence cases retain precedence.

The selected EF read adapter and its deferred SQLite read boundary are integrated from the reviewed
occurrence/ordinary-reader contributions. The existing public native checkpoint registration selects
that one scoped reader when no explicit history reader exists, replacing only the owned volatile
default. Explicit foreign registrations remain untouched. All read facets share the selected instance
and actual context/access/declared chain. No independent details/occurrence backend or capture feature
is added. Native SupportsHistory/pairing and independent mutation/deletion admission remain unadvertised
until their full provider binding qualifies.


The internal independent mutation owner implements the existing observation and retention ports with
one owned Serializable transaction on the same reader/context/access/chain. It refuses pending caller
work and caller/ambient/enlisted transactions. A result leaves only after a commit or exact persisted
receipt reconciliation. Purged replay retains its original acknowledgement without restoring content;
purge reconciliation pins the boundary actually staged and never broadens it to later arrivals. Failed
write and acknowledgement reads preserve their causes; access retirement and hard integrity remain
separate. This source does not register these ports or enable capture/deletion capability.
