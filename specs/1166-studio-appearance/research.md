# Research

- Add local overrides to existing presets. This preserves custom themes/configured stores without a second palette catalog or server schema. Duplicate theme stores and a Cartesian product of named themes are unnecessary.
- Separate style/palette identities: current theme-ID rules include typography, shadows and navigation recipes. Selecting colors must not import these. Explicit styles neutralize base-preset presentation overrides.
- Keep resource components mounted: unsaved authoring, input focus and selected runtime scope belong to the resource, not appearance.
- Null overrides retain legacy appearance. Explicit preset application clears overrides intentionally.
- Retain requested mode across palette fallback. Protect unresolved custom scheme identities during delayed/failed loading, matching the existing preset policy.
- Reuse surface-owned layout families and adaptive panels, with rendered clearance/resize/focus verification.

Source research covered ThemeProvider, appearance storage, selectable-theme filtering, theme-specific CSS and mounted reset tests. No unresolved question remains.
