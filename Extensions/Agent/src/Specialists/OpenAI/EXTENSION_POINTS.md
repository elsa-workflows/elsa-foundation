# OpenAI specialist contributor

- `IConnectionTypeProvider` (Connections Core): the immutable singleton
  `SpecialistModelConnectionTypeProvider("openai", "specialist-openai")` registers the connection type.
- `ISpecialistModelCapabilitiesSource` and `ISpecialistModelClientFactory` (Agent Specialists Core):
  the internal scoped `OpenAISpecialistModelProvider` resolves explicit Connections profiles and
  opens an owned official SDK chat client. It reuses the [shared operational guard](../Connections/EXTENSION_POINTS.md).

The feature registers no handlers or background tasks. Credentials are read at use and never stored
in artifacts. Streaming refusal signals are translated before runtime result acceptance.
