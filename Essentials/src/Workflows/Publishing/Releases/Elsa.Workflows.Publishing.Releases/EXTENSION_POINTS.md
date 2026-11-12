# Workflow release transport extension points

## IReleaseBundleCodec

- **Contract:** `Elsa.Workflows.Publishing.Releases.Core.IReleaseBundleCodec`.
- **Kind:** Replacement; one selected codec per shell. Register an override before the feature or use `services.Replace`.
  The feature's post-configuration checks reject duplicate codec registrations.
- **Default and lifetime:** `ReleaseBundleCodec`, internal, scoped, registered by `WorkflowReleaseTransportFeature`.
- **Purpose:** Capture original manifest/blob/attachment bytes, validate bounded directory/ZIP input, canonical manifest
  identity and the declared graph. The result is explicitly transport validation, not owner-semantic validation or readiness.
- **Failure:** Sanitized `ReleaseBundleFormatException.Code` for malformed/unavailable transport. Cancellation remains
  cancellation; input streams remain caller-owned. No payload values or credential-bearing paths are placed in diagnostics.
- **Known implementations:** `ReleaseBundleCodec` *(intra-domain — default)*. No cross-domain contributions are registered.

The contract does not compile source, recognize installed owner codecs, prove actual dependency completeness, authenticate
origin, write owner catalogs or activate workflows. Those gates precede any later release application capability.

## Feature inheritance

`WorkflowReleaseTransportFeature` is public/inheritable with virtual `ConfigureServices`. Derived features can retain the
default parsing contract or replace its codec deliberately; host composition still enforces one selected implementation.

There are no events, contribution loops, startup tasks or recurring tasks in this transport feature.
