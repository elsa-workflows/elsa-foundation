# Research: diagnostics group and catalog evolution

## Decision 1: publish a new catalog snapshot

The current `foundation-selection-catalog-v1.json` has an immutable digest and contains `embedded-runtime@1` but no groups. Editing it would invalidate authored catalog pins. Keep its bytes and digest, publish v2 with the same profile definition and one new group, and let default planning/generation choose the snapshot matching an authored exact pin. Init uses v2 for new documents. A mismatched explicit `--catalog` stays mismatched rather than falling back to another bundled file.

## Decision 2: select four server diagnostic IDs

The [profile-catalog exploration](../../docs/reports/runtime-composition/profile-catalog-contract.md) proposed the four-ID `diagnostics-ef` group. The feature declarations make the two EF-to-base `DependsOn` edges required. They do not show a reverse dependency or authorize a new provider/connection rule. The [#1969 two-target proof](https://github.com/elsa-workflows/elsa-foundation/issues/1969) used two explicit diagnostic bindings; a group-wide database choice is an editor gesture that writes those bindings, not a third runtime inheritance layer.

## Decision 3: keep readiness evidence outside the group

The pure planner already reports missing inventory and persistence evidence as unverified. The group cannot prove packages are available on a target host, that migrations are approved, or that in-process engine tracing is connected. The developer reference names those prerequisites. Existing planner expansion and provenance are reused; no new EF matrix leg or runtime host claim is warranted for selection-only changes.
