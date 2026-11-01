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
