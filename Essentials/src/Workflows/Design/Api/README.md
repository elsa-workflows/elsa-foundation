# Workflow Design API

`Elsa.Workflows.Design.Api` is the supported management-client slice for authored workflows. It owns workflow definitions, first-class drafts, immutable Design versions, scoped-variable analysis, and context-sensitive activity input options. Publishing and executable inspection belong to their respective domain APIs.

The ownership and lifecycle rules are specified in [`specs/220-domain-owned-apis`](../../../../../specs/220-domain-owned-apis/spec.md). Terms such as *feature*, *API capability*, *draft*, and *workflow executable* are defined in the [Elsa glossary](../../../../../docs/glossary/elsa.md).

## Composition

Add `WorkflowsDesignApiFeature` to the active shell and compose a Workflow Design persistence provider. The feature registers its Minimal API mapper, mediator handlers, scoped-variable authoring services, and contextual input-option resolver. Activity input options also read the Activity Design definition/version stores, so a host exposing that operation must compose Activity Design persistence. The feature depends on `WorkflowDesignValidations`, which registers the credential-literal rule its state-writing routes apply (below).

## Credential literals (spec 188)

Every route that writes workflow state (Definitions/Add, Drafts/Replace, Versions/Add, Definitions/Submit and
Drafts/Promote, and the `UpdateDefinition` mediator command) admits the state it writes through the credential-literal
rule before its command runs. A literal, an object, a value read or an expression on an input its activity declares a
credential is refused with 400; `errors` holds one entry per refused binding, keyed `{nodeId}/inputs/{referenceKey}`,
each message starting with the rule id `Inputs/CredentialLiteral` and never carrying the value, and nothing is stored.
This blocks a draft save, the one deliberate exception to draft saves recording validation errors without blocking.
Drafts/Promote admits the stored draft it reads and promotes exactly that content: when the draft changed before the
promotion lock was taken, it answers 409. When the draft no longer exists there is nothing to admit, and the promotion
command answers: a replay of a promotion that succeeded returns the original version, and a first promotion is 404.
A node of an activity the catalog does not hold cannot be judged here and is stored; publication refuses it.

This package does not reference or depend on `Elsa.Workbench`; the server application is only one possible reference composition.

## Supported routes

All routes are relative to the active shell route base.

| Area | Routes |
|---|---|
| Definitions | `GET/POST /design/workflows/definitions`, `POST /design/workflows/definitions/submit`, `GET/PATCH/DELETE /design/workflows/definitions/{definitionId}`, `POST .../{definitionId}/restore`, `DELETE .../{definitionId}/permanent` |
| Drafts | `GET/PUT/DELETE /design/workflows/drafts/{draftId}`, `POST .../{draftId}/promote` |
| Versions | `GET /design/workflows/definitions/{definitionId}/versions`, `GET /design/workflows/versions/{versionId}`, privileged `POST /design/workflows/versions/ingest` |
| Authoring | `POST /design/workflows/scoped-variables/analyze`, `POST /design/workflows/activities/{activityVersionId}/inputs/{inputName}/options` |
| Authoring schema | `GET /design/workflows/definitions/submit/schema`, `GET /design/workflows/structures` |

Definition list and detail views expose optional `publicationState` (`draft`, `published`, `retracted`, `retired`), `publishedVersionId` and `publishedVersion` when the host composes Publishing's `IWorkflowDefinitionPublicationStateLookup`. Design-only hosts leave publication facts unset; clients should display unavailable publication information rather than infer it from saved versions, which survive unpublish. For publication selection and journal capability rules, see the [lookup contract](EXTENSION_POINTS.md#iworkflowdefinitionpublicationstatelookup-contract--elsaworkflowsdesignpersistencecore). In particular, no-slot definitions remain unresolved on the EF ledger until its definition-keyed journal lookup is implemented in [#2801](https://github.com/elsa-workflows/elsa-foundation/issues/2801).

`latestVersion` identifies the latest immutable saved version. A mutable draft has no assigned semantic version until promotion; `draftSourceVersionId` and `draftSourceVersion` identify its saved base when available. Clients can present the draft and its base separately from the currently published version.

Definition reads and authoring analysis require `workflows/design:read`; mutations and direct ingestion require `workflows/design:manage`. The shared wildcard permission remains supported. Authentication and RFC 7807 error handling come from the shared identity and ASP.NET Core endpoint conventions. Context-sensitive option responses are non-cacheable.

The authoring-schema endpoints make the design API a complete headless authoring surface (issue #1164). `GET .../definitions/submit/schema` returns a versioned, fingerprinted JSON Schema for the submit body, generated at request time from the actual wire types so it cannot drift from the contract. `GET .../structures` enumerates the composite-activity structure kinds registered in the active shell (`IActivityStructureHandler` set) with each kind's `schemaVersion`, scoped-variable support, and a generated JSON Schema of its authored payload. Polymorphic argument values and activity-owned structure payloads intentionally render as unconstrained schemas in the submit-body document; per-kind payload shapes come from the structures endpoint instead.

## Extension points

### Optional workflow organization

`WorkflowsDesignApi.WorkflowOrganizationEnabled` is a Boolean setting that defaults to `true`.
An administrator can change **Workflow organization** in Studio's **Features → Server →
Workflows Design API → Edit settings**, save the draft, review the single-setting change and Apply.
The review targets the selected backend. Hosts whose configuration is owned by another source
must change that source instead. The same setting can be supplied in a shell blueprint:

```json
"WorkflowsDesignApi": { "WorkflowOrganizationEnabled": false }
```

Turning it off withdraws folder, placement, tag, organization, saved-view and favorite relations.
Their endpoints return HTTP 501; active organization predicates on the paged list return HTTP 400.
The legacy list also refuses organization query keys while disabled, rather than ignoring them.
Creation with a folder destination is refused before writing. Ordinary paged browsing, search,
sort, publication filters, authoring, publishing and runtime execution remain available.

The library store and its visibility policy stay registered. Definition/folder resource checks,
tenant isolation, visibility-bound counts and continuation tokens retain their normal behavior.
Organization metadata and mutation permission hints are omitted/false while disabled. This is an
availability control, not an alternative authorization policy.

No folder, tag assignment, saved view, favorite or column preference is deleted or migrated.
Re-enabling restores them, including after a restart. Each shell generation uses a fixed setting
snapshot. After a successful reviewed Server Apply, Studio refreshes capabilities for that backend
and retires the old route/dialog generation. A stale organization-filtered link holds browsing
until the user explicitly clears unavailable predicates; recovery retains supported ordinary
filters, backend selection and saved column choices. Clients should discover each operation's
relation independently. Paged browsing does not depend on organization being enabled. A composed
library store is still required; enabling the setting does not add storage to an unsupported host.

`workflow-definitions-query` advertises the extended paged query grammar: tag conditions,
descendants, favorites, sort and publication-state filtering. It remains available when organization
is disabled, while the disabled organization predicates are still refused. The older
`workflow-definitions-page` relation alone does not establish support for these options. Studio
accepts the original `workflow-organization` relation as the compatibility signal on older
library-capable servers, then checks the specific folder/tag/favorite operation as appropriate.

Workflow library pages enforce visibility before counting and paging. A host that registers any
`IPermissionResourceHandler` must also register an `IWorkflowLibraryVisibilityPolicy` whose
server-derived revision and bounded allow/deny definition identities describe the complete workflow
visibility for the current normalized principal. Without that integration the page returns HTTP 403
before reading the inventory, including when the handler is registered for another resource domain.
Tenant/action-only hosts without resource handlers use the unrestricted tenant scope. The read scope
never comes from a request or saved view; its revision/membership is bound into continuation tokens.
The visibility policy is an optional single-implementation contract. Multiple registrations fail
shell startup with a diagnostic rather than selecting the last registration. Definition details and
organization responses also authorize the definition and every returned folder/ancestor; a denied
resource produces HTTP 403 without returning its organization metadata. Definition mutations
preflight that read decision before committing. Replacement details readers can implement
`AuthorizeReadAsync`; its compatibility default reads the details, while the built-in reader
checks only organization metadata.
`IWorkflowDefinitionAuthorizer` in Workflow Design Core exposes the same current per-definition
read/manage decisions to other participating modules. Its HTTP implementation checks typed native
resources and tenant-scoped existence; definition GET honors the selected replacement before
returning details. See the [owner authorization contract](EXTENSION_POINTS.md#iworkflowdefinitionauthorizer-contract--elsaworkflowsdesigncore).
See the [visibility contract](EXTENSION_POINTS.md#overridable-contracts).

See [EXTENSION_POINTS.md](EXTENSION_POINTS.md) for persistence ports, structure handlers, and input-option providers. API clients should use the published HTTP contracts rather than implementation services.


### Workflow authoring templates

When `IWorkflowTemplateStore` is registered, the workflow-design capability advertises:

| Method | Route | Permission |
| --- | --- | --- |
| GET / POST | `/design/workflows/templates` | Read / Manage |
| GET / PUT | `/design/workflows/templates/{templateId}` | Read / Manage |
| POST | `/design/workflows/templates/{templateId}/delete` | Manage |

POST creates a custom snapshot; PUT updates it. Mutation bodies carry `operationKey` for atomic
replay; updates and deletion require `expectedRevision`. A stale revision returns HTTP 409.
Save bodies contain name, description and content (state, layout, activityPresentation).
Existing credential-literal admission applies. All reads/writes are tenant-scoped.
These resources are independent of workflow publication and runtime execution. Built-in recipes
are read-only client resources; their identifiers cannot be overwritten through these endpoints.
See [the extension catalog](EXTENSION_POINTS.md#workflow-authoring-templates) for replacement semantics.


Custom catalogs support 1000 templates per tenant. Creation uses the host's existing
`IDistributedLockProvider` to serialize catalog admission and checks capacity inside
the atomic write; a full catalog returns HTTP 400 before insertion. Replaying the
last successful create, reading, updating and deleting remain available at capacity.
The optional API returns HTTP 501 when no template store is configured.
