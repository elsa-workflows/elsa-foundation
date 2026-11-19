# Appearance data

No domain/database changes. Existing preference keys store base theme, workspace, palette, mode and navigation independently. Complete-preset selection coordinates existing keys rather than introducing another preference store.

New built-in IDs: `builtin:porcelain`, `builtin:atelier`, `builtin:graphite`. Existing `atelier` remains the original theme. New workspace value: `minimal`. Optional validated workspace-default metadata in theme definitions survives managed/directory storage, import/export, clone and preview; omitted metadata retains legacy behavior. Every new palette defines Light, Dark, Dim and High contrast.

Explicit preferences win over host fallbacks. An unavailable store does not erase unresolved custom identities. Reset clears overrides. Preview never persists. Auth, credentials and workflow state are unchanged.
