# Contract: Activity Presentation Metadata

## Draft and definition APIs

Full-state workflow write commands accept an optional `activityPresentation` collection alongside
`state` and `layout`. Omitting the property preserves the current collection on partial-compatible
API commands; supplying it replaces the complete collection. Draft/version detail views always
return a collection, defaulting to empty.

Each item contains `nodeId`, optional `displayName`, and optional `description`. Servers normalize
surrounding whitespace, reject values above 200/2,000 characters, reject duplicate node IDs, and
omit items with no remaining value.

## Source Reference

Source References expose an optional `activityPresentation` collection whose items contain
`executableNodeId`, optional `displayName`, and optional `description`. The collection is a frozen
snapshot. Older sources with no property are valid and behave as an empty collection.

## Behavioral identity

Neither authored nor frozen presentation is a member of Execution Material. Changing only these
fields must preserve the executable artifact ID and hash.

## Label resolution

Consumers resolve a label in this order:

1. normalized authored/frozen Display Name;
2. catalog display name;
3. technical activity type.
