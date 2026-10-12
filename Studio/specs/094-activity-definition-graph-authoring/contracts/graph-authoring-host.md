# Contract: Graph Authoring Host

## Resource kinds

```ts
type GraphAuthoringResourceKind =
  | "workflow-definition"
  | "activity-definition-graph";
```

## Controlled workspace

The workspace receives:

- current controlled graph document
- a document adapter for root, layout, scope, and expression references
- authorized activity catalog
- property and expression editor contributions
- host-level contributions filtered by resource kind
- read-only/disabled state and reasons
- current selection and scope path
- callbacks for edit, selection, scope navigation, undo, redo, diagnostic focus, and status announcement

The workspace emits:

- complete document edits through one `onChange(nextDocument)` boundary
- selection/scope changes that do not independently persist
- explicit history commands
- focus/status announcements

The workspace MUST NOT:

- fetch, replace, or save a resource
- create definition revisions
- invoke migration, publication, or test APIs
- assume workflow-definition metadata
- strip unknown provider or layout fields

## Contribution filtering

Host-level contributions declare:

```ts
supportedResourceKinds: GraphAuthoringResourceKind[];
```

The host instantiates a contribution only when the active resource kind appears in that list. Existing node property and expression editor registrations are shared without duplicating them under this contract.

## Activity host

The Activity Definition host:

- supplies `activity-definition-graph`
- adapts provider payload/layout
- passes public inputs and graph variables as expression references
- persists workspace changes through the existing exact-revision provider `onChange`
- owns conflict, recovery, validation, proposal, migration, publication, and test controls

## Workflow host

The Workflow Definition host:

- supplies `workflow-definition`
- adapts workflow root/layout
- retains workflow metadata, persistence preferences, publish/promote, artifacts, Weaver, import/export, and workflow test-run controls

## Compatibility

The initial extraction must render the existing workflow designer with no observable lifecycle or interaction change. Activity Graph schema 1 may use the workspace only for operations representable in schema 1 and must advertise explicit migration when a schema 2-only operation is requested.
