# Appearance model

- Base preset: existing nullable themeId, with null meaning configured default.
- Workspace override: quiet, focus, open or null; null inherits the preset.
- Scheme override: selectable theme ID or null; null inherits preset colors. Unknown IDs resolve through existing store fallback after successful loading.
- Mode: existing System/Light/Dark/Dim/High contrast preference; requested mode survives unsupported-palette fallback.
- Navigation: existing theme/left/top; explicit placement wins.
- Preview: transient full preset, independent of persisted overrides.

Normal transitions change one axis. Preset application clears both overrides. Reset ends preview and clears all ten appearance/chrome keys. Resource identity, workflow JSON and backend preferences do not participate.
