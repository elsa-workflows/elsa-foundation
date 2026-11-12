# Research

Two independent read-only research agents inspected the theme/workflow and branding/login seams.

- Reuse workspaceStyles and module-owned workspaceAppearance CSS. Linen gets Manrope and rounded shared radii; preserve existing arrangements. Alternative of global graph selectors rejected because modules own their rendering.
- Keep the eight palettes and add Linen. Navigation in light mode uses the palette's dark-mode sidebar pair, exposed through shared navigation roles. A hardcoded dark fill alone would break contrast.
- Distinguish missing style (configured Linen default) from explicit preset presentation using a storage sentinel. Retain asynchronous custom-theme guards and state identity.
- Studio credentials are backend-owned: styling only the SPA transition would miss the actual login. Keep authentication POST/security unchanged and add a single callback presentation seam alongside options in the owning provider package. No Identity-to-Studio dependency.
- Bundle the approved images as local assets; use fixed allow-listed asset names. Configure separate Studio/identity hosts explicitly; do not pass branding through untrusted return URLs.
