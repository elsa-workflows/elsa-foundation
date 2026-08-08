# Quickstart: Consumer Contract Fragments

**Feature**: 149-consumer-contract-fragments | branch `1165-consumer-contract-fragments`

## Regenerate the committed contracts

```powershell
dotnet build Elsa.Server.slnx -c Release
dotnet run --project tools/contracts/Elsa.Contracts.Generator -- merge
```

Commit the resulting `docs/contracts/` diff in the same PR as the surface change that caused it.

## Verify freshness (what CI runs)

```powershell
dotnet run --project tools/contracts/Elsa.Contracts.Generator -- check
```

Exit 0 = committed contracts match the tree; exit 1 lists stale files with the regenerate command.

## Run the feature's tests

```powershell
dotnet test tests/Elsa/Contracts/Tests -c Release                       # equivalence + determinism + completeness guard
dotnet test tests/Elsa/Activities/Design/Reconciliation -c Release      # scanner G1 tests (adjust path to actual test csproj)
dotnet test tests/Elsa/Activities/Design/Api/Tests -c Release           # catalog view G2 tests
```

## Verify the G1/G2 repros by hand

After composing a host (or against the equivalence test output):

- `HttpEndpoint` input `ResponseMode` → `defaultValue: "Async"` (was `null`).
- `HttpEndpoint` outputs `Request`/`RouteData` → `isRequired: true`; `ParsedContent` → `isRequired: false`.
- Same values in `docs/contracts/fragments/Elsa.Activities.Http.json` and in `GET design/activities/catalog`.

## Build the validation image (delivery protocol)

```powershell
dotnet publish src/Apps/Elsa.Workbench -c Release -t:PublishContainer -p:ContainerRepository=elsaworkflows/elsa-workbench -p:ContainerImageTags=local-<shortsha> -p:ContainerUser=root --os linux --arch x64
```

## Deployment note (Model X hash impact — research R11)

G1 enriches descriptor content, so the content hash for an unchanged `(DefinitionId, Version)` changes. A **pre-existing database** reconciled against this branch throws `ActivityVersionHashMismatchException` by design. Use a fresh DB (the established e2e convention) or bump activity assembly versions. CI/docker images with per-build versions are unaffected.
