# Implementation plan

1. Add `SecretScopeDescriptor`, `ISecretScopeProvider` and `ISecretScopeRegistry` to Secrets Core. Compose a singleton registry of static provider metadata in `AddSecrets`; validate names and expose a stable read-only catalogue. This lifetime is intentional: suggestions have no tenant, user, repository or request context.
2. Add `scopes` to `/secrets/descriptors`, defaulting to an empty collection. Older hosts remain supported by an optional frontend field. Follow the existing metadata permission gate.
3. Add a controlled Studio scope picker and integrate it into Secret creation. Keep custom input progressively disclosed and all matching semantics outside the picker. Preserve form state across metadata retries.
4. Verify provider composition and HTTP discovery through creation/resolution, plus real-browser picker interaction and layout. Run focused regression suites, type checking, lint and production asset builds. Review the integrated diff before opening a PR.

The catalogue is advisory. Existing scope strings are not migrated or silently rewritten; runtime matching remains the existing case-insensitive policy. Provider conflicts fail catalogue construction with deterministic diagnostic identities instead of affecting runtime resolution.
