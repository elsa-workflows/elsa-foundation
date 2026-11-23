# Contract: history queries and Studio inspection

Applies to FR-001–023, FR-030–042 and FR-045–046. Exact request/view names and public API baselines are resolved in implementation; these behaviors are fixed for the cross-layer boundary.

## Capability and authority

Extend the existing versioned runtime capability family with optional history page, exact event, activity bookmark inventory and exact bookmark lifecycle relations. Advertise only actual support. No Studio-hardcoded URL construction.

Every read resolves the run and enforces runtime:read plus structure authorization in its runtime persistence scope. Reauthorize direct links/cursors/related runs. Missing and denied resources retain existing equivalent response behavior. Independent value-inspection/payload-resolution grants and audit remain unchanged; history never automatically resolves a payload.

## Page

Inputs: exact run, optional exact execution with explicit descendant scope, bookmark/kind/category/identifier filters, free text, bounded page size and authenticated continuation. A new traversal captures an upper sequence watermark; pages advance by sequence, not offset or timestamp. Exact event navigation supports a retained target absent from the current page/filter.

Outputs: stable records and logical/physical group context; capture support, from-start/late/gap/disabled/purged/invalid coverage; first/last/settled sequence and retention boundary; next traversal/catch-up continuation. Default page size follows existing 100 and shared maximum 500. Qualification uses at most 100. Optional source settlement is not workflow completion.

Each multi-query read must use one committed provider snapshot for the head, selected rows, ancestry and counts. Inspection must not reserve a provider's writer slot merely to read. SQLite uses a deferred Serializable transaction; native-run access still precedes head/projection access. Other providers retain their existing isolation and caller-transaction rules. This does not weaken atomic writers, fencing, deletion/purge serialization or failure classification. Held-writer and concurrent-commit controls must prove both read progress and a coherent old snapshot, followed by catch-up to the committed append.

Bind purpose-separated continuation payloads to exact run, filters, limit, watermark, scope, tenant, audit subject and authorization profile using the existing authenticated runtime codec. Tokens are not authority. Invalid or mismatched continuations fail without exposing another range.

Exact recorded IDs use indexed paths. Free text matches safe common fields in a bounded ordered search window; return searched-through position and whether more search range remains. UI says “no matches in this searched range” when appropriate and can continue; do not claim global absence or exact full filtered totals. Active presentation counts describe loaded/known ranges accurately.

Use a history-specific scanned-window page: zero matching records with a strictly advancing searched-through cursor and more range is valid. Do not reuse RuntimeStorePage's empty-items/no-continuation invariant for this search shape. Every continuation must advance or terminate, with no same-window loop.

## Event and bookmark details

Event details expose safe common metadata, exact owning execution, observed/committed instants, local sequence/group/ordinal, source authority, causal links and existing phase-specific value references/dispositions. Unknown kinds/versions retain readable common metadata; invalid content is an explicit limitation.

Bookmark inventory is bounded by creation-sequence keyset and scoped to exact ownership. Lifecycle includes historical handles, active/lifetime counts, creation and final events, reasons, safe source/target and related delivery/continuation. Current eligibility is evaluated separately at server observation time. Missing physical handles never establish consumption.

If capture begins after creation or crosses an explicitly recorded gap, a later consumption/removal can create an Unknown-prior-state lifecycle entry with its actual first-observed sequence. It must not require an existing creation projection or fabricate a Created event, creation time or lifetime-total completeness.

## Studio behavior

Timeline is the default primary view for a direct run; explicit supported view selection is preserved. Diagram remains a peer showing labeled latest/pinned context. Primary view, selected execution, selected event/bookmark and explicit timeline filter are separate state.

Retain existing exact execution, ancestry, iteration/attempt navigation and legacy selected/activityExecutionId/tab links. New event/bookmark/view links preserve unrelated parameters and do not select “latest” when the exact target is missing. Following an event outside filters discloses scope and supports Back.

Activity summary plus Journal/Bookmarks/Data uses the same history IDs/order. Descendants are opt-in. Data uses existing value-policy components; later values do not impersonate earlier phases.

History observation has its own bounded context-keyed serial query/catch-up lifecycle. It preserves rows with stale/error state after transient failures, aborts superseded reads and clears protected state when backend/auth context changes. A settled watermark is a stable prefix, not permanent closure; visible live history continues bounded catch-up through future waits and late delivery observations unless explicitly paused/closed. Workflow terminal status alone cannot stop pending reads. New events do not move a reader examining earlier history.

Highlights retains important wait/bookmark/delivery/fault/recovery milestones; All events exposes all retained authorized records in scope. Text and kind icons carry meaning alongside restrained public design tokens. Narrow layout stacks timeline then inspector. Loading/empty/filtered/denied/unsupported/partial/stale/failed/missing-target states must be explicit and keyboard usable.

## Acceptance

The selected design PNG is reference only. Actual normal-host user-task wait/resume, continuation-fault/duplicate/expiry/partial cases, fresh-process durable restart, exact deep links, bounded long-history reads, protected access, keyboard and 320px/desktop light/dark evidence must be source-bound, downloadable and independently inspected before product acceptance.
