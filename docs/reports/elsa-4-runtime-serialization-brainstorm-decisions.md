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

### 5. Authored Value JSON Shape And Naming

Authored workflow documents should contain a `values` declaration collection. Each value has stable identity, display metadata, type/schema, durability, initialization rules, and role-specific facets. References use the stable value ID, not the display name.

Conceptual shape:

```json
{
  "values": [
    {
      "id": "customer",
      "name": "Customer",
      "roles": ["input", "variable"],
      "type": {
        "kind": "json",
        "schema": { "type": "object" }
      },
      "input": {
        "required": true
      },
      "variable": {
        "scope": "workflow"
      },
      "durability": {
        "lifecycle": "instance",
        "storage": "inline"
      },
      "initial": {
        "source": "input"
      }
    }
  ]
}
```

Implications:

- The collection name should be `values`, not `variables`, `arguments`, or `state`.
- `values` should be an array rather than an object/map so Studio ordering, diffing, metadata, and future annotations remain straightforward.
- Every value should have a stable `id` used by references.
- `name` is human-facing and renameable.
- `roles` is the compact semantic declaration.
- Role-specific blocks such as `input`, `output`, and `variable` hold only metadata for that role.
- `type`, `durability`, and initialization/default behavior live on the value because they apply across roles.
- The authored document should avoid raw CLR type names. If a .NET type is needed, it should be represented through a schema or type-alias contract, not assembly-qualified durable data.
- Separate root collections such as `inputs`, `variables`, and `outputs` should not be used for the same declared values because that recreates the Elsa 3 drift problem.

### 6. Logical Type Contracts, Reference Values, And Activity Input Materialization

Elsa 4 value declarations should use JSON-first logical type descriptors. The authored document may reference built-in kinds, JSON Schema-compatible schemas, or stable type aliases. Runtime CLR types, custom serializers, validators, UI editors, and reference resolvers are resolved through registries outside the durable document.

Conceptual custom domain type:

```json
{
  "id": "customer",
  "name": "Customer",
  "roles": ["input", "variable"],
  "type": {
    "kind": "alias",
    "id": "crm.customer",
    "schema": {
      "type": "object",
      "required": ["id", "email"],
      "properties": {
        "id": { "type": "string" },
        "email": { "type": "string" },
        "name": { "type": "string" }
      }
    }
  },
  "durability": {
    "lifecycle": "instance",
    "storage": "inline"
  }
}
```

Conceptual reference value:

```json
{
  "id": "customer",
  "name": "Customer",
  "roles": ["input", "variable"],
  "type": {
    "kind": "reference",
    "id": "crm.customer",
    "schema": {
      "type": "object",
      "required": ["id"],
      "properties": {
        "id": { "type": "string" }
      }
    }
  },
  "durability": {
    "lifecycle": "instance",
    "storage": "inline"
  }
}
```

Conceptual persisted reference value:

```json
{
  "valueId": "customer",
  "type": "crm.customer",
  "kind": "reference",
  "value": {
    "id": "cust_123"
  }
}
```

Conceptual activity input binding from a declared value:

```json
{
  "activities": [
    {
      "id": "send-email",
      "type": "SendCustomerEmail",
      "inputs": {
        "customer": {
          "source": "value",
          "valueId": "customer"
        }
      }
    }
  ]
}
```

Conceptual expression binding with explicit result type:

```json
{
  "inputs": {
    "customer": {
      "source": "expression",
      "language": "javascript",
      "expression": "input.customerId",
      "resultType": {
        "kind": "reference",
        "id": "crm.customer"
      }
    }
  }
}
```

Implications:

- Authored documents store Elsa logical type contracts, not CLR type names or assembly-qualified durable data.
- Built-in kinds should cover primitives and common JSON-compatible values such as `string`, `number`, `integer`, `boolean`, `dateTime`, `duration`, `guid`, `object`, `array`, `json`, `binary`, `reference`, and `alias`.
- JSON Schema-compatible `schema` data is used where validation, public API contract, or Studio form generation matters.
- `alias` lets integrations map stable logical names to runtime CLR types, serializers, validators, UI editors, and version handling.
- `reference` stores a stable pointer to domain-owned data rather than a workflow-owned snapshot of the whole object.
- A `Customer` domain object can be passed to strongly typed activity inputs after runtime materialization, while workflow state persists only the declared JSON payload or reference.
- Activity input bindings remain expressions, literals, declared-value references, activity-output references, or other binding sources.
- Expression evaluation produces an intermediate runtime value. The activity input descriptor and optional binding `resultType` determine validation, coercion, reference resolution, and CLR materialization.
- Durable workflow state is updated only when explicitly mapped into a declared durable value. Evaluated activity inputs are ephemeral by default and are not persisted merely because an activity ran.
- Conversion should be strict at persistence and API boundaries. Declared durable values should not silently fall back to default values when conversion fails.
- Arbitrary objects are allowed only through explicit serializer contracts or external references, not broad polymorphic object serialization.

Rationale:

- This preserves Elsa 3's useful expression-based activity input model while removing the accidental persistence of evaluated arbitrary runtime objects.
- Stable aliases keep authored workflow documents portable across assembly renames, namespace changes, hosting models, and Studio/API import/export flows.
- Reference mode keeps domain aggregates owned by the host application while allowing workflows to persist the identity of the aggregate they are about.
- JSON-first schemas give Studio, APIs, validation, and migration tooling a durable contract that does not require loading application assemblies.
- Separating expression evaluation from persistence makes it clear that expressions produce values, declarations decide persistence, and activity input descriptors decide materialization.

### 7. Defaults, Initializers, Invocation Inputs, And Completion Outputs

Elsa 4 should separate authored fallback data, instance initialization, invocation input binding, and workflow output mapping.

Conceptual input plus variable:

```json
{
  "id": "customer",
  "name": "Customer",
  "roles": ["input", "variable"],
  "type": {
    "kind": "reference",
    "id": "crm.customer"
  },
  "input": {
    "required": true
  },
  "default": null,
  "initial": {
    "source": "input"
  },
  "durability": {
    "lifecycle": "instance",
    "storage": "inline"
  }
}
```

Conceptual internal variable:

```json
{
  "id": "retryCount",
  "name": "Retry Count",
  "roles": ["variable"],
  "type": {
    "kind": "integer"
  },
  "default": 0,
  "initial": {
    "source": "default"
  },
  "durability": {
    "lifecycle": "instance",
    "storage": "inline"
  }
}
```

Conceptual output from a variable:

```json
{
  "id": "result",
  "name": "Result",
  "roles": ["variable", "output"],
  "type": {
    "kind": "object"
  },
  "output": {
    "source": "value",
    "valueId": "result"
  },
  "durability": {
    "lifecycle": "instance",
    "storage": "inline"
  }
}
```

Conceptual output computed at completion:

```json
{
  "id": "summary",
  "name": "Summary",
  "roles": ["output"],
  "type": {
    "kind": "string"
  },
  "output": {
    "source": "expression",
    "language": "javascript",
    "expression": "`Processed ${values.orderCount} orders`"
  }
}
```

Rules:

- `default` is static JSON-compatible data, validated against the value type. It is not an expression.
- `initial` runs when a workflow instance starts or when a value is first created for that instance.
- Dynamic start values use `initial.source = "expression"`, not `default`.
- Invocation input can bind only to values with the `input` role.
- `input` only means boundary data. It is available during invocation or start evaluation but is not durable workflow state unless captured into a `variable`.
- `input + variable` means invocation input initializes workflow state.
- `variable` owns mutable workflow state.
- `output` does not automatically expose every variable. Outputs need an `output` facet or a clear role-driven default.
- `variable + output` can default to exposing the current variable value at completion.
- `output` only requires an explicit completion mapping from a value, expression, literal, or activity result.
- Missing required inputs fail validation before the workflow starts.
- Type conversion failures at invocation, initialization, persistence, and completion boundaries are hard failures, not silent defaults.

Role-driven defaults should be allowed to reduce boilerplate, but should be specified explicitly in the later spec. For example, `input + variable` can imply `initial.source = "input"`, and `variable + output` can imply `output.source = "value"` for the same value.

Rationale:

- Defaults are authored fallback data.
- Initializers create instance state.
- Inputs define the external invocation contract.
- Outputs define the external result contract.
- Persistence happens only for declared durable values.

### 8. Durability Policy Vocabulary And Defaults

Elsa 4 durability policy should split value lifecycle from storage strategy. Lifecycle says why the value is retained. Storage strategy says where and how the retained representation is stored.

Conceptual workflow instance value:

```json
{
  "id": "customer",
  "roles": ["input", "variable"],
  "type": {
    "kind": "reference",
    "id": "crm.customer"
  },
  "durability": {
    "lifecycle": "instance",
    "storage": "inline"
  }
}
```

Conceptual large payload:

```json
{
  "id": "document",
  "roles": ["variable"],
  "type": {
    "kind": "binary",
    "mediaType": "application/pdf"
  },
  "durability": {
    "lifecycle": "instance",
    "storage": "external",
    "storageProfile": "documents"
  }
}
```

Conceptual transient computed value:

```json
{
  "id": "temporaryScore",
  "roles": ["variable"],
  "type": {
    "kind": "number"
  },
  "durability": {
    "lifecycle": "none"
  }
}
```

Conceptual workflow result:

```json
{
  "id": "summary",
  "roles": ["output"],
  "type": {
    "kind": "string"
  },
  "output": {
    "source": "expression",
    "language": "javascript",
    "expression": "values.summary"
  },
  "durability": {
    "lifecycle": "result",
    "storage": "inline"
  }
}
```

Lifecycle vocabulary:

- `none`: not persisted; available only in the current execution or evaluation scope.
- `instance`: persisted as workflow instance state for suspension and resume.
- `result`: persisted as workflow completion or result data.
- `audit`: persisted only as observation/history and never used for resume.
- `custom`: delegated to a named durability provider.

Storage vocabulary:

- `inline`: store a JSON-compatible value inline with the owning record.
- `external`: store payload outside the workflow record and persist a locator.
- `custom`: delegate storage to a named storage provider.

Default rules:

- `input` only defaults to `lifecycle = none`.
- `variable` defaults to `lifecycle = instance`.
- `input + variable` defaults to `lifecycle = instance`.
- `output` only defaults to `lifecycle = result`.
- `variable + output` defaults to `lifecycle = instance`, plus output mapping writes result data at completion.
- Activity input evaluation is always `none` unless explicitly mapped into a declared durable value.
- Activity output is always `none` unless explicitly mapped into a declared durable value or audit/history policy.

Storage defaults:

- JSON-compatible scalar, object, array, and reference values default to `inline`.
- `binary` defaults to `external`.
- Large payload aliases are integration-defined and usually `external`.
- Custom serializers must declare whether they produce inline JSON, external locators, or custom storage records.

Important distinction:

```json
{
  "type": {
    "kind": "reference",
    "id": "crm.customer"
  },
  "durability": {
    "lifecycle": "instance",
    "storage": "inline"
  }
}
```

This means "persist the customer reference inline as workflow instance state." It does not mean "persist the full customer externally." A reference value is already a pointer. External storage is for payloads the workflow owns but should not inline, such as files, large JSON, generated reports, or serialized snapshots.

Rationale:

- Keeping lifecycle and storage separate avoids overloading "durable" with resume state, result state, references, audit records, and storage drivers.
- Reference values remain lightweight domain pointers without implying ownership of the target aggregate.
- The policy makes activity inputs and outputs ephemeral by default while preserving explicit paths for durable capture, audit, or external storage.
- The two-axis model avoids repeating Elsa 3's ambiguity where storage drivers, variables, inputs, outputs, logs, and runtime values all carry slightly different meanings of persistence.

### 9. Validation Layers And Runtime Strictness

Elsa 4 should validate authored value declarations in layers. Import and save should preserve authored documents with diagnostics when design-time references are missing, but publish, compile, and run should be strict about runtime requirements.

Document-shape validation:

- Value IDs must be unique within the authored workflow document.
- Required fields must be present.
- Role names must be known.
- JSON shape must be valid.

Role and facet validation:

- If `roles` includes `input`, the `input` facet is allowed and validated.
- If `roles` includes `output`, the `output` facet is allowed and validated.
- If `roles` includes `variable`, the `variable` facet is allowed and validated.
- Facets for absent roles should be errors unless the later spec defines them as inert annotations.

Type and schema validation:

- Built-in type kinds must be known.
- Alias and reference IDs may be unresolved at import/save time, but unresolved types become diagnostics that block publish or compile unless explicitly allowed.
- Defaults must validate against the declared type and schema.

Lifecycle and storage validation:

- `lifecycle = none` cannot specify `storage`.
- `storage = external` requires a `storageProfile` or resolvable default.
- `custom` lifecycle or storage requires a provider ID.
- `binary` cannot default to inline unless explicitly allowed by policy.
- `audit` values cannot be used for resume.

Binding validation:

- Invocation inputs can target only `input` values.
- Initializers using `source = input` require the `input` role.
- Output mappings require the `output` role.
- Activity input bindings can reference values, expressions, literals, activity outputs, and other supported binding sources, but persistence still requires explicit declared durable values.

Validation timing:

- Import/save: preserve documents even if aliases, activities, storage providers, or reference resolvers are missing; attach diagnostics.
- Validate/publish/compile: require all type aliases, reference resolvers, serializers, activities, and storage providers needed for execution.
- Run: fail fast on missing required inputs or conversion/materialization errors.

Core rule:

**Unknown or unresolved design-time references should not destroy authored documents. Unknown or unresolved runtime requirements must block publish or execution.**

## Next Decision To Work

Define Elsa 3 compatibility mapping for variables, inputs, outputs, and activity output captures:

- Compatibility mapping from Elsa 3 variables, inputs, outputs, and activity output captures.
- How Elsa 3 `VariableDefinition` maps to Elsa 4 `values`.
- How Elsa 3 `ArgumentDefinition` inputs and outputs map to roles and facets.
- How Elsa 3 storage-driver settings map to lifecycle/storage policy.
- How Elsa 3 runtime values, logs, and activity outputs are handled when definitions migrate.
