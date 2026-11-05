# Bundled login artwork

The ten approved login directions ship as optimized portrait JPEGs: `river-to-sea`,
`open-architecture`, `flowing-ribbons`, `paper-landscape`, `creative-studio`, `botanical-growth`,
`constellation`, `miniature-world`, `glass-and-light` and `unfolding-map`.

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

## Parcel Room

`parcel-room.jpg` is the default artwork. The owner selected the M2 felt-tip concept on
4 November 2026: a warm illustrated sorting bench with branching paths for envelopes and parcels.
An image-only derivative was produced with OpenAI's built-in imagegen tool, preserving the selected
scene and removing the mock form, text and logo. The original generated PNG and extraction prompt
remain in the session's design artifacts. This locally embedded 1254×1254 JPEG uses quality 82.

The coordinated `parcel-room` layout and palette use white surfaces, a small standard Elsa logo,
a restrained teal action and an uncropped illustration. These are the new options defaults;
existing artwork, presentation choices and custom image/renderer overrides remain available.
