# Data Model: Activity Instance Metadata

## ActivityPresentationRecord

Typed design metadata owned by a workflow draft or version.

| Field | Type | Rules |
|---|---|---|
| `NodeId` | string | Required; unique within the owning presentation collection |
| `DisplayName` | nullable string | Trimmed; absent when blank; maximum 200 characters |
| `Description` | nullable string | Trimmed at boundaries; internal line breaks preserved; maximum 2,000 characters |

An empty record is omitted. Deleting a node prunes its record. Duplication copies values under the
new node ID. Version replacement preserves the record for the same node ID.

## Draft/version design metadata

`WorkflowDefinitionDraftLayout` and `WorkflowDefinitionVersionLayout` retain geometry records and
gain an `ActivityPresentation` collection. Promotion copies both collections atomically. Existing
documents without the property deserialize to an empty collection.

## WorkflowExecutableActivityPresentationRecord

Immutable historical projection stored on `WorkflowExecutableSourceReference`.

| Field | Type | Rules |
|---|---|---|
| `ExecutableNodeId` | string | Required; unique in the flattened source graph |
| `DisplayName` | nullable string | Frozen normalized authored value |
| `Description` | nullable string | Frozen normalized authored value |

The collection is optional/empty for older sources. It is not part of Execution Material or
`ArtifactHash`.

## Studio model

`ActivityPresentationMetadata` mirrors the authored record:

```ts
type ActivityPresentationMetadata = {
  nodeId: string;
  displayName?: string;
  description?: string;
};
```

The workflow draft signature includes normalized presentation metadata so autosave and undo/redo
observe edits. Read-only executable graphs join frozen records by executable node ID.

## State transitions

```text
new node -> no record
edit -> normalized record upserted
clear both fields -> record removed
duplicate -> values copied to new NodeId
change activity version -> record retained
delete/replace node -> record removed
promote/publish/Test Run -> immutable executable-node snapshot
```
