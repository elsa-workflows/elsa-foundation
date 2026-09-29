# Accepting edited composition intent

Contract for [#2165](https://github.com/elsa-workflows/elsa-foundation/issues/2165), under developer epic [#1962](https://github.com/elsa-workflows/elsa-foundation/issues/1962) and program [#1959](https://github.com/elsa-workflows/elsa-foundation/issues/1959). Implementation and verification are pending.

## Problem and decision

At source baseline `c40f1caa3`, `composition init` and `composition import` create initial accepted selections. After editing a profile, group, addition or removal, `composition plan` reports `candidate-re-resolution`, and `composition generate` refuses that drift. There is no supported command to accept the edited expansion; maintaining `accepted.featureIds` manually undermines the intended developer workflow.

Add an explicit, file-only acceptance step using the existing [v1 authored model and planner](../../../specs/174-profile-selection-planner/contracts/selection-planner-v1.md):

```text
composition accept --composition edited.json --output accepted.json
  [--catalog catalog.json] [--workspace-profile profile.json ...]
```

The command accepts a new exact selection within the composition's existing catalog pin. It preserves the selection semantics: zero or one pinned profile, flat pinned groups, additions, removals, and required-dependency validation. It does not silently add a dependency, upgrade a catalog or definition, acquire a package, access a database or change a host.

## Review and publication

Capture the bytes of every supplied composition, catalog and workspace-profile file once, and parse those captured bytes. Use the bundled snapshot matching the composition's catalog pin when `--catalog` is omitted. Reject symbolic-link and nonregular input files rather than introducing an ambiguous source identity. All supplied file bytes must still match immediately before publication, including files whose content does not affect the selected set.

Show a value-free review containing the exact previously accepted and candidate IDs, added/removed IDs, retained/dropped lock feature IDs, and safe planner findings. Validate every identity before rendering. Opaque settings/resources, arbitrary rationale, paths, connection values and parser excerpts must not appear in terminal diagnostics. Keep inventory and persistence explicitly unverified; accepting authored intent does not establish runtime readiness.

Refuse `catalog-pin-unresolved`, `accepted-pin-unresolved`, `definition-pin-unresolved`, or `required-dependency-missing`. The only permitted findings are `candidate-re-resolution` (advisory), `inventory-unverified` (unresolved) and `persistence-unverified` (unresolved); unknown codes or unexpected severity combinations refuse. This keeps the acceptance gate closed if the planner later adds a new authored-integrity failure. Missing runtime/persistence evidence remains visible but does not prevent this authored-intent operation. With no supplied host inventory, dependency validation covers reviewed definition edges; it does not prove the dependency closure of an arbitrary added feature.

Require interactive input and the exact word `accept`. Declining, cancellation or redirected input publishes nothing. There is no noninteractive bypass.

Publish to a fresh file through private staging and atomic, non-overwriting publication. Adjacent output is allowed; input and existing output can never be overwritten. Recheck all input snapshots after review and before publication. Reuse the existing publication mechanism while preserving import's separate prohibition on writing into its host source directory. Cleanup belongs to the publication mechanism.

## Preserved document and locks

Change only `accepted.featureIds` and `accepted.locks` in the captured JSON document. Retain the catalog digest, authored profile/group pins, additions/removals, and opaque settings/resources, including unknown fields inside those objects. Preserve absent optional fields as absent and explicit nulls as null. Semantic JSON preservation is required; whitespace and object formatting may change. The typed reader collapses absent and null optional fields, so serializing the typed model alone cannot meet this boundary.

Existing accepted locks remain historical observations, not fresh package evidence. Keep original locks for feature IDs still selected, drop locks for removed IDs, and invent no new locks. Do not replace existing locks with the planner's empty observed-lock list when inventory is absent. A newly added feature can be accepted without a lock, with availability still unverified.

## Demonstration and limits

Verify the actual CLI journey: create/import an accepted document, edit individual selection intent, inspect the drift, accept to a new file, inspect that the candidate equals the accepted set, generate a candidate, and read back its exact enabled IDs. Preserve canary-bearing opaque JSON without displaying its values. Negative evidence must include every supplied input kind changing during review, invalid pins, missing reviewed required dependencies, declined/noninteractive review, output collision and cleanup.

An unchanged command with the acceptance update removed must fail the downstream generation journey: this is a behavioral gate, not merely a JSON serialization test. Run the affected CLI suite, architecture guard, generated-map check and required exact-head CI before claiming delivery.

Catalog-version transitions, new package-lock acquisition, live host evidence, production identity, in-place apply/recovery and the [real-user builder study](builder-ux/moderated-evaluation.md) remain separate outcomes. Revisit catalog transitions when an explicit upgrade workflow and lock acquisition contract are reviewed.
