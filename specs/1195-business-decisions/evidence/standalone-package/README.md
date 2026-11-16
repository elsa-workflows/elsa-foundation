# Standalone Decisions package witness

The pure engine at `0718297e031c8b63f2043631c64e4358d7fd07f1` passed 96 evaluator tests and a cold, package-only console consumer with 46 assertions. The consumer required no web host, database or workflow runtime. `receipt.json` records the real calculator-generated versions, input fingerprints, exact nupkg hashes and restored dependency closure. `Consumer.cs` is the executed source; `consumer.csproj.template` contains its only PackageReference.

The checks cover approval boundaries and actual matched/selected explanations, portable artifact/result JSON, >2^53 and full 34-digit numbers, low-budget refusal, standard lower-case tagged scalar JSON materialization, a leading UTF-8 signature with distinct original hash, and an unnamed single output with identical executable meaning. The no-name regression failed as InvalidOutputName before correction; its red log and whole-suite green log are retained. The BOM red/green logs are historical witnesses at the BOM correction; the current package repeats that behavior.

Reproduce using the normal `dotnet` build-slot wrapper:

1. Run `tools/versioning/Elsa.Versioning.Calculator` with `--commit 0718297e031c8b63f2043631c64e4358d7fd07f1 --record-ref origin/publish-state --branch agent/codex/3602-business-decisions --output <private>/computation.json --pack-properties <private>/package-versions.props`. Pin the record-ref commit from the receipt to reproduce the exact same calculation; a moved publication ref is a new calculation.
2. Pack `Extensions/Decisions/src/Core/Elsa.Decisions.Core.csproj` and `Extensions/Decisions/src/Elsa.Decisions/Elsa.Decisions.csproj` in Release with `-p:CustomBeforeDirectoryBuildProps=<absolute-generated-props>` to a new private feed. Verify both nuspec repository commits and embedded `input-fingerprint.json` against the generated calculation/props.
3. Copy the consumer into a new directory outside the checkout, create `consumer.csproj` from its template and create a NuGet.Config containing only the private feed and nuget.org. Use initially empty `NUGET_PACKAGES` and `NUGET_HTTP_CACHE_PATH` directories for restore and run. Inspect `obj/project.assets.json`: every library must have type package; the nine recorded libraries contain no Workflow, Rules, Tasks, EF, ASP.NET or Studio dependency. Core's nuspec contains no external runtime dependency.

No packages were published. This proves the standalone slice at the stated source, independently of later integration changes. Original calculator/pack/restore logs, nupkgs and isolated cache are retained in the private qualification directory; committed receipts keep the bounded facts and executed consumer without vendoring binaries.

The Core/default source is unchanged between this witness and the integrated review-fix commit `2c8925a2ac808db26c6cb1b051e534b3736afbfd`; integration and documentation do not expand this dependency closure.
