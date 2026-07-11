# Quickstart: Validate Activity Input Editor Options

## Foundation

```bash
dotnet test tests/Elsa/Activities/Design/Tests/Elsa.Activities.Design.Tests.csproj
dotnet test tests/Elsa/Activities/Http/Tests/Elsa.Activities.Http.Tests.csproj
dotnet test tests/Elsa/Modularity/Tests/Elsa.Modularity.Tests.csproj
```

Confirm the descriptor for `HttpEndpoint.SupportedMethods` contains `uiHint: "checklist"` and five ordered options with identical labels and string values.

## Studio

```bash
NODE_OPTIONS=--localstorage-file=/tmp/elsa-studio-vitest-localstorage.json pnpm --dir src/Elsa.Studio.Web/Client test
pnpm --dir src/Elsa.Studio.Web/Client build
pnpm --dir src/Elsa.Studio.Workflows/Client test
pnpm --dir src/Elsa.Studio.Workflows/Client typecheck
pnpm --dir src/Elsa.Studio.Workflows/Client build
```

Use the shared descriptor fixture to verify scalar dropdown, checklist collection, explicit collection dropdown repeater, typed values, stale values, provider loading, dependency refresh, cancellation, failure, and retry.

## Generated maps

After code and tests pass, refresh the narrow activity/domain and extension-point maps required by the changed inputs, review the generated findings, and ensure no new dependency-boundary drift appears.
