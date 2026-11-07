# Data boundaries

## Embedded occurrence

An embedded block persists as an ordinary ActivityNode with an exact `activityVersionId` and activity-owned structure. Extraction introduces one composite node, preserves child node IDs and versions, and changes only the owning parent's child membership/routing plus corresponding layout/presentation. No generic kernel child/edge/block model is added.

Sequence stores authored list order. Flowchart stores its activities, explicit start and connections. BPMN stores elements, flows and bound child activities; its subprocess binds a nested BPMN process. New structures begin without local variables or execution policy overrides.

## Presentation

Extend typed Activity Definition presentation alongside layout, not inside the provider manifest or executable material. Each record has the existing node identity, optional display name and description. Missing older values mean an empty collection. Create/replace/clone/conflict-copy/version/source-freeze paths retain the collection; history snapshots include it. Provider-neutral public contracts must not depend on Workflows implementation projects.

T001 uses Activities Design-owned `ActivityNodePresentationRecord` collections named `ActivityPresentation` on the existing draft/version layout sidecars. Node IDs remain exact; names/descriptions trim to null when blank and reject values beyond 200/2,000 characters or duplicate identities. Omitted/null replacement metadata preserves the prior collection; explicit `[]` clears it. Revision fencing covers graph, layout and presentation together. Thin wire envelopes preserve existing command constructors/deconstructors while the endpoint binder handles the additive array.

Publication and Test Run freeze names for template nodes and graph dependency occurrences. Placement remaps each occurrence to its executable identity; unknown source identities never fall through to outer nodes. Authored call-site names take precedence over template root names. Review/dirtiness/recovery identities include presentation, while provider/template/executable behavior hashes exclude it. Historical inspection uses the execution's exact frozen source reference. Activities Design migrations follow the pre-release regeneration policy in [ADR 0078](../../docs/adr/0078-workflow-executions-are-virtual-actors-and-cluster-membership-is-a-foundation-contract.md); default-empty JSON compatibility does not imply an upgrade path for an existing pre-GA development database.

Actual T001 qualification found that the graph compiler embedded the proposed release version in its runtime contract, changing the template hash even when provider content was identical. Graph compiler `4.0.0` pins that contract to the existing runtime descriptor schema, matching source-owned CLR templates and [ADR 0038](../../docs/adr/0038-artifact-hash-is-purely-behavioral-and-executables-are-content-addressed.md). Newly compiled material has the new compiler fingerprint; previously published templates remain pinned and are not rewritten. Cosmetic releases compiled by the same compiler can share behavior material and retain separate frozen source metadata.

Editor history compares presentation using the persisted normalization rules, so a server acknowledgement containing `null` rather than an omitted field does not discard Undo. Input fields retain in-progress whitespace across equivalent autosave acknowledgements; actual document changes, Undo and Redo update their values. Provider, layout and substantive presentation changes still reset history when they arrive from outside the editor.

## Refactoring preview

Internal Studio plan input: current resource document, scope address, selected node/element identities, exact authorized catalog and requested name/body kind. Output is either located refusal diagnostics or a complete document edit including structure, layout, presentation and post-apply selection. Apply only against the unchanged source snapshot and catalog identities.

The native planner requires an explicit reference context for resource-owned declarations and bindings outside `rootActivity`, including reusable graph output mappings. Hosts supply those values alongside the normalized graph document; Ungroup inspects them for wrapper references, and preview freshness includes them even before a new server revision exists. An omitted context refuses the edit; an explicit empty array means the resource has no such bindings.

## Promotion proposal

Internal Studio proposal contains exact source fingerprint, candidate schema-2 Activity Graph manifest, public contract, explicit input/output/outcome mappings, call-site replacement and diagnostics. Existing public authoring APIs persist/validate/publish the target; the planner is not a second catalog or publication engine. Published target selection triggers fresh comparison/review before replacement. No cross-resource atomic transaction or destructive cleanup is implied.

## Runtime causality and BPMN interchange

No new runtime model is selected solely from source inspection. S001 must prove the current causal-completion behavior and design a narrowly owned supporting bridge if needed. T004 must extend the existing vendor binding-retention boundary for the supported XML profile rather than silently dropping native configuration. Any resulting additive interface/persistence change is recorded and reviewed with its implementing task before production acceptance.
