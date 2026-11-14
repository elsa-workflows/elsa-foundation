# Workflow release transport and verification

An optional, compiler-free target feature for bounded portable release transport. `WorkflowReleaseTransportFeature`
registers the scoped `IReleaseBundleCodec`; its Core package contains only the format boundary and captured result contracts.

The codec preserves original payload bytes and validates transport hashes and the declared graph.
`WorkflowReleaseVerificationFeature` adds scoped semantic orchestration and depends on transport. Owner adapters claim
exact artifact-codec and occurrence-site keys; missing adapters produce an unsupported result. The framework compares complete
actual owner facts with the declaration and checks the actual dependency graph. It installs no owner adapters itself.

Native/BPMN/ETL owner verification, source capture, resource mappings, deployment and activation are separate required
capabilities; this package alone does not deliver workflow releases or promotion. Semantic success establishes neither
authentic origin nor target readiness or serving authority.

ZIP is the portable transport. Directory input uses safe no-follow reads on supported Unix platforms and reports an explicit
unsupported result elsewhere. See [extension points](EXTENSION_POINTS.md) and the
[packed consumer witness](../../../../../../tools/releases/README.md).

## Cross-domain contributions

None. The feature does not add a publication/start authority or reference Design implementations.

Directory input is supported on Linux x64 and macOS ARM64 only; other OS/architecture combinations refuse with
`directory_platform_unsupported`. ZIP input/output remains portable. Directory support requires verified no-follow
open flags and a matching native stat layout, rather than assuming an operating system has one ABI.
