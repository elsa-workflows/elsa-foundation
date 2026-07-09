# Contract: Diagnostic Snapshot

## Purpose

A Diagnostic Snapshot is a bounded, policy-sanitized representation of a runtime value. It exists to help users troubleshoot workflow execution without storing unsafe raw payloads by default.

Diagnostic Snapshots are not replay data. They may omit, redact, summarize, truncate, or mark portions of a value.

## Capture Bounds

Snapshot generation must enforce these policy-controlled limits:

- `maxDepth`
- `maxObjectProperties`
- `maxArrayItems`
- `maxStringLength`
- `maxTotalBytes`

When a limit is reached, the snapshot must include a marker rather than continuing to expand the value.

## Node Shape

Each node is a discriminated union using `kind`.

Common optional fields:

- `typeName`: safe type name or logical value type.
- `displayName`: safe label for UI display.
- `metadata`: safe, non-sensitive display metadata.

### Scalar Nodes

```json
{
  "kind": "scalar",
  "typeName": "Boolean",
  "value": true
}
```

```json
{
  "kind": "number",
  "typeName": "Int32",
  "value": 42
}
```

```json
{
  "kind": "string",
  "typeName": "String",
  "preview": "Hello world",
  "length": 11,
  "truncated": false
}
```

### Null Node

```json
{
  "kind": "null",
  "typeName": "String"
}
```

### Object Node

```json
{
  "kind": "object",
  "typeName": "Order",
  "properties": [
    {
      "name": "id",
      "value": {
        "kind": "string",
        "preview": "A-100",
        "length": 5,
        "truncated": false
      }
    }
  ],
  "truncated": false
}
```

### Array Node

```json
{
  "kind": "array",
  "typeName": "String[]",
  "items": [
    {
      "kind": "string",
      "preview": "first",
      "length": 5,
      "truncated": false
    }
  ],
  "itemCount": 12,
  "truncated": true
}
```

### Redacted Node

```json
{
  "kind": "redacted",
  "reason": "sensitive-name",
  "displayName": "Protected value"
}
```

Valid redaction reasons include:

- `sensitive-source`
- `sensitive-type`
- `sensitive-name`
- `policy`

### Truncated Node

```json
{
  "kind": "truncated",
  "reason": "max-depth",
  "omittedCount": 3
}
```

Valid truncation reasons include:

- `max-depth`
- `max-properties`
- `max-array-items`
- `max-string-length`
- `max-total-bytes`

### Unsupported Or Error Node

```json
{
  "kind": "unsupported",
  "typeName": "Stream",
  "reason": "unsupported-type"
}
```

```json
{
  "kind": "error",
  "reason": "snapshot-failed",
  "message": "The value could not be safely inspected."
}
```

Error messages must be safe for display and must not contain payload values.

### Permission-Hidden Node

```json
{
  "kind": "permissionHidden",
  "reason": "missing-permission",
  "requiredPermission": "workflows.runtimeEvidence.viewSnapshots"
}
```

### Runtime Payload Reference Node

Runtime Payload Reference nodes are reserved for provider-backed future slices.

```json
{
  "kind": "payloadReference",
  "referenceKind": "blob",
  "referenceId": "rpr_01JZMM2Y9VN9R6DHD8T8XH4G2E",
  "displayName": "Large file",
  "contentType": "application/zip",
  "size": 48219342,
  "resolution": {
    "canResolve": false,
    "reason": "Reference resolution is not available in this release."
  }
}
```

Allowed `referenceKind` values:

- `secret`
- `blob`
- `artifact`
- `external`

Reference nodes must expose only safe display metadata. They must not expose provider connection strings, secret names that are themselves sensitive, filesystem paths, internal storage keys, or protected values.

The first Runtime Value Evidence implementation must not emit Runtime Payload Reference nodes in production capture until provider-backed reference creation, resolution endpoints, permissions, and audit records are implemented.

## Activity Inspection Integration

Activity execution inspection should include value evidence entries that reference Diagnostic Snapshot payloads when the effective evidence level is `DiagnosticSnapshot`.

```json
{
  "executionId": "activity-execution-id",
  "valueEvidence": {
    "inputs": [
      {
        "name": "Text",
        "level": "DiagnosticSnapshot",
        "snapshot": {
          "kind": "string",
          "typeName": "String",
          "preview": "Hello from runtime",
          "length": 18,
          "truncated": false
        }
      }
    ],
    "outputs": []
  }
}
```

When evidence is absent, capped to metadata, or hidden by permission, the API should return an explicit state instead of omitting the section silently.

```json
{
  "name": "Text",
  "level": "Metadata",
  "state": "notCaptured",
  "reason": "metadata-only"
}
```

## UI Rendering Requirements

- Render object and array snapshots as expandable trees.
- Collapse empty sections and summarize missing evidence.
- Show redaction and truncation markers inline where the value would appear.
- Show string previews with length and truncation state.
- Show permission-hidden states without leaking values.
- Treat unknown future node kinds as unsupported display nodes, not fatal rendering errors.
