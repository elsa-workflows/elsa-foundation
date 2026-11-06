# ETL native workflow release extension point

## IReleaseOccurrenceInspector

- **Contract:** `Elsa.Workflows.Publishing.Releases.Core.IReleaseOccurrenceInspector`.
- **Kind:** Exact-key strategy contribution; duplicate claims fail canonical shared composition.
- **Implementation:** `RunEtlReleaseInspector`, internal, scoped *(cross-domain — ETL native owner contribution)*.
- **Claims:** `runtime.clr-activity/Elsa.Etl.Workflows.Runtime.Activities.RunEtl/1` and
  `runtime.node-metadata/etl.pipeline.v1/1`.
- **Purpose:** Inspect exact captured literal selection, owner envelope, dependency, original source scope,
  leaf input/result/outcome contract and resume ownership. Derive one canonical dependency and native capability
  requirement per occurrence without a compiler or source/target fallback.
- **Failure:** Safe located invalid/unsupported codes. Unknown or dynamic behavior never receives a complete verdict.
- **Composition:** `EtlReleaseWorkflowsFeature` depends on `EtlReleaseVerification` and `WorkflowReleaseRuntime`.
  It is public and inheritable with virtual service configuration and installs no native/ETL runtime services.

The existing `IEtlReleaseArtifactCodec.DecodeOwnerArtifact` is the strict bounded decoder for embedded naked ETL
owner envelopes. It infers no source scope and does not accept or produce a release-member wrapper.
