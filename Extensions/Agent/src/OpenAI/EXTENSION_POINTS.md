# OpenAI agent contributor

- `IConnectionTypeProvider` (Connections Core): the immutable singleton
  `AgentModelConnectionTypeProvider("openai", "agent-openai")` registers the connection type.
- `IAgentModelCapabilitiesSource` and `IAgentModelClientFactory` (Agent Agents Core):
  the internal scoped `OpenAIAgentModelProvider` resolves explicit Connections profiles and
  opens an owned official SDK chat client. It reuses the [shared operational guard](../Connections/EXTENSION_POINTS.md).

The feature registers no handlers or background tasks. Credentials are read at use and never stored
in artifacts. Streaming refusal signals are translated before runtime result acceptance.
