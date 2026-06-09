# Elsa 4 Runtime Serialization Brainstorm Decisions

Status: brainstorm decisions locked for the Runtime Execution Seam discussion. This is not yet a ratified Speckit spec, implementation plan, glossary entry, or constitution gate.

Program goal state: [Runtime Execution Seam](../program-goals/runtime-execution-seam.md).

Source evidence: [Elsa Core runtime serialization and value persistence analysis](elsa-core-runtime-serialization-value-persistence-analysis.md).

Parent queue: [Elsa Core runtime broken windows brainstorm](elsa-core-runtime-broken-windows-brainstorm.md).

## Purpose

Capture the Elsa 4 direction selected during the topic 1 interactive review so the brainstorm can continue without losing decisions or mixing them back into the Elsa 3 source-evidence report.

## Locked Decisions

### 1. Elsa 3 Compatibility Scope

Elsa 4 should support importing or migrating Elsa 3 workflow definitions, but should not promise transparent resume of arbitrary Elsa 3 workflow instances.

Implications:

- Elsa 3 workflow definition JSON, exported workflow-definition JSON, and stored definition data are migration inputs.
- Elsa 3 `WorkflowState`, active activity execution contexts, scheduled activities, bookmarks, runtime variables, and runtime output registers are not part of the default compatibility contract.
- Existing Elsa 3 instances should be drained, completed, cancelled, or handled by a separate explicit migration tool if a customer truly needs it.
- Execution logs can be preserved as audit/history if needed, but should not be treated as executable state.
- Newtonsoft/STJ island compatibility can be limited to definition/import compatibility unless source evidence shows authored definitions commonly embed those values.

### 2. Runtime Value Persistence Policy

Elsa 4 should persist only declared durable runtime state by default.

Implications:

- Runtime values are not durable merely because they existed during execution.
- Workflow variables are persisted only when declared with a durable policy.
- Workflow inputs are persisted only if declared as part of the durable workflow contract or explicitly captured.
- Workflow outputs are persisted as declared workflow result/contract values with clear serialization rules.
- Activity outputs are ephemeral by default and become durable only when explicitly captured, mapped to workflow output, or opted into a storage policy.
- Arbitrary CLR objects are not automatically persisted inline. They need a serializer contract, a JSON-compatible representation, or an external reference.

### 3. Authored Definition Boundary

Elsa 4 should have one canonical authored workflow document. REST and Studio should use that document directly for authoring, import, and export, but API endpoints may wrap it with server-owned metadata and operation-specific fields.

Implications:

- The authored workflow document is the canonical persisted/import/export document.
- Studio edits the authored document shape directly instead of a separate workflow-body DTO.
- Saving or importing authored workflow JSON should not require activity descriptor resolution or runtime activity construction.
- Missing activity types should be stored as document data and reported as diagnostics during validation, compile, or publish.
- Workflow-as-activity references may be unresolved during import and resolved when compiling/publishing.
- API responses may wrap the document with server-owned metadata such as version ID, logical definition ID, publication state, timestamps, links, permissions, validation status, diagnostics, concurrency tokens, read-only flags, system flags, and consuming workflow counts.
- API request wrappers may carry operation fields such as `publish`, but should not introduce a second workflow definition body shape.

Conceptual separation:

```text
AuthoredWorkflowDocument
  Canonical Design-owned durable authored state.

WorkflowApiEnvelope
  Transport wrapper around the authored document plus server-owned metadata.

WorkflowExecutable
  Runtime-owned derived executable artifact produced at compile/publish time.
```

Boundary rule:

**The authored document is Design-owned durable state. The executable is Runtime-owned derived state.**

### 4. Unified Value Declaration Model

Elsa 4 should use one canonical value declaration collection in the authored workflow document. Variables, inputs, and outputs are roles or facets of a declared value, not three unrelated root-level models.

Conceptually, each declared value owns:

- Stable identity.
- Type and schema information.
- Default, initial, or binding rules.
- Durability policy.
- One or more semantic roles.

Roles preserve the distinct meaning of each value:

- `input`: an externally supplied workflow invocation contract value.
- `output`: an externally observable workflow result contract value.
- `variable`: an internally addressable workflow state value.
- durability policy: whether and how the value is persisted.
- type/schema: the allowed value shape and serializer contract.

Combined roles are allowed when the author means them:

- `input + variable`: an external input becomes internally addressable workflow state.
- `variable + output`: internal workflow state is exposed as a workflow output.
- `input` only: an invocation boundary value that is not automatically durable or internally mutable.
- `output` only: a workflow result computed or mapped at completion.

Implications:

- The document should not require separate `variables`, `inputs`, and `outputs` collections that can drift from each other.
- Public workflow contract metadata and internal state metadata should live on role-specific facets, not in separate duplicate models.
- Runtime persistence decisions should be driven by explicit durability policy on the declared value.
- Field names such as `values` and role names are conceptual for the brainstorm and should be finalized in the later spec.

## Next Decision To Work

Define the detailed semantics of declared values:

- Exact authored document JSON shape and naming.
- Type/schema representation and serializer contracts.
- Default value, initial value, invocation binding, and completion mapping semantics.
- Durability policy vocabulary and defaults.
- Validation rules for incompatible role combinations.
- Compatibility mapping from Elsa 3 variables, inputs, outputs, and activity output captures.
