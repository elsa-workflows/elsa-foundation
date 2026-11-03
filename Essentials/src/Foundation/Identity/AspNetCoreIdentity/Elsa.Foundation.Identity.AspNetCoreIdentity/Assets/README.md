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

## Field Guide

`field-guide.jpg` and `field-guide-logo.png` are the original approved pencil/watercolor
capability plate and hand-drawn Elsa logo from the Elsa 4 brochure. The same files are used
by the Elsa Foundation Atlas website. They ship locally with the identity implementation;
no website/CDN access is needed at login. Choose the `field-guide` layout, palette and
artwork for the coordinated presentation. The layout contains the full plate at all widths.
The illustration is decorative and the logo retains an empty alt beside the product name.
