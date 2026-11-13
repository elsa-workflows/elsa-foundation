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
