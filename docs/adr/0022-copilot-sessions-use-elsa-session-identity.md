# Copilot sessions use Elsa session identity

The GitHub Copilot provider binding uses the Elsa `AgentSession.Id` as the Copilot SDK session identifier rather than persisting a separate provider session identity. This keeps Elsa's durable session state provider-neutral while still allowing the Copilot SDK runtime to resume its own conversation state from a configured backend-owned storage location.
