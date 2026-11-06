# Owner semantic verification

**Status: Shared orchestration implemented; owner adapters and integrated qualification pending.** Companion to
[release-bundle](release-bundle.md), [model-artifacts](model-artifacts.md) and [promotion](promotion.md).
The transport contracts establish captured bytes and the declared graph. The optional verification feature dispatches
installed owner adapters and compares their complete actual facts. It installs no owner adapters itself.
No transport result, design record or semantic verdict is an activatable release or completed promotion acceptance evidence.

## Distinct results

| Result | What it establishes | What it does not establish |
|---|---|---|
| Transport valid | Bounded safe input, manifest/blob digests, declared identity/graph structure | Recognized owner codecs, actual dependency completeness, valid behavior or authentic origin |
| Owner semantic valid | Owner-decoded identities/hashes/contracts and complete supported actual dependency/requirement facts agree with the manifest | Publisher authenticity, authored-to-compiled equivalence, target authorization/readiness or integration results |
| Origin confidence | How source/version/publication associations and the claimed producer were obtained/authenticated | Artifact executability, target compatibility or passing tests |
| Target ready | Current target observations meet the validated release's prerequisites and authorized mappings | Reserved future readiness, atomic group activation or successful application |

Keep these results separate in API, CLI and Studio. Unsupported/incomplete owner inspection cannot become semantic success.
Apply still repeats current target checks; tests/evidence retain their own exact subjects, coverage and verdicts.

## Neutral ports and dispatch

The ports live under `Elsa.Workflows.Publishing.Releases.Core`; see
[the public contracts](../../../Essentials/src/Workflows/Publishing/Releases/Core/ReleaseSemanticContracts.cs).

```csharp
public interface IReleaseArtifactSemanticVerifier
{
    ReleaseSemanticAdapterIdentity Identity { get; }
    IReadOnlyCollection<ReleaseCodecKey> SupportedCodecs { get; }
    ValueTask<ReleaseSemanticFacts> VerifyAsync(ReleaseContentMember member,
        ReadOnlyMemory<byte> payload, IReleaseContentReader content,
        CancellationToken cancellationToken = default);
}

public interface IReleaseOccurrenceInspector
{
    ReleaseSemanticAdapterIdentity Identity { get; }
    IReadOnlyCollection<ReleaseSiteKey> SupportedSites { get; }
    ValueTask<ReleaseOccurrenceFacts> InspectAsync(ReleaseSemanticSite site,
        IReleaseContentReader content, CancellationToken cancellationToken = default);
}
```

`ReleaseCodecKey` is the exact `(kind, codec, codecVersion)` tuple; `ReleaseSiteKey` is `(siteKind, discriminator, schemaVersion)`.
Resolving the scoped framework verifier validates blank/duplicate registry claims; missing claims refuse at verification.
`WorkflowReleaseVerificationFeature` depends on transport and checks one canonical framework verifier and codec during shell
post-configuration. It does not build a temporary provider or eagerly instantiate scoped adapters during registration.
Artifact verifiers own payload decoding. They return actual identity, owner-hash result, required edges/requirements, associations,
dependency sites, explicit root content/action capabilities and located diagnostics with `Verified`, `Invalid`, `Unsupported` or `Incomplete` status.
Occurrence facts return actual required edges/requirements and an explicit completeness verdict for their claimed site.
A site carries containing member key, semantic origin/occurrence path, node/input identity, discriminator/schema and owner payload JSON.
Different facets of one occurrence are separate sites, allowing a reusable boundary and a module-owned activity to both be inspected.
Inspection views never replace or reserialize retained bundle bytes. Ports contain no Design entities, compiler types or service locators.
`IReleaseContentReader` indexes captured members, including their tagged owner version, and returns fresh payload copies.
Resolving omitted content from a target/source store is forbidden here. The orchestration revalidates the transport before
creating this indexed reader; adapters cannot supply a replacement declared manifest.
Source export capture, target prerequisite assessment and staging are separate operations; verification writes nothing and executes nothing.

## Complete actual facts, then graph comparison

1. Validate transport and any caller-supplied expected release digest before invoking owner code; unknown codec versions refuse.
2. Decode every member through its registered owner, independently derive identity/hash and enumerate every nested/placed occurrence.
3. Inspect actual semantic references, not only manifest edges or generic dependency arrays. Every required site needs a complete answer.
4. Normalize full typed edge identities, relation, occurrence/dispatch-node path and semantic requirements using ordinal exact identities.
5. Compare actual and declared sets in both directions: omitted, invented, conflicting or incorrectly scoped facts are named refusals.
6. Require each selected root owner's explicit capability for the exact content/action pair. Same-member roots need the
   same explicit allowance. A capability grants no serving authority: it establishes the owner-recognized role only.
   Start actual reachability at selected members, following verified edges/associations to their content; do not seed the
   requested content independently. Resolve the complete closure and check full pins, shared-member consistency, cycles and unrelated extras.
7. Return an immutable verified-subject result bound to transport digest and verifier/codec capabilities; it grants no start authority.

The default status is `Incomplete`; only explicit complete answers become `Verified`. `Invalid` takes precedence over
`Unsupported`, then `Incomplete`. Adapter exceptions become stable, located incomplete diagnostics without exception text;
cancellation propagates. Site/fact/diagnostic bounds apply independently of transport bounds. Declared-set comparison waits
for complete owner answers, so an unsupported codec cannot masquerade as an observed empty closure.

Each supported owner profile declares its closed inspection surface, including supported no-artifact-dependency leaf sites.
The selected owner independently validates its source/publication association before allowing that exact root content;
the existence of two valid captured members does not establish that they belong together. Root capabilities are not a
request for every capable dependency to be selected, and selecting a dependency never implicitly makes it a serving root.
No inspector claim must not mean "no dependency". Unknown/dynamic module references refuse unless the owner supplies a bounded resolver.
Installed activity consumers prove descriptor support, not that arbitrary CLR code has no hidden dependencies or external effects.
Package provenance/policy constraints are classified separately from payload-derived semantic requirements; compare each against its
declared evidence source instead of pretending exact package versions can always be inferred from a consumer key.
Diagnostics identify safe member/occurrence/input paths and stable codes; do not echo payload values, credentials or host paths.

## Required owner inspections

| Owner / site | Independent facts to derive and verify |
|---|---|
| Native workflow | Full retained runtime pin and native behavioral hash; input/storage/consumer contracts; exact declared dispatch edges versus actual static DispatchWorkflow pin and dispatch-node sites |
| Reusable activity | Template behavioral hash, exact catalog/contract/provider/publication association, direct and closed template DAG; inspect workflow placement metadata and invocation origins for omitted template members |
| BPMN | Direct model format/protocol/profile, `ComputeContentHash`, complete service/event worker pins; RunBpmn sites expose exact model edges and source-qualified physical-locator requirements |
| ETL | Artifact format 1/2, definition/positive integer version, hash/framed artifact ID and canonical pipeline; RunEtl literal selection must match embedded `etl.pipeline.v1` metadata and its separate ETL member |

Native hashing uses the Runtime-owned hasher, including explicitly supported historical coverage; do not restamp old bodies as new hashes.
Template hashing uses Runtime-owned [ActivityTemplateHasher](../../../Essentials/src/Workflows/Runtime/Core/Models/ActivityTemplateHasher.cs).
The existing Publishing `ExecutableActivityTemplateBehaviorHasher` forwards to that unchanged algorithm. Fixed vectors
captured from the original helper before extraction preserve historical template identities.
Model hashing uses [RuntimeModelMaterial.ComputeContentHash](../../../Essentials/src/Workflows/Runtime/Core/Models/RuntimeExecutionMaterial.cs), not the native hasher.
ETL uses [EtlArtifactJson](../../../Extensions/Etl/src/Core/EtlArtifactJson.cs)/v2 owner codecs: typed compact property order,
unprefixed lowercase SHA-256 and owner-framed artifact IDs. Generic JSON sorting or ETL draft recompilation is not verification.
Actual omissions are possible today: [reusable placement metadata](../../../Essentials/src/Workflows/Publishing/Elsa.Workflows.Publishing/Services/ActivityTemplatePlacer.cs)
is separate from workflow dispatch edges, and [ETL publication](../../../Extensions/Etl/src/Workflows/Design/Services/EtlPublishedArtifactPinSource.cs)
contributes embedded metadata without a generic workflow dependency edge. Inspectors must cover both.
Native/BPMN/ETL adapters depend on their owning neutral codecs/Core contracts. Target verification never requires Design compilers.

## Implemented Runtime profile foundation

The optional [Runtime profile](../../../Essentials/src/Workflows/Publishing/Releases/Runtime/Elsa.Workflows.Publishing.Releases.Runtime/README.md)
uses two separate packages so the transport packages retain their small dependency graph. Its codec version 1 defines a
new `{formatVersion: 1, content: ...}` envelope containing the source content scope and unchanged typed owner material;
it is not the older naked executable or closure JSON codec. Original artifact/template/version IDs remain the member keys.
Native/catalog versions retain their exact SemVer; the neutral model's retained version label is opaque until its protocol
owner enforces a narrower contract. The wire preserves original opaque JSON, strings and full producer pins.

The codec refuses lossy Unicode, duplicate/unknown fields, contradictory computed aliases and unsupported flattening.
Decode bounds input before copying; encode bounds writer output and scratch while writing, including escaped strings and
repeated opaque values. Recomputed node indexes are excluded from the wire. Target import still retains original blob bytes.

Runtime verifies native historical hashes, template hashes/direct/closed DAGs, full dependency pins and native source-selection
root associations. Neutral model verification emits a mandatory exact protocol site; BPMN supplies its protocol inspector.
The containing-node/model reader exposes captured content only. Unclaimed aliases, consumers, structures, metadata,
provider/contracts, policies or module references remain unsupported. This foundation is not a complete native, BPMN or ETL
profile, source capture, publisher-authenticity proof or target import/activation capability.

## Selected source, artifact producer and origin

Source capture reads the immutable selected version and its actual owner publication/reference association while retaining the closure.
Preserve that selection separately from the first producer identity on deduplicated artifact material; never require those version IDs to equal.
Check each association's exact version/body digest, content authority, publication outcome and artifact/template pin without minting history.
A same-content candidate that never acquired a publication is not silently relabeled published. Missing source evidence is explicit.
For direct BPMN, retain the trusted authored subject/version association separately from its hash-derived model identity.
Portable publication/reference evidence can be self-consistent but unauthenticated. Structural/semantic checks cannot prove the original
publisher produced those bytes, or prove arbitrary authored source compiled to that body without trusted evidence or compilation.
Expose origin as authenticated capture/attestation, self-consistent supplied evidence, or unknown, with its actual basis and limits.
An optional target policy may require a trusted producer/attestation or caller-supplied expected digest; no universal approval gate is added.
An expected digest establishes identity against the caller's independently obtained value; it is not publisher authentication by itself.
Attaching or changing origin/test evidence does not change release content; evidence must still match actual covered subjects.

## Installed, live and qualified prerequisites

Use Modularity Core `IFeatureManagementService.GetCatalogAsync`: package ID/version/manifest hash, enabled state, live descriptor
`RuntimeAvailable`, dormancy and read errors remain distinct. Nuplane obtains package identity/version from `GetPackagesAsync`
and hashes actual manifest bytes through [PackageManifestCatalogMapper](../../../Essentials/src/Modularity/Nuplane/Services/PackageManifestCatalogMapper.cs).
Installed manifests include disabled/unloaded features; [RuntimeFeatureCatalogContributor](../../../Essentials/src/Modularity/Nuplane/Services/RuntimeFeatureCatalogContributor.cs)
reads CShells live descriptors. Neither alone proves that the requested model/storage/connector operation is qualified.
Catalog `Revision` is shell configuration revision, not package inventory revision; fingerprint the non-secret observations actually used.
Combine inventory with actual consumer/schema capabilities, storage-driver/type registries, protocol registrations and owner store/admission capabilities.
Explicit provider `CompilerFingerprint` is producer provenance, not an assembly-version guess or a requirement to compile on target.
Host-bundled/missing package metadata remains identified as such or unknown; never invent a NuGet version from assembly metadata.
Unknown required package/provider provenance or capability is visible and follows the explicit prerequisite/policy contract; promotion installs nothing.

## Source construction and inspection work bounds (source implementation; qualification pending)

`IReleaseBundleBuilder` copies bounded content and discovers actual owner facts before constructing declarations.
It then invokes the shell's selected `IReleaseSemanticVerifier`; replacement refusal remains authoritative.
`MaxInspectionBytes` defaults to 256 MiB and charges every member, including aliases of one physical blob.
It is separate from distinct expanded transport bytes. The verifier checks the actual captured manifest as well as
the supplied transport wrapper before invoking adapters. Logical prerequisite/resource keys cannot conflict even
when their full fact tuples differ.

The optional capture package holds exact source callbacks through builder verification, bounded ZIP creation and
confirmed durable archive acceptance. It requires a schema-ready durable selected archive and refuses incomplete
source callbacks or conflicting shared content. No source adapter is installed implicitly. Native/BPMN/ETL owner
acquisition, import and composed release acceptance remain separate open work.
