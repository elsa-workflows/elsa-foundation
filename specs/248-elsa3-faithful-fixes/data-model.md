# Data model: Elsa 3 converter faithful fixes

No new types and no stored-shape change. Two existing concepts change meaning:

| Concept | Where | Change |
|---|---|---|
| Member rule `WriteLine.Text` | `Elsa3BuiltInActivityMap` | R1 carry → R2 default flip with Elsa 3 default `""` |
| Member rule `customProperties.canStartWorkflow` (non-trigger) | normalizer | R3 removed input → consumed by start resolution, then dropped |
| Declared type of a typed value | `Elsa3ValueFlowLowerer.ReadType` | Elsa 3 alias → the Elsa 4 `TypeReference` (alias + `CollectionKind`) the value has after normalization |

Converted output: a WriteLine without text now carries `text` as the literal `""`; a status code name arrives as its
integer literal (already true before this unit). Nothing else in `WorkflowDefinitionState` changes.
