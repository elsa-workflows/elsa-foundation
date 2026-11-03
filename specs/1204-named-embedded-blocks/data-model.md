# Data boundaries

## Embedded occurrence

An embedded block persists as an ordinary ActivityNode with an exact `activityVersionId` and activity-owned structure. Extraction introduces one composite node, preserves child node IDs and versions, and changes only the owning parent's child membership/routing plus corresponding layout/presentation. No generic kernel child/edge/block model is added.

Sequence stores authored list order. Flowchart stores its activities, explicit start and connections. BPMN stores elements, flows and bound child activities; its subprocess binds a nested BPMN process. New structures begin without local variables or execution policy overrides.

## Presentation

Extend typed Activity Definition presentation alongside layout, not inside the provider manifest or executable material. Each record has the existing node identity, optional display name and description. Missing older values mean an empty collection. Create/replace/clone/conflict-copy/version/source-freeze paths retain the collection; history snapshots include it. Provider-neutral public contracts must not depend on Workflows implementation projects.

## Refactoring preview

Internal Studio plan input: current resource document, scope address, selected node/element identities, exact authorized catalog and requested name/body kind. Output is either located refusal diagnostics or a complete document edit including structure, layout, presentation and post-apply selection. Apply only against the unchanged source snapshot and catalog identities.

## Promotion proposal

Internal Studio proposal contains exact source fingerprint, candidate schema-2 Activity Graph manifest, public contract, explicit input/output/outcome mappings, call-site replacement and diagnostics. Existing public authoring APIs persist/validate/publish the target; the planner is not a second catalog or publication engine. Published target selection triggers fresh comparison/review before replacement. No cross-resource atomic transaction or destructive cleanup is implied.

## Runtime causality and BPMN interchange

No new runtime model is selected solely from source inspection. S001 must prove the current causal-completion behavior and design a narrowly owned supporting bridge if needed. T004 must extend the existing vendor binding-retention boundary for the supported XML profile rather than silently dropping native configuration. Any resulting additive interface/persistence change is recorded and reviewed with its implementing task before production acceptance.
