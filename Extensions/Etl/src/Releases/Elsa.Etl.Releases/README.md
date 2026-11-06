# Retained ETL release verification

`EtlReleaseVerificationFeature` adds bounded compiler-free content inspection for ETL artifact formats 1 and 2.
It depends on the shared workflow release verification feature and installs no ETL worker, catalog or admission service.
The library calls the ETL Runtime owner's [pure content validator](../../Runtime/Elsa.Etl.Runtime/Hosting/EtlArtifactSemantics.cs);
referencing that assembly does not enable its host feature.

The versioned release codec is a new JSON envelope containing `source` and `artifact`. `source` holds the original
environment authority plus the existing ETL tenant/partition scope. `artifact` retains all six owner envelope fields,
including its unchanged canonical pipeline. This wrapper is separate from the existing naked `EtlArtifact` wire shape.
Neither its source-scope claim nor a valid content hash authenticates the original publisher.

Member keys use the original `ArtifactId` with kind `etl-artifact` and codec `elsa.etl-artifact`; codec version is the
artifact's format version, 1 or 2. Owner versions are tagged positive integers. Native workflow SemVer is never substituted.
The original unprefixed lowercase content hash and length-framed artifact ID are checked independently of the release
blob's prefixed SHA-256. Pipeline property order and original canonical JSON remain owner-defined; no generic sorting,
upcast, draft replay or artifact recompilation occurs.

The codec bounds output while serializing and input before parsing, rejects duplicate JSON properties and refuses
unrepresentable UTF-16 rather than silently repairing strings. A malformed envelope/hash/ID/pipeline is invalid.
Serialization scratch is bounded to six times the output budget plus 4 KiB for writer growth/escaping hints; actual
emitted bytes remain limited to the output budget. Original header and opaque pipeline bounds are checked before writing.
A self-consistent unknown connector/pagination discriminator remains unsupported; unknown codec versions are refused by
the shared exact-key registry. Retained bytes remain separate from any later target-owned persistence projection.

Owner inspection derives every source, optional lookup, main route and error route as a scoped resource requirement.
The key includes the artifact member, original occurrence path, alias and connector/role kind, for example
`etl.binding.http.source` or `etl.binding.jsonl.error-output`, at schema `1`. Equal alias text does not combine different
roles or occurrences. A standalone root permits `make-available` only; it grants no serving slot or independent start.

Prerequisites describe actual connector/protocol capabilities and current host ceiling demands. Capacity requirements
use kind `runtime-capacity`, an ETL-owned identity and integer-range constraint `[n,)`. Producer compiler identity is
not inferred from assemblies and no target compiler is required. These are requirements, not observations that a target
already satisfies them. Host capacity, binding existence/approval, provider/store qualification, resource revisions and
credentials remain separate readiness/admission checks.
Prerequisite keys include the original artifact ID so several retained ETL versions can coexist in one bundle.

This foundation does not yet capture source catalogs, import historical versions, translate target mappings, retain
rollback roots or activate deployments. Native `RunEtl` occurrence inspection is a separate workflow bridge.
The first complete native/BPMN/ETL promotion delivery remains open until those owner integrations and composed witnesses pass.
