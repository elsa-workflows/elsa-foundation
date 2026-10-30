# Data model: Elsa3JsonReconciliation (spec 235)

No persisted schema changes. Everything the feature writes goes through the collection import's commit and the
publishers. The types below are in-memory.

## Configuration

`Elsa3JsonReconciliationOptions` (public; bound from the feature section, validated at registration):

| Member | Type | Rule |
|---|---|---|
| `SourceId` | string | Required. Names the input in logs. |
| `FilePath` | string? | Exactly one of `FilePath`, `Files`, `FolderPath`. |
| `Files` | list of `{ Order, FilePath }` | Read in ascending `Order`. |
| `FolderPath` | string? | Top-level `*.json`, ordinal file-name order. |
| `FailOnUnconvertible` | bool | Default `false` (FR-015). |

## Input item

`Elsa3InputItem(string FilePath, Elsa3WorkflowDefinition Definition)`: one definition read from a file. Its key is
the import's source version id (`Elsa3WorkflowDefinition.Id`, the Elsa 3 version id) and its display identity is
`(DefinitionId, Version)`.

## Skipped item

`Elsa3SkippedItem(string Code, string FilePath, string DefinitionId, int Version, string Cause, string? BlockedBy)`.
`BlockedBy` is the source version id of the item it depends on, set for dependents (FR-014).

New codes (only where no existing code applies):

| Code | Cause |
|---|---|
| `ELS3-RECONCILE-DUPLICATE-INPUT` | A blank source version id, the same source version id more than once, or the same definition id and version more than once in the input. |
| `ELS3-RECONCILE-BLOCKED-BY-DEPENDENCY` | A reusable this item uses was skipped or not published. |

Every other skip carries the import's own code (`ELS3-*`, `VF-IMP-*`), including a reusable whose publication the
publish phase returns as `Refused` with its identity-collision code (research R-5): that reusable is skipped and its
dependents are blocked. Two refusals at apply time are not skips, because the import does not attribute them to an
item: a credential-literal refusal and an identity collision thrown by the commit. They fail the start (research R-5).

## Startup publishing grant

`Elsa3StartupPublishingGrant` decorates `IActivityPublishingAuthorizationContext` (scoped). A scoped holder in the same
DI scope carries an optional granted tenant. While it is set (only by the startup task, inside the operation scope the
persistence runner created for it), `TenantId` is that tenant and `CanAccessTenant(t)` is true for `null` and that
tenant. Otherwise every call goes to the inner context unchanged.

## Import outcome

`Elsa3ReconciliationOutcome` per persistence scope: imported activity versions, imported workflow versions, published
workflow versions, skipped items, conversion warnings. Logged as the summary line (FR-014).

## Additive members on the import (FR-010)

`ReusableActivityImportApplyRequest` gains two members, used only when `AccessScope` is null (the HTTP path always
passes an access scope, so it is unchanged):

| Member | Use |
|---|---|
| `string? TenantId = null` | Stamped on the mutation's activity definitions, versions, authoring states, workflow definitions and versions, as the scoped branch stamps the access scope's tenant. |
| `string? OperationKey = null` | The publish phase's operation key instead of the plan id, so publication idempotency keys are new on every start (research R-4). |

`ReusableActivityImportApplyResult` gains:

| Member | Type | Content |
|---|---|---|
| `Publications` | `IReadOnlyList<ReusableActivityPublicationState>?` | The publish phase's states, also when there is no receipt. |
| `Workflows` | `IReadOnlyList<ReusableActivityImportedWorkflowVersion>?` | One per committed workflow or wrapper version. |

`ReusableActivityImportedWorkflowVersion(string SourceVersionId, string WorkflowDefinitionId, string WorkflowVersionId, string Version)`.

Both default to `null`, so existing constructors and the HTTP response mapping are unchanged.

## Flow per persistence scope

```text
read files → input items ─┬─ duplicates / instance state → skipped
                          └─ analyze (plan) ─┬─ blocking diagnostics (+ dependents) → skipped
                                             └─ apply selection (tenant stamped, grant set) ─┬─ validation refusal → skip located items (+ dependents), apply again
                                                                                             ├─ credential / collision refusal → fail the start
                                                                                             └─ committed + reusables published
                                                                                                    └─ publish newest workflow versions (skip: already published once in its slot, reusable unpublished)
```
