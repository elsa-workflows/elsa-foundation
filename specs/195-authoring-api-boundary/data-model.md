# Authoring boundary data model

No new durable entity/table/schema is proposed. Existing publication journal, activation authority, executable/template stores and source references remain canonical. New HTTP projections read those owners without mutation. Exact selectors, fields and unavailable cases are in the [surface contract](contracts/authoring-surface.md).

## Authoring surface declaration

Feature identity `WorkflowsPublishingAuthoringApi`; capability identity `elsa.api.publishing`, existing version convention1, new source feature identity. Relations cover mapped retained operations and two new reads plus publication-identity export, omitting all six test-run relations and all Runtime declarations. The full declaration remains separate; incompatible co-selection fails before mapping.

## Publication slot projection

Identity: definition ID + slot name/slot ID. Authority fields: active activation ID, source kind/ID, revision, updatedAt. Joined fields: existing publication view/status, nullable for foreign occupancy or no journal. Current source ownership controls joining; a foreign source is never changed into Publishing ownership by an ID coincidence. Inactive slots preserve latest journal ordering.

## Published artifact group

Identity: requested definition + artifact ID, with retained `publications` to distinguish repeated publication/source/version identities of one content-addressed artifact. Metadata belongs to the immutable artifact; version/source/slot/lifecycle belongs to each journal/reference. Representative selection is current/live first and deterministic timestamp/ID order.

Current rows require active Publishing-owned authority, Active journal, live Published reference and matching journal/reference definition/version/slot/activation identities and referenced immutable artifact ID/hash; shared artifact definition/version metadata never substitutes for per-publication provenance. Inconsistent current state or identity/scope mismatch is a fixed409 integrity problem, never an empty success. Retired history can retain journal identity after legitimate source/artifact pruning: unavailable rows have explicit reason and null unavailable metadata, never an export target.

No runtime execution counts, test-run references, source input values or layout payloads enter this projection. Unavailable values are null, not made-up zero/default strings. Query operations never reconcile publication state; existing startup/mutation ownership remains.

## Durable support feature settings

Use current aggregate options: provider, explicit private connection string or connection name, schema, pooling, executable cache flag/capacity, private recovery/hierarchy cursor signing keys. Named resource preparation owns effective defaults/overrides and module agreement. The new feature declares one existing Runtime context/module and registers its migrations. Do not create a second history table, partial activation backend or generic settings classification.

## Versioned profile state

Authoring is currently an evaluation fixture. Reviewable new profile/catalog bytes may be published only after generated-host/client proof. Existing v1/v2/v3 pins remain immutable. Explicit selection re-resolution produces a new reviewed accepted result; no accepted/pinned closure changes in place.

## Export request identity

New publication export identifies the exact journal record, then pins its definition-version/source-reference/artifact identities through an additive producer operation. Missing/pruned state refuses; a custom producer without that operation fails closed. Existing version export and current-engine transitive reference/trigger-binding envelope semantics remain unchanged. No new durable export entity or historic runtime-topology guarantee is introduced.
