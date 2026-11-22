# Workflow template management
**Status**: Draft — built-in starters amendment (2026-11-22)
**Date**: 2026-10-30
**Issue**: #3456
**Program**: none/free-flow

**Built-in starters amendment (2026-11-22)**: This in-place amendment is made at the user's request to let server modules contribute built-in workflow starters and let integrators remove them by id. Built-ins are listed through a separate read-only Design capability; custom template CRUD remains unchanged. This amendment describes proposed behavior and does not change the already-shipped baseline requirements below. The implementation PR that ships this amendment must restore the status to `Implemented`. The earlier description of a built-in as code-owned graph construction is superseded by the version-independent starter recipe requirements below.

## User Scenarios & Testing
1. An author saves the current workflow as a named template, finds it under Workflows → Templates, and edits it with the familiar designer. Graph, variables, inputs/outputs, layout and activity labels survive reload/restart.
2. An author chooses a built-in or custom template when creating a workflow. The resulting workflow is independent: later template edits or deletion never change it.
3. Built-in starters can be copied to editable custom templates, but never overwritten or deleted. Blank templates allow choosing the root activity. Missing activities produce a useful explanation instead of silently creating an incomplete workflow.

4. An author chooses Hello World Console, Hello World HTTP, Backend API Call, Scheduled job, Dispatch child workflow or BPMN process and receives an independent workflow that resolves the newest available versions of its activities.
   - **Independent test**: Choose each available starter and verify the resulting graph, activity versions and node identities; edit or remove the source starter and verify the created workflow does not change.
5. A module author contributes starters with their module, so only hosts that enable the owning module offer those starters.
   - **Independent test**: Enable and disable a module that contributes a starter and verify the starter appears and disappears from the server list.
6. An integrator removes unwanted starters by listing their ids in host configuration, without rebuilding Studio.
   - **Independent test**: Configure one known and one unknown id, verify the known starter is absent everywhere, and verify the unknown id produces one startup diagnostic without preventing host startup.
7. A template author exports a Studio template as a version-independent starter recipe that a module can embed. *(P3)*
   - **Independent test**: Export a template, resolve it against a different activity catalog version, and verify activity type references are retained while activity versions and node ids are resolved for use.

## Functional Requirements
- FR-001: Provide a persistent, tenant-scoped custom template catalog with name, description, authored state, layout and activity labels.
- FR-002: Expose list/read/create/update/delete through authenticated Design APIs. Reuse Design permissions, atomic persistence and optimistic concurrency; stale edits must not overwrite newer content.
- FR-003: Provide a searchable Templates management view with create, edit, copy, delete and create-workflow actions. Built-ins are visibly read-only.
- FR-004: Offer Save as template on an existing workflow, capturing current authored content including unsaved edits.
- FR-005: Reuse the workflow designer for template editing; templates cannot be published or executed as workflows.
- FR-006: Populate Starting point from the catalog; clone selected content and retain layout/presentation. Preserve blank-root selection and existing HTTP starter availability checks.
- FR-007: Creating/copying/deleting a template never changes previously created workflows. Templates never enter runtime executable/template stores or normal workflow inventories.
- FR-008: Handle unavailable APIs, stale revision conflicts, missing dependencies and failed saves without losing local edits. Built-ins remain usable on older hosts.
- FR-009: The server provides a read-only list of built-in starters and advertises it through its own Design capability link. Each listed starter includes a stable id, name, description, owning module, required activity types and expression languages, availability state, and an actionable reason when unavailable.
- FR-010: Enabled server modules contribute their built-in starters. A starter whose owning module is not enabled is absent from the list; it is not shown as unavailable.
- FR-011: Integrators can remove built-in starters by id through host-level configuration at `Workflows:Design:Templates:RemovedStarters`. Removed starters are absent from the server list and from Studio's Templates and Starting point views. Unknown ids are ignored and reported once as a startup diagnostic. If all built-in starters are removed, Blank workflow remains available.
- FR-012: Starter recipes reference activity types rather than pinned activity versions. Each use resolves the newest available version for each required activity and assigns fresh node ids. The resulting workflow is an independent authored workflow as required by FR-007.
- FR-013: If a required activity or expression language is unavailable, the starter is listed as unavailable with an actionable explanation and cannot be used to create an incomplete workflow. A missing owning module is handled as specified by FR-010.
- FR-014: Built-in starters cannot be updated or deleted through the template API. Authors can still copy a built-in into an editable custom template as specified by FR-003.
- FR-015: Blank workflow remains a client-side starting point with the existing root-activity choice. Server configuration cannot remove it.
- FR-016: If a server does not advertise the built-in starter list capability, Studio uses the current client-side HTTP starter, preserving the older-host behavior in FR-008.
- FR-017: The initial built-in catalog meets these acceptance criteria:
  - **Hello World Console**: a Sequence containing a WriteLine activity that writes `Hello World` to the host application's console.
  - **Hello World HTTP**: a Sequence triggered by HttpEndpoint at `/hello-world`, followed by WriteHttpResponse returning a valid JSON response.
  - **Backend API Call**: a Flowchart triggered by HttpEndpoint at `/products`, followed by SendHttpRequest issuing GET to an editable URL defaulting to `https://fakestoreapi.com/products`, then WriteHttpResponse returning `text/html` with a clean, styled product table rendered from the parsed response using Liquid.
  - **Scheduled job**: a Timer or Cron trigger followed by WriteLine.
  - **Dispatch child workflow**: a parent workflow that dispatches a child workflow.
  - **BPMN process**: a minimal BPMN process with a start event, task and end event.
- FR-018: Liquid bindings can parse a JSON response body into iterable data using a `parse_json` filter. Liquid filters implementing `ILiquidFilter` are registered with the binding-readonly handler, and values interpolated into HTML are escaped using Liquid's `escape` behavior.
- FR-019 *(P3)*: Studio can export a template as a starter recipe that references activity types without pinned versions and uses placeholder node ids that are regenerated when the recipe is used.

## Key Entities
Workflow authoring template: identity, tenant, revision, name/description and authored document/presentation. Built-in starter source: a module-owned contribution of one or more stable starter identities and recipes. Starter recipe: version-independent authored workflow content that references activity types and has node ids regenerated on use. Removal list: host-level ids that exclude matching built-in starters from the host's catalog. Workflow created from a template: independent design resource.

## Assumptions and Scope
Custom templates are mutable authoring snapshots, without a separate publication/version lifecycle; their CRUD contract remains unchanged. The existing HTTP starter keeps the stable id `http` and becomes Hello World HTTP. Removal is host-level and applies equally to every tenant. The server resolves recipe activity types against its own activity catalog and chooses the newest available version on each use. Built-in recipes can be copied into stored snapshots when customized. There are no runtime changes, per-tenant starter lists or removals, or automatic upgrades to workflows already created from a starter. Blank workflow remains client-side. Hosts without the capability retain the current client-side HTTP starter. The Backend API Call URL is editable; its third-party default may be unreachable or blocked by host outbound-HTTP policy, in which case workflow execution reports the HTTP activity's failure outcome.

## Success Criteria
- Custom-template create, save, edit, copy, delete and use actions survive a host restart; stale concurrent edits do not overwrite newer content, built-in mutation is refused, and workflows already created remain independent.
- With all owning modules and dependencies enabled, the initial catalog contains all six starters from FR-017. Each returned starter includes its id, owner, required dependencies and availability information; a disabled owner contributes no entry.
- Removing a known id excludes that starter from the server list and Studio, an unknown id does not prevent startup and is diagnosed once, and removing all six server starters leaves Blank workflow available.
- Each of the six available starters creates the graph defined in FR-017, resolves the newest available activity version, assigns fresh node ids, and remains independent from later source-recipe changes. A missing required dependency prevents creation and identifies the missing requirement.
- When the built-in list capability is absent, Studio offers the existing client HTTP starter. Blank workflow remains available with root-activity choice regardless of server removal settings.
- Given a valid product JSON response, Backend API Call returns `text/html` with one product row per item and escapes every interpolated product value. The behavior is verified with a local response fixture, independent of the public upstream.
- Existing light/dark template journeys preserve graph and declaration editing without template publication or run actions.
