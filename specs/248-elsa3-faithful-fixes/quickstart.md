# Quickstart: verify spec 248

```bash
dotnet test Extensions/Elsa3/tests/Mapping/Tests/Elsa3.Mapping.Tests.csproj --filter "FullyQualifiedName~Elsa3FaithfulFixesTests|FullyQualifiedName~Elsa3ActivityMapCoverageTests"
```

Then the whole Mapping test project:

```bash
dotnet test Extensions/Elsa3/tests/Mapping/Tests/Elsa3.Mapping.Tests.csproj
```

**JTest check (throwaway, never committed).** Convert each definition in a local JTest checkout with
`ConversionServices.ConvertAsync` against `RealActivityCatalog`, reading the files with `JsonSerializerDefaults.Web`.
Expected to convert end to end: simple-write-hello-world, performance-baseline-dispatch-sub, performance-bigdata,
performance-commit-after-every-activity, composition-bulk-dispatch-sub-workflow, storage-drivers-variables-sub-workflow.
Delete the probe afterwards.
