# OpenAI agents

Enable `OpenAIAgents` to add the `openai` agent model owner and
`agent-openai` connection type. This uses a direct OpenAI project API key, independently of
Azure OpenAI and Weaver. See the [shared connection guide](../Connections/README.md) for
secret ownership, capability declarations, model settings, schema limits and request controls.

For a GPT-4.1 mini example, use endpoint `https://api.openai.com`, allowed host `api.openai.com`,
model `gpt-4.1-mini`, output ceiling `32768`, structured results `true`, temperature supported `true`,
authentication `api-key` and secret field `apiKey`. The factory appends `/v1` internally. The official
OpenAI SDK sends Bearer authentication to Chat Completions with SDK retries disabled. Capability
declarations are explicit configuration; the connection test does not make an inference request.

The optional agent streaming profile uses the same complete-result/refusal acceptance boundary
as the other provider adapters. Model access, billing and real inference need separate live verification.

[OpenAI model documentation](https://developers.openai.com/api/docs/models/gpt-4.1-mini).

Follow the [Studio example](../../../../specs/1179-agents/openai-provider/quickstart.md) to create, test and publish a typed copywriter.

## Cross-domain contributions

- `IConnectionTypeProvider` (Connections Core): registers the stateless `agent-openai`
  connection-type contribution without capturing a scoped model factory or resolver.
- `IAgentModelCapabilitiesSource` and `IAgentModelClientFactory` are intra-domain Agent
  contributions for the exact `openai` key, registered scoped; their canonical ports are in
  [Agent Core](../Core/EXTENSION_POINTS.md).
