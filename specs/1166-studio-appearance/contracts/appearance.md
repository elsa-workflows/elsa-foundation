# Presentation contract

Existing --studio-* tokens, theme-pack JSON and SDK data-theme-layout remain valid. Added semantic edge/surface tokens provide standalone defaults.

The host publishes data-theme for the base preset, data-color-scheme for active palette and data-workspace-style only for an explicit style. Modules consume semantic tokens and surface-owned layout/style attributes; palette names do not change behavior.

Quiet/Focus/Open select supported compositions. Color selection changes colors and supported modes. Style selection preserves scheme, requested mode and explicit navigation. Existing contributed tools and commands remain available.
