# Bundled login artwork

The ten approved login directions ship as optimized portrait JPEGs: `river-to-sea`,
`open-architecture`, `flowing-ribbons`, `paper-landscape`, `creative-studio`, `botanical-growth`,
`constellation`, `miniature-world`, `glass-and-light` and `unfolding-map` (default).

They were created with OpenAI's built-in image generation from the approved login mockups on
2 November 2026. The production edit retains each left-panel artwork and removes the UI, text,
readability gradient, frame and rounded corners; the application renders those elements separately.
The original generated PNGs and edit prompt remain in the task's design artifacts, outside the build.
JPEG encoding uses quality 80 at the original 1024×1536 resolution.

The earlier Ember (warm coast/path), Tidal (cool coast), Daybreak (soft sunrise) and Canopy (green
woodland) artwork remains available for explicitly configured hosts. All assets are embedded locally
and require no third-party photo/CDN account.

Integrators select artwork through `Identity:Login:Artwork` or `LoginPageOptions.Artwork`, or replace
it with `ImageUrl`. See the canonical [branding guide](../../../../../../../Studio/docs/branding.md#configure-the-actual-credential-page)
for the artwork catalog and JSON, environment variable and C# configuration examples.
