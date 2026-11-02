# Runtime operations

Extend existing authorized BPMN routes/advertised relations with domain catalog, inspection and claiming. No generic model-management API.

## Reads

- Catalog pages default50/max500 underlying rows. Apply normalized identity, persistence scope, tenant, protocol and resource permission before each disclosed item. Foreign/missing resources remain indistinguishable.
- One raw page per request; transport permits empty filtered items+continuation. Protect purpose, subject, tenant, scope, filter/page size, raw cursor and original source live time. Invalidate on scope/filter changes. No total or cross-request snapshot promise.
- Inspection exposes exact graph/pin/source, lifecycle, current work, persisted original/replacement service attempts/incidents, outcomes and owed settlement. Authorize before retained command-result access. Do not invent node history.

## Writes and replay

Existing start/unclaimed completion/retry/cancel keep signatures/payload fingerprints. Same operation content recovers durable outcome after uncertainty/restart; changed content conflicts. Pending remains Pending. Browser refresh never silently creates a replacement operation identity.

Claim/claimed-completion includes exact invocation/attempt/claim identity. Capture normalized subject server-side and recheck `bpmn:complete-human` resource permission. Never trust client actor/tenant/Authority. Existing mailbox/revision CAS/atomic checkpoint+receipt chooses one owner. Wrong subject, stale attempt or interruption cannot advance/resurrect work.

Private adapter format15 wraps legacy document+claims. Claim-required human material uses explicit format6/profile. Older readers refuse both; new readers accept old formats unchanged. Do not rewrite Authority. Keep exact old completion payload; claimed completion has a new input kind. No timed leases, teams or separate claim table.

## Capabilities/admission/errors

Advertise runtime catalog/inspection/claim support separately from optional authored publication and current admission. New claim-required admission/legacy upgrade is opt-in. Admission-off still permits supported inspection/claim/completion/settlement of accepted work. Old unclaimed completion stays supported.

Retain missing/forbidden/validation/conflict boundaries without leaking foreign resources. Infrastructure errors retain contextual domain causes and do not become semantic success. Reauthorize every read/write/replay.
