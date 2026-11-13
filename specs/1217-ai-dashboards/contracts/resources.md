# Dashboard and saved-question resource contract

**Status:** Resource requirements for the implementation delivered through the [reviewable slice stack](../delivery-slices.md); final qualification and delivery acceptance remain open. This contract serves [#3604](https://github.com/elsa-workflows/elsa-foundation/issues/3604) and the [feature specification](../spec.md). Source/public API files define implemented names and bounds. The owner confirmed separate Dashboards with Mine and Shared with me alongside unchanged Home on 6 November 2026. The owner selected the existing Copilot SDK adapter through backend options/configuration; ordinary resource persistence and refresh are native operations.

## Ownership and existing boundaries

The Dashboard capability owns named dashboard/question definitions, their lifecycle, grants, history, layout and native authoring controls. Dataset-owning modules retain field/metric semantics, query validation/execution and source authorization. Weaver remains the conversational host and uses module-owned resources/tools/renderers; this contract introduces no alternate conversation orchestration.

Proposed packages remain within the existing `Elsa.Studio` domain: implementation-free Dashboard contracts, native resource/API behavior, and replaceable persistence implementations. This is a proposed package boundary, not a new pinned root domain. Domain adapters depend on these contracts; Dashboard storage does not depend on Workflows Design or Runtime implementation types.

Existing [Home](../../../Studio/src/essentials/Elsa.Studio.Dashboard/README.md) continues using its current routes, contributed widgets and `home-layout` Studio Preference namespace. Named shared resources are not preference documents. They do not inherit device-local fallback, administrator Home defaults or client-supplied `X-Elsa-Studio-Host-Id` as an authorization boundary. Resources live in the authenticated tenant and the owning backend/store scope; a different Studio host does not grant access by choosing the same host ID.

The API uses the current native endpoint/capability publication and Foundation permission path, as illustrated by [StudioHomeLayoutApi](../../../Essentials/src/Studio/Preferences/Api/StudioHomeLayoutApi.cs). Tenant scoping, keyed creation, revisions, quotas and atomic audit follow the established [workflow saved-view persistence patterns](../../../Essentials/src/Workflows/Design/Persistence/EntityFrameworkCore/Stores/EfWorkflowLibraryStore.Views.cs). Its tenant-wide shared flag and ability for a general sharing permission to edit another person's view are not the proposed resource ACL policy here.

## Resources and readable projections

| Resource | Persisted definition | Reader-visible behavior |
| --- | --- | --- |
| Saved question | Server ID; tenant; immutable owner reference; name/description; versioned validated query and visualization; typed parameter definitions/defaults; dataset/schema/metric references; current resource revision; timestamps | Current definition plus effective capabilities and validation/availability status. No saved author result, model credential, provider transcript or raw preview records. |
| Dashboard | Server ID; tenant; immutable owner reference; name/description; ordered tiles; current revision; timestamps | Current layout and safe tile/reference projections. A tile whose question is inaccessible remains a generic unavailable placeholder without question name, source details or protected counts. |
| Tile | Dashboard-local stable ID; question reference; supported presentation and parameter overrides; semantic size | Independent instance identity even when a question appears twice. The effective query/result identifies the resolved exact question revision and override validation outcome. |
| Audience grant | Resource identity; tenant-local identity reference of kind user or role; capability level viewer or editor | Full grant list only for the owner through the sharing surface. Other readers receive their effective capabilities and a safe personal/shared state, not the audience directory. |
| Definition revision | Resource identity; immutable revision identity; bounded content snapshot; action/time; safe audit actor reference | Read through the current resource ACL and current field/definition disclosure policy. Historical ACLs never authorize current access. |

Resource names and descriptions are author-supplied shared definition content, not trustworthy module guidance. Share review makes that content visible to the owner; the service never enriches it with privileged preview summaries. Module validation decides which saved field identifiers and parameter/literal values are permitted as shared definition content. A data grant does not imply permission to disclose secret values in a definition.

The API separates definition access from data access. A recipient may read a shared dashboard while some tiles cannot run. A recipient may inspect/copy an otherwise shareable question definition but lack source access to execute it. Current source/field disclosure checks can withhold protected portions and return a meaningful unavailable state; responses never include a forbidden field/value merely because it appeared in an old definition.

### Preserving unavailable content during edits

A projected unavailable tile exposes its dashboard-local tile ID and safe layout only, with `referenceState: unavailable`; it omits the protected question ID and overrides. A layout update may submit that existing tile ID with `preserveReference: true`, size/order and the dashboard's expected revision. Inside the authorized write transaction, the server copies the question binding and hidden overrides from that exact stored tile. This marker is valid only for an existing tile of that same dashboard/revision; it is not transferable to another resource, cannot retarget a tile and cannot retrieve its hidden identity. Explicit removal of the tile remains permitted for a dashboard editor. A preservation save does not require disclosing or executing the hidden question; replacing the binding or overrides requires a currently readable, validated replacement.

Dashboard copy reads and copies retained bindings on the server under the source dashboard's current access/revision, generating new tile IDs without sending protected question IDs through the client. Hidden bindings remain unavailable in the copy. Generic full replacements cannot treat a redacted placeholder as a null reference or silently discard its protected content.

A partly withheld question projects `definitionState: withheld` and safe metadata/capabilities. It supports an explicit metadata-only update with `preserveDefinition: true` and exact expected revision; the server keeps the complete stored query, parameters and visualization. A replacement query must be fully supplied, currently disclosed/validated and explicitly requested. Do not merge an incomplete projection into a full replacement. Copy/restore of withheld definition content is unavailable until current disclosure checks permit the complete selected definition; metadata visibility alone cannot copy protected query values.

## Identity binding and effective capabilities

Use the normalized principal and the identity-owned stable actor mapping. [Foundation identity](../../../Essentials/src/Foundation/Identity/Core/Authorization/AuthorizationContracts.cs) distinguishes canonical `IdentityClaimTypes.UserId` from `NameIdentifier`/`sub`, which may be an external provider's subject. Never treat an external subject or display name as an IAM user ID. An identity integration must resolve an unambiguous tenant-local actor/reference; retain provider qualification where necessary. Unsupported or ambiguous mappings fail closed rather than matching a different user.

User grants bind stable canonical identity references; role grants bind stable tenant-local role identities, not mutable display names. The identity-owned resolver supplies effective membership and its propagation semantics. The existing [IAM stores](../../../Essentials/src/Foundation/Identity/Core/Iam/IamContracts.cs) provide canonical users, role IDs and bounded directory operations where supported. A token role string is not automatically an IAM role ID. If the active identity provider cannot resolve or enumerate an audience kind safely, discovery reports it unavailable and share validation rejects that kind; personal resources and other supported audience kinds need not become unavailable.

Both the feature's catalogued coarse permission and its resource ACL apply. Proposed permission families are `studio/questions:{read,create,edit,share,delete}` and `studio/dashboards:{read,create,edit,share,delete}`, contributed through Foundation `IPermissionContributor`; exact keys are provisional. Wildcard/coarse permissions do not by themselves turn an unrelated user into a resource owner. Current authorization uses Foundation's canonical request permission/resource path rather than trusting client `canEdit` flags.

| Action | Owner | Editor grant | Viewer grant |
| --- | --- | --- | --- |
| Read/list current definition and permitted history | Yes, with coarse read permission | Yes, with coarse read permission | Yes, with coarse read permission |
| Execute/refresh/drill/supported export | Only with independent current source/field/aggregate rights | Same | Same |
| Change content/layout or restore content revision | With coarse edit permission | With coarse edit permission | No |
| Change owner or tenant | No public first-release operation | No | No |
| List/change audience grants | Owner only, with coarse share permission | No | No |
| Delete original resource | Owner only, with coarse delete permission | No | No |
| Save a private copy | With destination create permission and permitted source definition disclosure | Same | Same |

The effective grant is the strongest current owner/user/role match; absence of a match denies definition access. An editor cannot submit ownership, tenant, ACL or audit fields through a content update. Newly created/copied resources are personal and owned by the authenticated creator; initial create does not accept caller-selected owner or grants. Ownership transfer, public links, cross-tenant grants and administrator takeover are outside the first-release contract.

Audience search is bounded and permission-filtered. Use existing identity directory permissions where applicable; do not grant general directory visibility merely because a person owns a dashboard. Inputs referencing another tenant or an undiscoverable principal receive a safe validation/denial result without confirming that principal's existence. Removed identities/grants do not transfer to a new identity reusing the same name.

Resource ACL changes are read from the current store for each operation. A committed grant removal or downgrade applies to the next resource operation, including history, copy, execution, drill-down and export. Membership/source-permission changes follow the configured identity provider's declared propagation boundary; distinguish live IAM evaluation from token-refresh-bound claims in UI/help and qualification. No cached author permission snapshot is authority.

## Proposed API operations

The routes below are provisional native API spellings. Publish capability relations so Studio follows the backend contract rather than assuming those paths. Definition operations and query execution have separate capabilities; refreshing a saved question never routes through an AI provider.

| Operation | Proposed surface | Required semantics |
| --- | --- | --- |
| List questions/dashboards | `GET /_elsa/studio/questions`, `GET /_elsa/studio/dashboards` | Bounded search; Mine or Shared with me; stable ordering; current ACL filtering before counts/paging. Return only safe summaries and effective actions. |
| Read current definition | `GET …/{id}` | Resolve tenant server-side; current resource and disclosure authorization; return revision/ETag and effective capabilities. |
| Create personal definition | `POST` to the appropriate collection | Authenticated creator; operation key; structural/semantic validation; private ownership; committed readback. |
| Replace content | `PUT …/{id}` | Exact expected revision; owner/editor plus coarse edit; allowlisted content only; atomic history/audit and new revision. |
| Delete definition | `DELETE …/{id}` | Owner plus coarse delete; exact expected revision; no cascade to separate resources. |
| Read/replace grants | `GET` / `PUT …/{id}/audience` | Owner plus coarse share; replace against exact current revision; validate all targets; atomically commit grants/audit/revision. |
| Copy definition | `POST …/{id}/copies` | Exact source revision; current source disclosure rights and destination create; keyed create; private destination; copy policy below. |
| List/read history | `GET …/{id}/revisions` and `GET …/{id}/revisions/{revision}` | Bounded metadata/content under current ACL and current disclosure policy; no historic ACL bypass. |
| Restore content | `POST …/{id}/restorations` | Owner/editor plus edit; current expected resource revision and selected historical content revision; revalidate; append a new current revision. |
| Audience candidates | Bounded capability-published user/role search | Supported identity kinds only; current tenant/directory permissions; do not expose arbitrary directory contents. |

Query discovery, draft validation/preview, saved-question execution, tile execution and drill/export belong to the companion query contract. Resource services provide the exact authorized definition/revision binding to those operations; they do not accept a saved author's principal or result payload.

An unauthorized/nonexistent/cross-tenant resource uses the same safe not-found response shape. An authenticated reader without edit/share/delete rights receives a denied action response without additional protected detail. Validation returns field-addressed problems; a missing precondition is distinct from a stale revision; an unavailable persistence provider is distinct from empty data. No API success is inferred from generated prose or a device-local write.

## Concurrency, idempotency and atomic persistence

GET returns an opaque current revision as an ETag. Content updates, audience replacement, restore and delete require one exact `If-Match` revision. Missing preconditions fail, and stale preconditions return a conflict/precondition failure with safe reload/copy recovery. The server never performs an unconditional last-writer-wins replacement. One shared resource revision covers content and grants so a concurrent revoke cannot be overwritten by an older content save.

Create/copy requests carry a bounded client operation key scoped by authenticated actor, tenant and operation kind. Persist its association and canonical request fingerprint atomically with the resource. A retry of the same key/request returns the same committed destination under current read authorization, without a second resource, history event or audit effect. Reusing a key with different input conflicts. Concurrent same-key creates converge on one committed result across supported stores. Keyed replay does not recreate a deleted destination or bypass a current ACL.

Within the write transaction/atomic protocol, recheck tenant, current revision, actor rights, grants and applicable quotas. Persist current content, its immutable history revision, audience changes when applicable, operation-key outcome and audit consistently. A provider must demonstrate these guarantees; an in-memory demo store alone does not establish durable restart or concurrency qualification. Use the repository's supported persistence participation/invariant patterns rather than embedding EF dependencies in `.Core` contracts.

Resource and history storage contain definitions/audit only. Do not persist query results, authorization snapshots, model sessions, secrets or credentials with them. Source query data continues to live in its owning module. Persisted timestamps and audit facts are server-issued. Current definitions and operation keys survive an owned host restart; no silent local fallback is used for either personal or shared named resources.

## Reuse, execution revisions and overrides

Dashboard references follow the current saved question revision the viewer can read. A tile does not silently fall back to an older accessible history revision if current question access or compatibility fails. Historical question inspection is explicit; first-release tiles do not pin historical question revisions.

Before updating a reused question, show the caller that it is reused and offer **Save a copy**. Reuse details are filtered by current dashboard access; the warning must not reveal inaccessible dashboard identities or counts. After a question update, visible results from its previous revision are labeled outdated and refreshed deliberately or through the configured bounded ordinary refresh policy. Never label an old result as answering the new definition.

Each execution binds one exact dashboard revision when applicable, tile ID, question revision, schema/metric references, validated overrides and parameter values. The client supplies the last observed revisions; if definition resolution has changed, return a definition-changed outcome and require reload/revalidation rather than mixing new definitions with old assumptions. Concurrent edits cannot change an in-flight execution's meaning. Its returned provenance identifies the executed revisions, and an older completion cannot replace a newer preview/result generation.

Presentation overrides may select only compatible supported visual types/options; parameter overrides bind only declared parameters with matching types and current bounds. Revalidate both after every question/schema change and before execution/save. An invalid override produces an actionable tile state. Do not silently discard an override, choose another parameter or use a default that changes the answer. A dashboard editor can adjust a valid tile override without thereby gaining permission to edit its shared question.

## Copy and history semantics

**Copy question:** create a new personal question owned by the copier from one exact currently readable source definition revision. It has its own ID, initial revision and future lifecycle, with no copied ACL, source history, author results or conversation. Current source/schema/definition disclosure checks still apply, and source-data rights are evaluated independently when the new question executes. Later changes or revocation of the original question do not alter the copy or grant source access.

**Copy dashboard:** create a new personal dashboard with new tile instance identities and copied layout/overrides, retaining its identified question references. This is not an implicit copy or widening of any question. Preview/explain the retained references and dependency access; perform separate explicit question-copy operations when the user wants independent questions and has the required rights. Inaccessible dependencies remain generic unavailable placeholders without disclosing question identity; copying the dashboard does not recover their contents. Later question edits follow the normal current-revision policy; revocation/deletion can make the copied dashboard's reference unavailable.

Historical restore creates a new content revision after current source/override validation. It does not roll the current owner, tenant or grants back to historic values, and it does not restore deleted source records. History is bounded; a pruned revision has an explicit unavailable/expired-history outcome for an authorized current reader, never a substituted revision. Historic metadata and payloads follow current disclosure policy before reading, copying, restoring or sending any material to Weaver.

No shared conversation history is created by sharing or copying a question/dashboard. Weaver context admission rechecks authority for retained context/results; an old owner preview cannot become shared evidence through a copied definition or historical revision.

## Deletion, missing sources and navigation

Deleting a dashboard removes that resource's ordinary readable definition/history/grants without deleting referenced questions. Deleting a question does not delete dashboards or tiles; their references become unavailable. A minimal operation-key/deletion record can prevent recreate-on-retry without making deleted content readable. Underlying audit retention follows the host policy and its own authorization; it is not a back door through the ordinary history API.

Owner confirmation explains known reference impact only within the caller's access, avoiding a global reverse-reference disclosure. The first release has no public undelete or ownership-transfer operation. A missing question and one hidden by ACL have the same safe tile-level unavailable message to an unauthorized reader. Owners who can inspect the reference may receive actionable removal/repair detail. Repair explicitly chooses an accessible replacement question and saves the dashboard; it never substitutes one automatically.

Data-module removal or incompatible field/schema/metric change leaves the saved resource recognizable and editable where current disclosure permits, with a validation/unavailable state and explicit repair/copy options. Removal or failure of an AI provider affects conversation availability only; ordinary definition reads, native editing, save and valid execution remain available.

Proposed Studio routes are `/dashboards`, `/dashboards/{id}`, `/questions` and `/questions/{id}` plus an authoring route for a new question. The separate Dashboards area is owner confirmed; exact routes are implementation details subject to router conventions. Existing `/`, `/dashboard` and `/overview` remain Home aliases with their current preference behavior. Direct navigation and refresh perform fresh API checks; URL IDs, current browser history and a previously mounted editor are not authority. Return-to-dashboard links retain a safe local destination and must not carry protected result data in URLs.

## Proposed resource bounds

These are reviewable initial defaults, independent of the query contract's execution bounds. The host may lower them; capability metadata and errors expose effective limits before a user loses work.

| Bound | Proposed initial maximum |
| --- | --- |
| Name / description | 200 / 2,000 characters |
| Question definition / dashboard definition | 64 KiB / 128 KiB encoded content |
| Tiles per dashboard | 50, with unique tile IDs |
| Audience grants per resource | 100 distinct user/role references; no nested audience expressions |
| Owned questions / dashboards per actor and tenant | 100 / 50 |
| Tenant resources | 5,000 questions / 2,000 dashboards; explicit capacity failure rather than partial list results |
| Retained content revisions | Current plus 20 prior revisions per resource |
| Collection/history page size | 1–100; stable ordering and opaque scope/query-bound continuation |
| Client operation key | 1–128 characters under a documented safe encoding |

Apply bounds server-side before persistence and transactionally for quota races. Reject a whole invalid update with field-addressed problems; never partially apply grants/tiles or silently truncate definitions. List counts describe only the authorized query universe. Tenant quota errors reveal capacity state without exposing other resource owners, names or audiences. Malformed IDs, duplicate tile IDs/grants, unsupported resource versions and unknown mutation fields receive meaningful validation outcomes.

## Required contract witnesses

Qualification must prove owner/editor/viewer and coarse-permission combinations, same-name identities across tenants/providers, canonical audience binding, permission propagation, current ACL revocation, protected history, copy/reference behavior and removed questions. Exercise two writers, content-versus-grant races, same-key concurrent create/replay, mismatched-key payloads, quota races and restart persistence on supported stores. Verify one exact definition revision per execution, current-question changes with incompatible overrides, no silent fallback, and no author results/ACL/history/transcript in copies. UI acceptance remains **incomplete** until the [composed journeys and independent inspection](../ux-acceptance.md) are performed on the implemented current head.
