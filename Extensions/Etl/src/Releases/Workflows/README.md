# Native RunEtl release inspection

`EtlReleaseWorkflowsFeature` adds the ETL owner's exact occurrence facets to the native Runtime release bridge.
It depends on `EtlReleaseVerification` and `WorkflowReleaseRuntime` and installs no execution workers, catalogs or stores.
The standalone ETL release codec keeps its original compiler-free dependency closure; this optional addon references
the actual native `RunEtl` assembly and the Runtime bridge contracts.

The two schema-1 sites are `runtime.clr-activity/Elsa.Etl.Workflows.Runtime.Activities.RunEtl` and
`runtime.node-metadata/etl.pipeline.v1`. Both inspect the complete containing captured workflow or template.
The CLR alias, leaf shape, required literal `Pipeline`, activity input/result/outcome contract and correctly owned
resume declaration must agree with the actual activity. Contract version agrees with the retained node version;
source-owned template consumer labels and native workflow catalog SemVer labels remain distinct.
Structural fault handling, rescheduled-fault child handling and child-control hints are refused for this leaf.

A published workflow must contain its embedded naked ETL owner envelope. The bounded strict owner codec validates
that envelope without manufacturing source scope, and every field plus the complete canonical pipeline must match
the captured ETL release member. Its definition, positive integer version and full unprefixed content hash must match
the canonical `EtlPublishedSelection` token. Source authority, tenant and persistence partition match exactly.

A reusable template can retain a static exact literal before metadata pin embedding. The inspector derives the two
supported v1/v2 framed artifact IDs and reads at most those two captured keys. It requires a unique exact captured
association. Dynamic selectors and unsupported conversion behavior remain unsupported; missing or conflicting
captured content is invalid. There is no source or target lookup, partition default, compilation or draft replay.

Only the metadata facet emits the ETL dependency when a pin exists. For a static template without metadata, the CLR
facet emits it. That same canonical facet emits the `etl.native-workflows` schema-1 runtime capability requirement;
this is a target requirement, not evidence that the target has installed checkpoint enrichment, intent handlers or
resumption. Per-occurrence prerequisite keys use length-framed source member/node identities, hashed incrementally.
Connector resources and host ceiling requirements remain attached to the captured ETL member.

A generic runtime dependency, when present for this node, must agree with the exact ETL owner ID/hash; the addon
does not emit a duplicate edge. The current native generic dependency owner supports only native workflow/model
members, so a handcrafted generic ETL dependency still fails that owner's inspection. Normal `RunEtl` publication
uses its metadata-only ETL dependency.

Captured-member decoding is memoized within the identity of one immutable captured reader. Reusing an inspector for
another reader does not reuse a verdict by digest, ID or source scope. This package establishes neither authenticated
publisher origin, durable historical materialization, target readiness nor serving activation/recovery.
