# Elsa.Samples.Nuplane.Activities

Sample package-loaded shell feature for `Elsa.Workbench`.

## Build And Drop

```bash
dotnet pack samples/Elsa.Samples.Nuplane.Activities/Elsa.Samples.Nuplane.Activities.csproj \
  -c Release \
  -o src/Apps/Elsa.Workbench/packages
```

Enable the feature on a shell by adding it to the shell's `Features` section. The stock Workbench `shells.json` does
not list it, because the `packages/` feed is empty in a checkout and a listed feature that no package supplies only
produces a startup warning:

```json
"SampleNuplaneActivities": {}
```

Then reload the shell. The Workbench serves `https://localhost:7243` (and `http://localhost:5095`), and the
shell-management endpoints require the management key configured at `Elsa:ModuleManagement:ApiKey`:

```bash
curl -k -X POST https://localhost:7243/_admin/shells/reload/default \
  -H "X-Elsa-Module-Management-Key: <management-key>"
```

Or reload every active shell:

```bash
curl -k -X POST https://localhost:7243/_admin/shells/reload-all \
  -H "X-Elsa-Module-Management-Key: <management-key>"
```

The package contributes:

- `SampleNuplaneActivities`, a CShells feature.
- `SayHelloFromNuplane`, a custom Elsa activity.
- A design reconciliation source and runtime constructor so the activity works from a Nuplane-loaded package.
