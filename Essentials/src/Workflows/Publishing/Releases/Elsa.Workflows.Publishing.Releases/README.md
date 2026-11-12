# Workflow release transport

An optional, compiler-free target feature for bounded portable release transport. `WorkflowReleaseTransportFeature`
registers the scoped `IReleaseBundleCodec`; its Core package contains only the format boundary and captured result contracts.

The codec preserves original payload bytes and validates transport hashes and the declared graph. Native/BPMN/ETL owner
verification, source capture, resource mappings, deployment and activation are separate required capabilities; this package
alone does not deliver workflow releases or promotion.

ZIP is the portable transport. Directory input uses safe no-follow reads on supported Unix platforms and reports an explicit
unsupported result elsewhere. See [extension points](EXTENSION_POINTS.md) and the
[packed consumer witness](../../../../../../tools/releases/README.md).

## Cross-domain contributions

None. The feature does not add a publication/start authority or reference Design implementations.
