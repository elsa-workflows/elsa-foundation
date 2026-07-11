# Dynamic Activity Input Options API

## Operation

`POST /_elsa/workflow-management/descriptors/activities/{activityVersionId}/inputs/{inputName}/options`

Request:

```json
{
  "nodeId": "http-endpoint-1",
  "workflowState": {
    "variables": [],
    "rootActivity": {},
    "inputs": [],
    "outputs": []
  }
}
```

Success (`200`, `Cache-Control: no-store`):

```json
{
  "options": [
    { "label": "Customer name", "value": "customerName" },
    { "label": "Customer number", "value": "customerNumber" }
  ]
}
```

## Resolution and validation

1. Load the routed activity version and input definition.
2. Read the provider key from that input's cataloged `UISpecifications`; never accept a key in the request.
3. Find `nodeId` in the submitted current workflow state and require its activity version to equal `activityVersionId`.
4. Resolve exactly one registered provider by ordinal key and invoke it with the typed workflow state, node, and input.

## Errors

- `400`: malformed request or missing node.
- `404`: activity version or input not found.
- `409`: node/activity-version mismatch or input has no dynamic provider metadata.
- `503`: provider missing or provider execution failed; body uses code `OPTIONS_PROVIDER_UNAVAILABLE` and contains no exception details.
- Request cancellation propagates without conversion to `503`.
