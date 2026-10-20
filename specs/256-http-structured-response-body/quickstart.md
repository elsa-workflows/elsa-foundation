# Quickstart: verify spec 256

Run from the worktree root. Build and test only these projects (shared machine; a build-slot wrapper may queue them).

```bash
dotnet test Essentials/tests/Activities/Http/Tests/Elsa.Activities.Http.Tests.csproj
dotnet test Essentials/tests/Activities/Http/IntegrationTests/Elsa.Activities.Http.IntegrationTests.csproj
dotnet test Essentials/tests/Activities/Design/Tests/Elsa.Activities.Design.Tests.csproj --filter "FullyQualifiedName~ActivityContractSurface"
dotnet test Extensions/Elsa3/tests/Mapping/Tests/Elsa3.Mapping.Tests.csproj
```

Expected:

1. `WriteHttpResponseStructuredBodyTests`: an object body with no content type is sent as compact JSON with
   `application/json`; a string body with no content type is sent as text with `text/plain`; 204 sends no body.
2. The existing string-body tests pass unchanged in intent.
3. The contract surface snapshot shows `Body` as `System.Text.Json.JsonElement?`.
4. `Elsa3HttpResponseBodyTests`: object content converts with unset, blank, empty-script or JSON content types, and is
   refused with `ELS3-ACTIVITY-VALUE-OUTSIDE-CONTRACT` for a non-JSON literal, a computed content type or binary content.

Throwaway JTest check (never committed): convert the 36 definitions in `JTEST-nexxbiz/workflow-definitions` with
`ConversionServices.ConvertAsync` against `RealActivityCatalog`. `javascript-array-methods` and `javascript-object-methods`
convert end to end, and the only `WriteHttpResponse.content` refusal left is the computed content type in `http-endpoints`.
