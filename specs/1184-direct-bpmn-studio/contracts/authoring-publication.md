# Authored direct publication and interchange

Existing workflow save/promote is unchanged. Direct publication is an explicit optional operation on an immutable root BpmnProcess version; the server reads the version/provenance. Runtime-only API composition installs no Design services.

## Binding representation

`Properties["elsa.direct.binding"]` is an owned JSON object, formatVersion1, maximum16KiB UTF-8, bounded depth. Discriminator `kind`: human or nativeService. Human can require claiming; nativeService carries exact published sourceRef/full native pin, business-error code and existing versioned fault policy. Caller/tenant/Authority never come from metadata.

The interchangeable attribute is `elsa:directBinding` in the existing Elsa vendor namespace. Preserve the exact property value. Malformed/oversized metadata raises contextual interchange error. Unknown versions stay unexecutable, never dropped or treated as legacy human work; publication validates complete schema/inventory/dependencies.

Old documents emit no new attribute. Frozen envelope/childNodeId semantics stay unchanged. Meaningful Dropped/Degraded import findings cannot replace the active direct document; retain original XML for correction.

## Publication

Mechanically project graph/binding identities, then delegate executable/profile validation to IBpmnModelRuntime/released engine. Refuse nested native intent, unsupported constructs and mixed inventories outside the profile. Never infer behavior from layout.

New source-aware publication carries server-read immutable version/label/layout; replay compares that provenance and behavior. Preserve raw publication signatures, synthetic material identities and fingerprints. Same content/source+operation replays; changed source/content conflicts. Missing source before publication refuses; accepted material does not depend on later Design availability.

## ETL selection

Choose a closed published service workflow or create a normal RunEtl service workflow with the existing pipeline selector and save/review/publish lifecycle. Bind full worker pin/live source. No hidden publication/GC path, arbitrary worker inputs or second ETL dispatcher. Later pipeline versions cannot change accepted pins.
