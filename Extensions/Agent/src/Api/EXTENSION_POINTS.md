# Agent authoring API extension points

Owner project: `Elsa.Agent.Api`
Domain: `Elsa.Agent`

Two MinimalEndpoints endpoints for the Studio agent editor, with capability `elsa.api.agent` v1.
Both need `activities/design:author` or `activities/design:manage`; reading activity designs alone does not grant them.

| Route | Answer |
|---|---|
| `GET agent/model-providers` | Each registered model provider that can back an agent: its provider key, its operational connection type and display name |
| `POST agent/model-capabilities` | The current settings schema, structured-result support and output-token ceiling of one `{providerKey, connectionName, modelId}` reference |

A provider is offered only when exactly one `AgentModelConnectionTypeProvider` and exactly one
`IAgentModelCapabilitiesSource` name its key. Capabilities come from that source in the caller's scope, the same
read publication's preflight makes, without opening an inference client. The capability fingerprint is not returned.
The request body is closed, so an unmapped member is refused with 400. A missing, foreign, mismatched or otherwise
unusable connection is a plain 422 that does not say which check failed. The editor lists connections through the
existing Connections picker, which returns no secret values.

## Cross-domain contributions

The feature declares its API capability with `AddApiCapability` and keys its fault renderer by the endpoint group
owner. It contributes no permission: it reuses the Activities Design authoring permissions.
