# Plan

Use the existing descriptor-provider/startup bridge to register JSON under its current qualified CLR alias, preserving activity contract identities. Extend the existing shared canonical JSON target policy rather than adding activity-specific cases. Use the same JSON recognition for Studio reference pickers and display; retain raw JSON default text under Object expression syntax to avoid JavaScript number narrowing.

Affected areas: Expressions descriptor catalog, Runtime value-conversion compatibility, Studio Workflows shared authoring, and existing tests/e2e helpers. No project/reference, persistence schema, feature identity or public API additions are planned.

Constitution check: framework and Elsa Gate indexes and review checklist reviewed. Preserve existing typed/Object behavior (§2.21.1); add meaningful behavior regressions and red/green controls (§2.23.2). Keep implementation internal by default. Run affected whole projects, architecture guard and generated-maps check. Draft/provisional constitution sections are not ratified here. Review the complete diff independently for correctness, standards and acceptance before delivery.

Validation: catalog resolution/nullable registration; generic JSON capture and variable/input read conversions; scalar/array/object/null/precision boundaries; mounted Studio picker/default round trips; actual published HTTP echo through a JSON variable; exact-head CI and resulting main.

Fresh-server qualification reproduced that an explicitly selected `elsa.json` driver was incorrectly classified as an external variable storage profile. Normalize this built-in inline driver when compiling variable declarations and defaults, preserving refusal for actual external profiles. The causal compiler test and same normal-host journey cover this required repair.

Review round 1 identified element-alias collection descriptors and the reusable activity-contract default editor. Honor the descriptor's explicit collection kind in both reference pickers, and refuse contract defaults whose parsed numeric values would change (with visible guidance); workflow JSON defaults retain raw text. The real null-body journey also reached an incorrectly required Body result projection. Mark that documented optional result optional and update its catalog snapshot; keep unrelated ContentType work (#3476) separate.

Round 2 required raw JSON defaults for JSON collection variables and exact HashSet/Dictionary shape guards, retaining only the existing Array/List interchange. The frozen response-replay closure is not regenerated: its comparison allows only the reviewed optional-Body metadata delta in addition to its existing structured-Body allowance, and still executes the historical artifact unchanged.

The optional Body contract ships as Write Http Response 1.1.0. Existing catalog versions and published artifacts remain immutable; source reconciliation appends the corrected version instead of changing 1.0.0 under an existing identity.
