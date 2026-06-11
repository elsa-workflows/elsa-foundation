# Elsa.Samples.Nuplane.Activities

Sample package-loaded shell feature for `Elsa.Server`.

## Build And Drop

```bash
dotnet pack samples/Elsa.Samples.Nuplane.Activities/Elsa.Samples.Nuplane.Activities.csproj \
  -c Release \
  -o src/Apps/Elsa.Server/packages
```

Enable the feature on a shell:

```json
"SampleNuplaneActivities": {}
```

Then reload the shell:

```bash
curl -k -X POST https://localhost:5001/_admin/shells/reload/default
```

Or reload every active shell:

```bash
curl -k -X POST https://localhost:5001/_admin/shells/reload-all
```

The package contributes:

- `SampleNuplaneActivities`, a CShells feature.
- `SayHelloFromNuplane`, a custom Elsa activity.
- A design reconciliation source and runtime constructor so the activity works from a Nuplane-loaded package.
