# Northstar branded host

This example publishes and runs the real standard Studio host with application-owned branding assets
and Linen defaults. It uses the existing modular host and theme store, rather than copying the SPA.
The separate credential host uses the same product name and the bundled local artwork.

From the repository root (with the documented .NET/Node/pnpm prerequisites):

```sh
pnpm --dir Studio install --frozen-lockfile
pnpm --dir Studio build
Studio/examples/branded-host/run-studio.sh
```

Open `http://127.0.0.1:7086`. `LINEN_STUDIO_URL`, `LINEN_BACKEND_URL` and `LINEN_HOST_OUTPUT` optionally
select the listen URL, existing backend URL and publish directory. The script publishes into a temporary
application folder and copies logo.svg into that application's static files. It leaves your source and
other running hosts alone. Remove the temporary folder after stopping the sample.

The backend must separately enable the desired authentication provider and trust the Studio origin for
credentialed CORS/return URLs, following the existing hosting/auth guide. Copy the `Identity` section
from [identity.json](identity.json) into that backend's application configuration. For the in-repo
Workbench, these equivalent command-line settings demonstrate the bundled credential presentation:

```sh
ASPNETCORE_ENVIRONMENT=Development dotnet run --no-launch-profile \
  --project Essentials/apps/Elsa.Workbench -- \
  --urls http://127.0.0.1:7286 \
  --Identity:Login:ProductName='Northstar Studio' \
  --Identity:Login:Tagline='Make room for possibility.' \
  --Identity:Login:Layout=split --Identity:Login:Artwork=ember
```

Visit `http://127.0.0.1:7286/_elsa/identity/login`. This does not configure accounts, identity-provider
selection or trust on your behalf; keep your deployment's existing auth configuration. To use a custom
backend logo, copy logo.svg into its `wwwroot/branding` and configure `Identity:Login:LogoUrl` accordingly.
Serve your own Help/Privacy pages before adding their URLs. The sample defaults do not depend on remote
images or fonts.

Switch Layout to `reverse`, `immersive` or `editorial`, Palette to `tidal`, `daybreak` or `canopy`, and
Artwork independently. Follow [Brand Studio and sign-in](../../docs/branding.md) for custom photos,
responsive cropping, palettes, shared module roles, accessibility and the renderer replacement example.

All settings are host defaults: an explicit browser preference wins. Use Settings → Appearance → Reset
to inspect a fresh host-default experience.
