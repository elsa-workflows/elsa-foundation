# Contract: Pinned Input Source

Studio joins two additive, independently stored Foundation projections by `(executableNodeId, inputKey)`:

1. `WorkflowExecutableSourceReference.AuthoredInputs`: non-behavioral, per-publish authoring provenance selected through the Run's pinned `sourceReferenceId`.
2. `WorkflowExecutableInputBindingView`: behavioral compiled binding from the content-addressed executable.

## Authored Inputs Sidecar

```json
{
  "executableNodeId": "node-42",
  "inputKey": "input-reference-key",
  "inputName": "Recipient",
  "declaredType": "String",
  "expressionType": "JavaScript",
  "value": "variables.recipient.email",
  "isSensitive": false
}
```

- `inputKey` is `InputDefinition.ReferenceKey` within the executable-node scope.
- `value` is a verbatim `JsonElement`, never a CLR `object` graph and never canonicalized.
- The sidecar is copied at publish/Test Run time, is immutable with the Source Reference, and never contributes to `ArtifactHash`.
- Missing sidecars are valid for legacy, promoted, or source-unavailable references.

## Compiled binding projection

```json
{
  "executableNodeId": "node-42",
  "inputKey": "input-reference-key",
  "inputName": "Recipient",
  "declaredType": "String",
  "binding": {
    "kind": "Expression",
    "expression": {
      "language": "JavaScript",
      "source": "variables.recipient.email",
      "resultType": { "alias": "String" },
      "metadata": {}
    }
  },
  "isSensitive": false
}
```

Supported binding discriminators are the existing `Literal`, `Expression`, `ActivityOutput`, `DurableValue`, and `Reference` variants. Studio must tolerate a future API discriminator with a generic fallback, but Foundation does not promise persistence of an unknown compiled CLR binding type.

## Authorization

The authored-sidecar and detailed compiled-source projection require `workflow-publishing.read`. Runtime evidence separately requires `workflow-runtime.read`.

When source access is denied, the response may retain only `executableNodeId`, `inputKey`, `inputName`, declared type, sensitivity boolean, and `accessState: "permissionHidden"`. It must omit authored value, expression/literal text, activity-output/reference/durable IDs, compiled source, metadata, and legacy `source`/`summary`.

## Compatibility and invariants

- Existing executable `source`/`summary` fields may remain temporarily but new Studio code never parses them.
- A missing sidecar renders `compiledOnly` or `sourceUnavailable`; the server never consults a mutable draft.
- Different authored forms that compile identically may point to the same executable through different Source References without changing artifact identity.
- `isSensitive` is true if authoring, binding metadata, or runtime policy marks the input sensitive.
