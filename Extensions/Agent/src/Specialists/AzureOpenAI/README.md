# Azure OpenAI specialists

Enable `AzureOpenAISpecialists` to add the `azure-openai` specialist model owner and the
`specialist-azure-openai` connection type. See the [shared connection guide](../Connections/README.md)
for explicit endpoint/deployment declarations, supported settings/schema, API-key ownership and request controls.
This package uses the official Azure OpenAI SDK independently of Weaver's selected assistant harness.

## Cross-domain contributions

- `IConnectionTypeProvider` (Connections Core): registers the stateless specialist connection-type
  contribution for `specialist-azure-openai` in the existing Connections registry. It does not capture
  a scoped model factory or connection resolver.
- `ISpecialistModelCapabilitiesSource` and `ISpecialistModelClientFactory` are intra-domain Agent
  contributions for the exact `azure-openai` key, registered scoped; their canonical ports are in
  [Specialists Core](../Core/EXTENSION_POINTS.md).

The optional `SpecialistsRuntime.StreamResponses` host setting uses the qualified `stream1` profile
through MAF. Completion/refusal checks precede accepted result/history; partial output and interrupted
streams remain unaccepted. See the [runtime boundary](../Elsa.Agent.Specialists/EXTENSION_POINTS.md#optional-streaming-responses)
and [pinned signal experiment](../../../../../tools/agents/research/streaming/README.md).
