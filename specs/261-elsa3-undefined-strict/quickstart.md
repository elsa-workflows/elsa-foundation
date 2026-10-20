# Quickstart: verify spec 261

```bash
dotnet test Extensions/Elsa3/tests/Mapping/Tests/Elsa3.Mapping.Tests.csproj --filter "FullyQualifiedName~Elsa3UndefinedResultTests|FullyQualifiedName~Elsa3StrictModeTests"
```

Then the whole Mapping test project, which holds the spec 236–248 JavaScript tests whose expected sources change:

```bash
dotnet test Extensions/Elsa3/tests/Mapping/Tests/Elsa3.Mapping.Tests.csproj
```

**JTest check (throwaway, never committed).** Rerun the baseline probe against a local JTest checkout
(`JTEST_CORPUS` set; the probe skips otherwise) and compare with the spec's baseline:
- End to end with `ConversionServices.ConvertAsync` against `RealActivityCatalog`, reading files with
  `JsonSerializerDefaults.Web`: the same 6 definitions convert.
- Every JavaScript source fed to the rewriter with every name resolving: still 121 rewrite, and no new refusal.
- Strict parse of the rewritten sources, wrapped as Elsa 4 wraps them: 121 of 121 pass (the 2 trailing-comment scripts
  now pass).
- Evaluate the rewritten Expression-mode sources with every parameter `{}`: 0 fail with the `undefined` error (baseline
  16 in 10 definitions).

Record the numbers in the spec's "Measured after" section, then delete the probe and check `git status`.
