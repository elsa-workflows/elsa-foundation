# ETL release extension points

## IEtlReleaseArtifactCodec

- **Contract:** `Elsa.Etl.Releases.Core.IEtlReleaseArtifactCodec`.
- **Kind:** Replacement; select one codec per shell before the feature or use `services.Replace`.
- **Default and lifetime:** `EtlReleaseArtifactCodec`, internal, scoped, registered by `EtlReleaseVerificationFeature`.
- **Purpose:** Encode/decode copied source-scope and unchanged ETL owner artifact bytes; verify bounded input, original
  identity, integer version, canonical pipeline and intrinsic owner semantics without external lookup or artifact recompilation.
- **Failure:** `EtlReleaseArtifactException` carries only a stable code and invalid/unsupported classification.
  Target readiness, publisher origin, catalog writes and admission remain separate.
- **Known implementations:** `EtlReleaseArtifactCodec` *(intra-domain — default)*.

## IReleaseArtifactSemanticVerifier

- **Contract:** `Elsa.Workflows.Publishing.Releases.Core.IReleaseArtifactSemanticVerifier`.
- **Kind:** Exact-key strategy contribution to Workflows Publishing release verification.
- **Implementation:** `EtlReleaseSemanticVerifier`, internal, scoped *(cross-domain — ETL owner contribution)*.
- **Claims:** `(etl-artifact, elsa.etl-artifact, 1)` and `(etl-artifact, elsa.etl-artifact, 2)`.
- **Purpose:** Derive actual owner identity/version/hash, all connector/role-scoped binding requirements and protocol/host
  ceiling prerequisites. Explicit root capability is `MakeAvailable` for the exact artifact only; serving activation is refused.
- **Failure:** Invalid or unsupported content remains nonverified with safe located diagnostics. No source/target fallback,
  package installation, catalog write or execution occurs. Canonical shared composition rejects duplicate exact-key claims.

`EtlReleaseVerificationFeature` is public and inheritable with virtual service and post-configuration methods. It depends
on `WorkflowReleaseVerification` and checks one canonical codec. It installs no ETL hosted services or runtime tasks.
