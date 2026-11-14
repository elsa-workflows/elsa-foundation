# Runtime release bridge extension points

- **Layer:** Publishing release Runtime adapter; bridges shared release contracts and Runtime Core without Design.
- **Contract signatures and override obligations:** [Runtime Core catalog](../Core/EXTENSION_POINTS.md).
- **Dependent callers:** Scoped release semantic verifier; captured model/activity inspectors and Runtime release tests.

## Replacement services

- `IReleaseRuntimeArtifactCodec` → internal `ReleaseRuntimeArtifactCodec`, scoped.
- `IReleaseRuntimeSiteReader` → internal `ReleaseRuntimeSiteReader`, scoped.

`WorkflowReleaseRuntimeFeature` uses `TryAddScoped`; overrides registered first are preserved. Its post-configuration
guard rejects duplicate canonical services and requires exactly one selected Runtime-owned `IWorkflowExecutableHasher`.
The public feature is inheritable and its configuration methods are virtual. Its only feature dependency is
`WorkflowReleaseVerification`; read-only hosts can select a hasher without starting the Runtime feature.

## Semantic strategies

`ReleaseRuntimeArtifactVerifier` contributes exact artifact codec/version claims through
`IReleaseArtifactSemanticVerifier`. It validates Runtime identities/hashes and generic inventories, then emits mandatory
owner inspection sites. Module assemblies contribute `IReleaseOccurrenceInspector` implementations for exact site keys.
An installed runtime consumer alone does not prove that its descriptor, CLR activity, structure or metadata is a leaf.
Missing inspectors remain unsupported; duplicate claims are rejected by semantic registry composition.
Artifact and site strategies must coordinate their inventories: each actual edge, resource or prerequisite is reported
by exactly one strategy. Edge occurrence labels are owner-trusted; the shared verifier checks the source member and
complete equality with the manifest rather than inferring a site's edge label.

Member read selections contribute their actual selected-type alias and exact activity-value JSON profile revision,
plus the member-selection engine requirement. Profile revisions are retained facts for target readiness; this adapter
does not substitute the installed target profile or decide that a different revision is compatible.

The captured-site reader returns full node/container context or neutral model context. Model protocol inspection is a
separate mandatory site; the generic Runtime model codec never grants serving capability or accepts a protocol graph.
One internal scoped cache shares typed decodes across artifact checks and sites for the same captured reader identity.
Its concurrent lazy entries never reuse values by digest across readers and confer no owner-hash verdict. Installed
inspectors are trusted verification code: typed legacy model views are read-only inputs and inspectors must not mutate
nested collections. Original captured bytes remain immutable; deep mutation isolation of legacy typed views is not claimed.
There are no events, startup tasks, recurring tasks, store lookups, imports or activation hooks in this feature.

## Original activity publication evidence and portable catalog inspection

Activity-version codec profile2 retains original contract/provider JSON text, original publication projection
metadata and an explicitly known nullable owner tenant. Profile1 remains readable and encodes without an added
null evidence field. The member codec version must agree with the actual evidence profile; a manifest cannot
hide or invent evidence by relabeling its version. Source namespace and owner-tenant facts grant no target visibility.

| Strategy | Contract / layer / default | Supported sites and signature | Replacement guidance and callers |
|---|---|---|---|
| `RuntimeCatalogReleaseInspector` | `IReleaseOccurrenceInspector` in Releases.Core; registered by the optional Runtime profile | Public-contract `elsa.activity-contract/1` and provider `elsa.clr-activity/1`; `ValueTask<ReleaseOccurrenceFacts> InspectAsync(ReleaseSemanticSite site, IReleaseContentReader content, CancellationToken cancellationToken = default)` | Compares captured public contracts, exact template/provider association, the existing producer fingerprint recipe, original projection fragments, counts and requirements. Unknown fields/schema/provider profiles refuse; it loads no CLR type and invokes no compiler. The shared semantic verifier and builder dispatch it for captured catalog sites. |

This partial native verification capability grants no root action, source publication authority, import or
activation. Placement metadata and individual activity-module semantics remain mandatory separate owner sites.
The production package dependency closure is unchanged; Runtime-only consumers require no Design/compiler/source package.

### Retained placement associations

The built-in placement inspector checks the original workflow-producer origin, exact catalog/template pins,
placed node and dependency-frame inventories, and child-slot order. It admits the existing six-field placed
CLR descriptor only with the separately mandatory placement and exact CLR owner sites. It does not resolve
CLR types, recompile a template or approve Graph-provider semantics. Unsupported inline template placements
remain explicit refusals. `IReleaseRuntimeSiteReader.ReadTemplate` and `ReadActivityVersion` expose only
qualified captured members for those independent owner inspectors; they do not read a live source catalog.
