# Research: React Admin Module Host

- **Decision**: Use startup-discovered server manifests and same-origin static web assets.
  **Rationale**: This proves NuGet/package-style admin contributions without remote trust or hot-load complexity.
  **Alternatives considered**: Remote module origins and live Nuplane asset providers were deferred.

- **Decision**: Use Vite ESM library builds for module entries.
  **Rationale**: The browser can dynamically import module entries, and packages can ship built assets under `wwwroot`.
  **Alternatives considered**: Module Federation was deferred because it couples the first slice to a bundler runtime.

- **Decision**: Use a named Elsa event to collect module manifests.
  **Rationale**: This follows the repository's cross-feature contribution model and keeps the host lifecycle explicit.
  **Alternatives considered**: Ad hoc DI provider enumeration was rejected for this contribution surface.
