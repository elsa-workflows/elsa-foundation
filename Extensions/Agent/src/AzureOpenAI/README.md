# Azure OpenAI agents

Enable `AzureOpenAIAgents` to add the `azure-openai` agent model owner and the
`agent-azure-openai` connection type. See the [shared connection guide](../Connections/README.md)
for explicit endpoint/deployment declarations, supported settings/schema, API-key ownership and request controls.
This package uses the official Azure OpenAI SDK independently of Weaver's selected assistant harness.

## Cross-domain contributions

- `IConnectionTypeProvider` (Connections Core): registers the stateless agent connection-type
  contribution for `agent-azure-openai` in the existing Connections registry. It does not capture
  a scoped model factory or connection resolver.
- `IAgentModelCapabilitiesSource` and `IAgentModelClientFactory` are intra-domain Agent
  contributions for the exact `azure-openai` key, registered scoped; their canonical ports are in
  [Agent Core](../Core/EXTENSION_POINTS.md).

The optional `AgentRuntime.StreamResponses` host setting uses the qualified `stream1` profile
through MAF. Completion/refusal checks precede accepted result/history; partial output and interrupted
streams remain unaccepted. See the [runtime boundary](../Elsa.Agent/EXTENSION_POINTS.md#optional-streaming-responses)
and [pinned signal experiment](../../../../tools/agents/research/streaming/README.md).
