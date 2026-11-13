# Activity-occurrence read facet (S14a)

Related: [history inspection](history-inspection.md), [atomic history](atomic-history.md), and [implementation handoff](https://github.com/elsa-workflows/elsa-foundation/issues/3849#issuecomment-6858916502). Supports playback #4366; neither feature is complete through this supporting slice.

## Provider boundary

`IRuntimeHistoryOccurrenceStore` is an optional facet of the actual selected history reader. One `ReadOccurrencesAsync` operation admits a frozen segment or reads a bounded page within it. It is a read projection of the existing journal, not another capture pipeline or execution authority. Existing history interfaces/constructors remain unchanged.

The immutable query carries the exact run, optional exact execution scope with explicit descendants, limit1–100 (default50), optional signed sequence token, and either a zero-based ordinal or exact occurrence anchor. The server supplies the tenant/subject/authorization-profile binding; tokens never authorize access. A read returns exact first-observation identities/positions, adjacent occurrence identities, coherent known count or explicit unknown, selected position, coverage/head, retained bounds, sequence token and separate newer-history indication. The page is centered around the requested target without requiring preceding event pages.

Membership is one entry per exact activity-execution identity, ordered by its first eligible committed observation sequence. The closed schema-one eligibility rule lives in [RuntimeHistoryOccurrence](../../../../Essentials/src/Workflows/Runtime/Core/Models/RuntimeHistoryOccurrence.cs). Completion can be the first observation for an intrinsic activity. Wait/resume facts do not create additional occurrences for the same execution. The observation kind/time remains visible; unknown occurrence time does not become recording or repair time. Parallel adjacency establishes no causal edge.

A window excludes later appends/materialization. Its token binds the run, scope, limit, access context, upper/purge bounds and policy identity. Changed authorization/filter/purpose rejects the token. Retention/deletion or changed policy invalidates the window, never silently rebases ordinals. Explicit refresh admits new bounds and may locate an eligible exact anchor. A known count describes the retained segment, not full-run capture completeness or source closure.

## In-memory projection

First-occurrence identity/position receipts and ordered scope indexes are derived during existing history publication under its checkpoint transaction/lock. They copy no event content and allocate no new journal positions. Exact receipt lookup and binary bound lookup support distant ordinal/anchor reads independently of preceding lifecycle volume. Replay cannot introduce another occurrence. Run deletion removes these receipts/indexes.

The first observation's proven ancestry fixes scope membership. Unknown ancestry produces explicit unavailable descendant admission; a known root is distinct from an unknown parent. Later parent enrichment cannot change an admitted sequence. This conservative implementation does not repair unknown first-observation ancestry into playable descendant scope.

Identity receipts survive prefix content retention so a later continuation cannot reintroduce a purged occurrence. The reader returns only first observations above the retained prefix. Changing that prefix invalidates an existing token. Unknown lifecycle vocabulary/schema blocks occurrence admission explicitly, including after content purge; normal event inspection remains readable. A global unlocated gap cannot establish traversable subsegments and returns unavailable.

## HTTP and capability

The additive `workflow-history-occurrences` relation resolves to `GET runtime/workflows/instances/{workflowExecutionId}/history/occurrences`. Advertise it only when the selected reader implements the facet and the actual selected atomic writer qualifies that reader. Unsupported EF/custom readers do not gain the relation by implementing ordinary history APIs.

The route requires runtime read permission and reauthorizes structure before projection access. It rechecks structure/profile after a pending read and never resolves protected value payloads. Invalid provider identity/order/target/window returns invalid coverage without rows. Unsupported, unlocated-gap, unknown-scope, invalidated and unavailable-target results remain distinct; unsupported is not a playable empty run.

All64-bit sequence, ordinal, count and coverage positions are decimal strings in this additive view. Existing history wire contracts are unchanged. Unknown count/time/settlement remain explicit nulls. No protected value payload or arbitrary event metadata is copied into an occurrence entry.

## Qualification boundary

Written controls cover seeded projection ordering/identity,200-occurrence seeks, frozen appends/ancestry, prefix invalidation, unknown semantics, token purposes/context, deletion/cancellation, logarithmic bound probes, actual in-memory commit replay/validation atomicity, and mapped authorized HTTP reads/access retirement/string wire values. They remain **NOT RUN** until an authorized execution receipt says otherwise.

This slice supplies no EF projection/migration, retention mutation, source-settlement owner, enabled public history module or composed-host acceptance. Those remain with #3849 S11–S14 and production qualification. Neither memory controls nor an HTTP projection fixture closes playback T003's durable provider/restart/retention/authorization/composed-host gate. The accepted handoff grants no local .NET/browser campaign, Azure restart or manual-dispatch/protection exception.
