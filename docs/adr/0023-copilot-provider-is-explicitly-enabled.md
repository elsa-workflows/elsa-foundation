# Copilot provider is explicitly enabled

The GitHub Copilot provider feature may be registered by a host or shell, but the provider only reports available diagnostics when Copilot support is explicitly enabled and authentication/runtime configuration is usable. This lets Elsa hosts compose the provider package without requiring local Copilot credentials, while Studio continues to discover provider readiness through provider-neutral diagnostics instead of provider-specific startup failures.
