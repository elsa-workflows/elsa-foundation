# Dashboard data model

**Status:** Data-model requirements for the implementation delivered through the [reviewable slice stack](delivery-slices.md). Resource schema and runtime behavior are implemented; final qualification, review, merge and resulting-main acceptance remain open.

| Entity | Essential fields | Invariants |
|---|---|---|
| Saved question | Opaque ID, tenant, identity-resolved owner, title/description, current content revision, source/schema/metric/query-profile references, query, visualization, typed parameters, timestamps | Current ACL and operation permissions; validated query/presentation; no stored result grants |
| Question revision | Question ID, monotonic revision, definition, actor/time, preceding revision | Immutable bounded history; current resource access governs history; restore creates a new validated current revision |
| Dashboard | ID, tenant, owner, title/description, revision, ordered tiles, timestamps | Private initially; layout saves preserve missing/unavailable references explicitly |
| Dashboard revision | Dashboard ID, revision, tile/layout snapshot, actor/time | Immutable definition history; no author results/prompts/credentials |
| Tile | Dashboard-local unique ID, question ID, semantic size, presentation/typed parameter overrides | Multiple tiles may reference one question; validate overrides and bind exact current question revision per execution |
| Audience grant | Resource kind/ID, tenant, canonical user/role, viewer/editor role, ACL revision | Owner alone shares/deletes; tenant-valid identity mapping; no public/cross-tenant links |
| Execution receipt | Exact definition revision, source/schema/metric IDs, effective windows/parameters, timestamp, availability/coverage/outcome, bounded values and drill references | Ephemeral viewer-scoped evidence, never persistent data authority or shared privileged cache |

Identity-owned mapping resolves actor/audience references. External `sub`, display name and raw Studio session subject are not interchangeable with canonical IAM user ID. Unsupported identity/audience mapping fails explicitly.

## Query and visualization

The [query contract](contracts/query.md) owns the typed grammar and bounds. Persist declarative source/field/metric identities, never executable SQL/scripts. Numbers, dates, instants and nulls are typed distinctly. Relative windows keep timezone/rule and resolve once per execution, with exact UTC intervals in the receipt.

Visualization is a supported kind (bar, line, table, list or metric), result-column bindings and bounded labels/formatting/options; no code, HTML or arbitrary authority-bearing URL. Tile overrides cannot bypass validation or mutate the saved question.

## Lifecycle and reuse

A local draft becomes a validated preview through current authorized execution. Explicit native Save creates/updates a durable definition. Editing/refining returns to a draft bound to an expected revision. Source removal/schema incompatibility makes execution unavailable without destroying the saved definition. Repair requires current definition access. AI-provider absence changes conversational authoring only.

Create uses a tenant/actor/resource-scoped operation key; exact retries return the accepted identity and incompatible key reuse conflicts. Update/restore/share/delete require expected resource/ACL revisions. Old content requests cannot restore removed grants. Storage commands return confirmation/revision tokens; readback is a separate query.

Tiles resolve the current saved question after current access checks, pin one revision per execution, and revalidate overrides. Question edits show accessible reuse impact and offer Save a copy. Old visible results become outdated; incompatible overrides require repair rather than silent substitution.

Unavailable tile projections use a tile-ID/revision-bound preserve-reference operation; the server retains hidden question bindings and overrides during layout saves/copies without exposing them. Partly withheld question definitions permit metadata-only preservation, not replacement or copy of an incomplete projection. See the [resource contract](contracts/resources.md#preserving-unavailable-content-during-edits).

A dashboard copy is private and retains explicitly identified question references. Independently copying an accessible question is separately authorized and creates new private ownership/history without copying grants/results/conversations. Later original-question revocation affects a shallow dashboard copy.

## Result dimensions and protection

Availability, coverage and refresh are distinct. A successful result may have unknown/incomplete coverage. No matching records differs from observed zero or a ratio without an eligible denominator. Failed refresh may retain clearly marked prior content only while current access still authorizes it. Identity/tenant/access changes clear protected prior results; definition changes mark them outdated.

The implemented Weaver authoring profile contains declarations and declarative proposals only. Protected result rows and aggregates remain in native viewer-scoped execution and are never included in provider context; retained proposals with a result preview are rejected. Any future result disclosure requires a reviewed original-evidence readmission design.

## Persistence boundary

Provider-neutral Dashboard contracts own commands/queries; the EF provider owns schema version/family, mappings, tenant predicates, concurrency and bounded history. Consumer-owned DbContext composition stays supported. Existing Home preferences/defaults remain in their current namespace, separate from these shared domain resources.
