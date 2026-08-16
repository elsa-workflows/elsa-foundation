# ASP.NET Core OpenAPI collectible-contract retention reproduction

This framework-only reproduction maps one endpoint, generates a document through the built-in
`IOpenApiDocumentProvider`, removes the endpoint, disposes the provider, unloads the contract
`AssemblyLoadContext`, and performs bounded compacting collections.

Run from this directory with .NET SDK 10.0.300 / runtime 10.0.8:

```bash
dotnet build Contract/Contract.csproj
dotnet run --project Repro.csproj -- "$PWD/Contract/bin/Debug/net10.0/OpenApiRetention.Contract.dll"
```

Expected output with `Microsoft.AspNetCore.OpenApi` 10.0.10:

```text
Stable metadata:      CollectionResult { Collected = True, ... }
Collectible metadata: CollectionResult { Collected = False, LoadContextAlive = True, AssemblyAlive = True, ContractTypeAlive = True, DelegateAlive = False, ProviderAlive = False }
```

The two cycles use the same collectible implementation. The only changed input is whether
`IAcceptsMetadata.RequestType` and `IProducesResponseTypeMetadata.Type` name stable host records or
records from the collectible contract assembly. The reproduction does not inspect or mutate private
framework caches, wait for time-based eviction, or keep a strong collectible reference in its result.

Captured receipts from the accepted matrix are checked in as `results-macos-arm64.txt` and
`results-linux-amd64.txt`.
