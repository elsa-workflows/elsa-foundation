# Elsa Studio UI

The shared UI layer defines the Modular Admin Workbench language for Studio
screens. Host pages can consume these React primitives directly. Independently
bundled modules should depend on the documented token aliases and stable SDK
contracts until the component export boundary is finalized.

Core patterns:

- Resource pages use compact toolbars, rows, grids, status chips, and inspectors.
- Configuration pages use split inspector or grouped settings layouts.
- Diagnostics pages use dense rows, severity chips, timelines, and bottom panels.
- Typography and colors come from `tokens.css`; module CSS should use the
  `--studio-*` variables or compatibility aliases rather than one-off values.
