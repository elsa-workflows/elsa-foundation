# Anthropic agents

Enable `AnthropicAgents` to add the `anthropic` agent model owner and the
`agent-anthropic` connection type. See the [shared connection guide](../Connections/README.md)
for explicit endpoint/model declarations, supported settings/schema, API-key ownership and request controls.
This package uses the official Anthropic SDK independently of Weaver's selected assistant harness.

## Cross-domain contributions

- `IConnectionTypeProvider` (Connections Core): registers the stateless agent connection-type
  contribution for `agent-anthropic` in the existing Connections registry. It does not capture a
  scoped model factory or connection resolver.
- `IAgentModelCapabilitiesSource` and `IAgentModelClientFactory` are intra-domain Agent
  contributions for the exact `anthropic` key, registered scoped; their canonical ports are in
  [Agent Core](../Core/EXTENSION_POINTS.md).

The optional `AgentRuntime.StreamResponses` host setting uses the qualified `stream1` profile
through MAF. Completion/refusal checks precede accepted result/history; partial output and interrupted
streams remain unaccepted. See the [runtime boundary](../Elsa.Agent/EXTENSION_POINTS.md#optional-streaming-responses)
and [pinned signal experiment](../../../../tools/agents/research/streaming/README.md).
