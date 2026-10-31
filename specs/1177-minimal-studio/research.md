# Research: Minimal Studio

- Decision: three new palettes plus one minimal workspace treatment. Preserve old `atelier` identity. Geometry cannot follow palette selection implicitly.
- Decision: a separate complete-preset action applies canonical mode; existing theme/palette actions retain one-axis behavior and persisted user choices.
- Decision: optional workspace metadata belongs in both typed server definition and frontend clone/normalization. Unknown JSON fields would otherwise be discarded. Use an init property to retain the old record constructor. Theme defaults supply their own editable layout/type/shape; explicit workspace overrides replace them. Legacy Linen inference remains.
- Decision: common rules ship in public studio-ui CSS. Owner-module rules consume shared roles; no host selectors into sibling private namespaces.
- Decision: retain approved mockups and normalize minor generated inconsistencies in a visual contract. The empty-result inset is a component study. Runtime/incident semantics and actual template provenance remain truthful.
- Alternatives rejected: replace the original Atelier ID; geometry keyed on palette; font/radius inference; a new layout kind; duplicated screen implementations; host-only styling.

Independent read-only planning research inspected the theme/provider/store paths and public UI/package/module surfaces. Canonical integration contracts remain in [module UI](../../Studio/docs/module-ui.md) and [design tokens](../../Studio/docs/design-tokens.md).
