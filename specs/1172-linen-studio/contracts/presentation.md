# Presentation contracts

- Shared module styling consumes `--studio-*` semantic roles; gradients have background-only roles. Navigation has separate background/text/muted/active roles. High contrast removes decorative gradients.
- `Studio:Branding`: product name/subtitle/logo/favicon/help/privacy; `Studio:Appearance`: workspace style/palette/mode/navigation defaults.
- Identity login configuration uses its own `Identity:Login` section and optional programmatic renderer. Renderer is a single replacement callback, not a multi-registration service. It receives security context, must encode content and keep the standard form fields/action. No authentication provider API changes.
- Safe URLs are same-origin paths or HTTPS URLs (HTTP localhost allowed for development); reject executable/data/protocol-relative schemes. Deployment configuration is trusted but still encoded.
