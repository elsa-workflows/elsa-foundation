# Anthropic specialists

Enable `AnthropicSpecialists` to add the `anthropic` specialist model owner and the
`specialist-anthropic` connection type. See the [shared connection guide](../Connections/README.md)
for explicit endpoint/model declarations, supported settings/schema, API-key ownership and request controls.
This package uses the official Anthropic SDK independently of Weaver's selected assistant harness.

## Cross-domain contributions

- `IConnectionTypeProvider` (Connections Core): registers the stateless specialist connection-type
  contribution for `specialist-anthropic` in the existing Connections registry. It does not capture a
  scoped model factory or connection resolver.
- `ISpecialistModelCapabilitiesSource` and `ISpecialistModelClientFactory` are intra-domain Agent
  contributions for the exact `anthropic` key, registered scoped; their canonical ports are in
  [Specialists Core](../Core/EXTENSION_POINTS.md).
