# Copilot provider uses backend-owned identity

The first GitHub Copilot provider binding authenticates from backend-owned configuration or runtime state, not from Studio-supplied tokens or workflow-authoring requests. Studio discovers only redacted provider-neutral auth/config diagnostics, and per-user Copilot identity is deferred until Elsa has a provider-neutral credential or profile concept that can carry user delegation without exposing SDK-specific secrets.
