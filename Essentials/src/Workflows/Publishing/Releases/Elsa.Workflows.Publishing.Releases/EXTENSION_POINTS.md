# Workflow release transport extension points

## IReleaseBundleCodec

- **Contract:** `Elsa.Workflows.Publishing.Releases.Core.IReleaseBundleCodec`.
- **Layer:** Core contract; default implementation in the Publishing release engine.
- **Signatures:** `Validate(manifestUtf8, blobs, limits)` returns `IReleaseBundleTransport`;
  `ReadZipAsync(input, limits, cancellationToken)` and `ReadDirectoryAsync(directory, limits, cancellationToken)`
  return `ValueTask<IReleaseBundleTransport>`; `WriteZipAsync(bundle, output, limits, cancellationToken)` returns `ValueTask`.
  Full parameter types and defaults are declared in [the contract](../Core/ReleaseBundleContracts.cs).
- **Kind:** Replacement; one selected codec per shell. Register an override before the feature or use `services.Replace`.
  The feature's post-configuration checks reject duplicate codec registrations.
- **Default and lifetime:** `ReleaseBundleCodec`, internal, scoped, registered by `WorkflowReleaseTransportFeature`.
- **Purpose:** Capture original manifest/blob/attachment bytes, validate bounded directory/ZIP input, canonical manifest
  identity and the declared graph. The result is explicitly transport validation, not owner-semantic validation or readiness.
- **Failure:** Sanitized `ReleaseBundleFormatException.Code` for malformed/unavailable transport. Cancellation remains
  cancellation; input streams remain caller-owned. No payload values or credential-bearing paths are placed in diagnostics.
- **Known implementations:** `ReleaseBundleCodec` *(intra-domain — default)*. No cross-domain contributions are registered.
- **Override when:** A host needs a different bounded transport implementation while preserving exact captured bytes,
  canonical identities, path/link refusals and sanitized failures. An override does not grant owner validity or start authority.
- **Dependent callers:** Host consumers resolving `IReleaseBundleCodec`, the packed transport consumer under
  [`tools/releases`](../../../../../../tools/releases/README.md), and transport tests. This slice registers no owner import or activation caller.

The contract does not compile source, recognize installed owner codecs, prove actual dependency completeness, authenticate
origin, write owner catalogs or activate workflows. Those gates precede any later release application capability.

## Feature inheritance

`WorkflowReleaseTransportFeature` is public/inheritable with virtual `ConfigureServices`. Derived features can retain the
default parsing contract or replace its codec deliberately; host composition still enforces one selected implementation.

There are no events, contribution loops, startup tasks or recurring tasks in this transport feature.
