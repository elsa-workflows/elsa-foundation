# Workflow release transport and verification extension points

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
origin, write owner catalogs or activate workflows. `ValidateParts` revalidates manifest, blobs and optional opaque attachments
from an archive store without re-encoding a ZIP. Those bytes remain transport-only input.

## IReleaseSemanticVerifier

- **Contract:** `Elsa.Workflows.Publishing.Releases.Core.IReleaseSemanticVerifier`.
- **Kind:** Replacement; one selected verifier per shell. Register before the feature or use `services.Replace`.
- **Default and lifetime:** `ReleaseSemanticVerifier`, internal, scoped, registered by `WorkflowReleaseVerificationFeature`.
- **Purpose:** Revalidate captured transport/expected digest, dispatch exact owner codecs/sites, compare complete actual
  identities, hashes, edges, scoped resource requirements and prerequisites, require each selected owner's explicit
  content/action capability, and validate the actual graph from selected roots.
- **Failure:** Immutable `Incomplete`, `Unsupported` or `Invalid` result with bounded stable diagnostics. Cancellation
  propagates. A verified result grants no origin confidence, target readiness, catalog write or start authority.
- **Known implementations:** `ReleaseSemanticVerifier` *(intra-domain — default)*.

## IReleaseBundleBuilder

- **Contract:** `Elsa.Workflows.Publishing.Releases.Core.IReleaseBundleBuilder`.
- **Kind:** Replacement; exactly one selected builder per shell, registered by `WorkflowReleaseVerificationFeature`.
- **Default and lifetime:** `ReleaseBundleBuilder`, internal, scoped.
- **Purpose:** Freeze bounded member bytes, inspect them through the same exact owner registry and actual-fact rules,
  then construct manifest declarations from those observed edges, resources and prerequisites. Validate root
  content/action capabilities, complete reachability, cycles and bounds before returning a bundle.
  The selected `IReleaseSemanticVerifier` then verifies the constructed transport; a replacement verifier's refusal
  remains authoritative. Both passes enforce `MaxInspectionBytes`, separately from distinct expanded transport bytes.
- **Failure:** Structural input throws value-free `ReleaseBundleFormatException`; owner refusal returns a result with
  no bundle and explicit semantic status/diagnostics. Requested cancellation propagates.
- **Known implementations:** `ReleaseBundleBuilder` *(intra-domain — default)*.

This service does not acquire source retention or read missing members. Source owners must supply a complete retained
closure and hold their acquisition boundary through durable archive creation. A returned bundle is not a created durable
release, an authenticated publication or target admission. Attachments/evidence remain separate archive operations.

## IReleaseArtifactSemanticVerifier and IReleaseOccurrenceInspector

- **Contracts:** `Elsa.Workflows.Publishing.Releases.Core.IReleaseArtifactSemanticVerifier` and
  `Elsa.Workflows.Publishing.Releases.Core.IReleaseOccurrenceInspector`.
- **Kind:** Exact-key strategy contributions. Each installed adapter claims immutable codec/site keys and an adapter
  identity/version. Select exactly one implementation for each full key; different versions may coexist.
- **Composition:** Resolving the scoped framework verifier rejects blank/duplicate claims. Registration does not build
  a temporary service provider. Missing claims return unsupported results during verification.
- **Purpose:** The artifact owner decodes retained bytes and enumerates complete actual facts and occurrence facets.
  It explicitly allows exact root content/action pairs only after validating their owner association. Capable dependencies
  are not selected automatically; root capabilities establish a semantic role and confer no start authority.
  Site inspectors return complete actual facts for their claimed facets, including an explicit supported leaf answer.
  `IReleaseContentReader` exposes only indexed captured members and fresh payload copies; no target fallback is permitted.
- **Failure:** Unknown/incomplete inspection refuses semantic success; adapter exceptions yield sanitized incomplete
  diagnostics. A diagnostic contains a stable code and member/origin/input location, never raw payload or exception text.
- **Known implementations:** None installed by this feature. Native, BPMN and ETL owner adapters remain separate work.

## Feature inheritance

Both features are public/inheritable with virtual `ConfigureServices` and `PostConfigureServices`.
`WorkflowReleaseVerificationFeature` declares its transport dependency. Derived features can replace the canonical services
deliberately; host composition still enforces one selected implementation.

There are no events, startup tasks or recurring tasks. The scoped exact-key registry is the semantic strategy composition seam.
