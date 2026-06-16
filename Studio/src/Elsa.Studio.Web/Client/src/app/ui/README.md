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

Visual rules:

- Shell/page titles stay compact; no hero-scale admin headings.
- Resource lists are row-first. Avoid stacked large cards for scan-heavy data.
- Resource workbenches use the available content width; avoid fixed desktop
  max-width caps that leave unused space beside lists or inspectors.
- Use true white or cool neutral surfaces, hairline borders, minimal shadow, and
  5-8px radii for admin chrome.
- Selection uses the shared blue/cyan accent; statuses use the shared semantic
  green, amber, and red tokens.
- Summary metrics are compact strips, not oversized dashboard cards.
