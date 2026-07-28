# Data Model: Expression Code Intelligence Foundation

## Ownership Map

| Entity | Owner | Persisted | Purpose |
|---|---|---:|---|
| `ExpressionAuthoringContext` | `Elsa.Workflows.Design.Core` | No | Revision-bound metadata snapshot for one expression location. |
| `ExpressionAuthoringDocument` | `Elsa.Expressions.Core` | No | Stable identity of type, location, source revision, expected type, and source text for one request. |
| `ExpressionSymbol` | `Elsa.Expressions.Core` | No | Caller-visible name/member/callable metadata. |
| `ExpressionValueShape` | `Elsa.Expressions.Core` | No | Language-neutral structural type/collection/nullability/reference facts. |
| `ExpressionToolingOutcome<T>` | `Elsa.Expressions.Core` | No | Explicit version/revision/state envelope. |
| `ExpressionDiagnostic` | `Elsa.Expressions.Core` | No | Stable range/path/severity/code feedback. |
| `ExpressionToolingProvider` | Expression type module | No | Cancellable language semantic contributor. |
| `ExpressionValidationSummary` | `Elsa.Workflows.Design.Validations` | No | Full-draft aggregation passed into existing validation errors/gates. |

## `ExpressionAuthoringDocument`

| Field | Rules |
|---|---|
| `DocumentId` | Stable opaque id derived from draft + activity node + property/input + expression type; never a runtime execution id. |
| `WorkflowDraftId` | Required Design draft identity. |
| `NodeId` | Required activity node identity. |
| `PropertyKey` | Required authored input/property key. |
| `ExpressionType` | Required expression descriptor/provider key. |
| `DocumentRevision` | Required opaque revision of submitted or current authored source. |
| `ExpectedResultType` | Optional language-neutral type reference; affects rank/validation but does not hide valid symbols. |
| `Source` | Request-only authoring source; not persisted, cached cross-session, logged, or returned unless caller supplied it. |

## `ExpressionAuthoringContext`

| Field | Rules |
|---|---|
| `ContractVersion` | Required semantic contract version; unknown mandatory major is incompatible. |
| `Document` | Required `ExpressionAuthoringDocument` identity excluding arbitrary execution data. |
| `ContextRevision` | Required opaque fingerprint of the policy-filtered design facts. |
| `SymbolCatalogRevision` | Required opaque catalog revision for stale-result handling. |
| `ExpectedResultType` | Same declared Design type represented in a language-neutral form. |
| `RootSymbols` | Bounded first page of visible symbols, ordered deterministically. |
| `Capabilities` | Flags/limits for paging, children, completion, hover, and validation; no feature-name inference. |
| `PolicyFingerprint` | Opaque diagnostic/debug correlation only; contains no permissions or secret policy values. |

## `ExpressionSymbol` and `ExpressionValueShape`

| Field | Rules |
|---|---|
| `SymbolId` | Stable opaque ID scoped to the context revision. |
| `Name` | Caller-visible identifier only; omitted if unauthorized. |
| `Kind` | `variable`, `workflow-input`, `activity-result`, `function`, `filter`, `tag`, `namespace`, `member`, or language extension value. |
| `Documentation` | Sanitized plain-text/limited-Markdown summary; no raw module markup. |
| `Signatures` | Zero or more callable signatures with parameter name, optionality, and value shape. |
| `ValueShape` | Alias/display name, scalar/object/array/map/function/reference kind, nullability, collection item/key/value shape, bounded inline members, and an explicit additional-members indicator (false for v1 providers). |
| `ChildrenLink` | Opaque continuation/query token; only valid with its context revision and caller authorization. |

## `ExpressionToolingOutcome<T>`

| State | Meaning | Payload rule |
|---|---|---|
| `success` | Request was evaluated for the specified current revision. | Required typed payload; may be empty only if `supported-empty` is semantically different. |
| `supported-empty` | Provider and capability work, but no matching symbols/diagnostics/items exist. | Required typed empty payload. |
| `unavailable` | Provider/host dependency intentionally absent or temporarily failed. | Actionable safe code/message; no partial result. |
| `unauthorized` | Caller cannot use this operation/location. | No catalog, symbol, source, or diagnostic disclosure. |
| `incompatible` | Client/provider contract versions or document/type capability do not match. | Required supported-version/capability metadata. |
| `stale` | Requested document/context revision no longer matches current Design state. | Current opaque revisions only; no stale content. |
| `canceled` | Caller cancellation reached the operation. | No partial/cacheable result. |

## `ExpressionDiagnostic`

| Field | Rules |
|---|---|
| `Code` | Stable provider/category code. |
| `Severity` | `error`, `warning`, `information`, or `hint`. |
| `Message` | Sanitized user-safe message. |
| `Range` | Zero-based source range when the request carries source; otherwise Design path. |
| `AuthoredPath` | Workflow/node/property location for full-draft validation. |
| `DocumentRevision` | Required evaluated revision. |
| `RelatedSymbols` | Optional opaque visible IDs only; must never introduce a hidden symbol name. |

## State Transitions

```text
Draft/source revision
  -> resolve Design context (authorized + policy-filtered)
  -> language provider parse/semantic analysis (cancellable; no evaluation)
  -> tooling outcome and revision-bound diagnostics
  -> full-draft validation gate
     -> draft read/save: diagnostics only
     -> test run: errors reject; unavailable requires explicit confirmation
     -> publication/promotion: errors or unavailable/incompatible/canceled reject
```

There is no transition from a tooling request into a workflow execution, runtime value read, persisted diagnostic, or expression mutation.
