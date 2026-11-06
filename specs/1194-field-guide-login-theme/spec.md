# Field Guide login theme

**Status**: Implemented — delivery and exact-head gate receipts on PR #3682
**Issue**: #3680 · **Goal**: none/free-flow · **Owner request**: 2 November 2026

Provide a new opt-in backend-owned login appearance matching the Elsa 4 brochure. A user signs in through a clear form on warm paper, with sage ink, serif headings, the drawn Elsa logo, and a full pencil/watercolor capability plate.

## Acceptance

- FR1: `Identity:Login:Layout`, `Palette`, and `Artwork` each accept `field-guide` independently. Existing defaults, choices, custom image URLs and the host renderer remain effective.
- FR2: The Field Guide layout uses the bundled drawn logo by default. `LogoUrl` overrides it. Both new assets are anonymous, locally embedded and correctly prefixed for shell routes and proxy PathBase.
- FR3: The coordinated palette remains light under an OS dark preference. Other palettes retain their existing behaviour. The form remains usable with high contrast, missing art, keyboard input, and no JavaScript.
- FR4: The layout preserves the complete illustration at desktop, tablet and 320px. Mobile places the form first. Configured copy is encoded; configured safe URLs and focus semantics retain existing policy.
- FR5: Standard credential validation, antiforgery, trusted return URLs and error feedback remain endpoint-owned. A real HTTP sign-in still works.
- FR6: Canonical branding documentation explains activation and independent overrides; no Studio shell theme or default is changed.

Verification: whole Identity suite with discovery/execution reconciliation; new configuration/prefix/asset and form tests; a red-before/green-after bite proof; actual source-rendered browser checks and existing repository architecture/maps gates. The initial branch stacks on #3679 for its artwork registry and does not own that change.
