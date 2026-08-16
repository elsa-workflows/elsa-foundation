# Quickstart: Studio Preferences API Canary

## Verify the focused migration

```bash
dotnet test tests/Elsa/Studio/Preferences/Tests/Elsa.Studio.Preferences.Tests.csproj --no-restore
dotnet test tests/Elsa/Api/Compatibility/Testing/Tests/Elsa.Api.Compatibility.Testing.Tests.csproj --no-restore
dotnet test tests/Elsa/Architecture/Elsa.Architecture.Tests.csproj --no-restore
dotnet run --project tools/maps/Elsa.Maps.Generator -- check
```

Before merge, also run the repository build and all affected solution gates.

## Inspect the canary contract

1. Confirm the module exposes only `GET` and `PUT` at `/_elsa/studio/preferences/{namespace}`.
2. Confirm both endpoints carry module ownership, Minimal API authoring metadata, and one Foundation permission disposition.
3. Run the authorization matrix for anonymous, missing, exact, implied, wildcard, and resource-denied callers.
4. Compare Minimal API HTTP/OpenAPI evidence with the committed FastEndpoints-before baseline. The result must contain no unapproved delta.
5. Confirm one unrelated FastEndpoints route still works in the same host.
6. Confirm repeated route/service release cycles produce collected weak-reference evidence.
7. Confirm the transition scanner no longer discovers Studio Preferences FastEndpoints registrations and the two #1347 exception entries are gone.

## Diagnose failures

- **HTTP delta**: inspect the named status, media type, header, body, or ProblemDetails facet; fix the mapper unless the user separately approves a contract change.
- **Binding delta**: verify `namespace` comes from the route and only schema version/value come from JSON.
- **Authorization delta**: inspect the canonical `Any(*, action)` policy and active `StudioPreferencesPermissionContributor`; do not add claim checks to the handler.
- **Duplicate route**: remove the legacy endpoint class/registration; do not rely on registration order.
- **OpenAPI delta**: compare the canonical operation projection before adding metadata convenience calls.
- **Collectibility delta**: release the reported route or service owner and rerun; do not infer unloadability from process memory.
