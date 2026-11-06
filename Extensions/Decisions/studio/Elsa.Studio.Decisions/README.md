# Decisions Studio module

`DecisionsStudio` contributes the `/decisions` resource area and the `decision-version` activity property editor through the public Studio SDK. It discovers the selected backend's `elsa.api.decisions` v1 `decisions` relation and uses the authenticated host HTTP transport for every operation, including XML downloads. Its own backend, tenant and subject identity bound all mounted state.

The module supplies one table editor for the [supported DMN profile](../../../../specs/1195-business-decisions/contracts/dmn-profile.md), saved cases, exact-version evaluation, actual rule explanations and protected completed-test history. Numeric form values remain decimal strings within typed tags; browser Number coercion is never used. Server validation and permission checks remain authoritative.

Imported documents retain their original XML. Table edits change only the owned source nodes and preserve surrounding diagram/opaque extension material; unpreservable edits refuse. Unsupported documents remain downloadable without being represented as executable. Input renames/types update matching retained input-data variables, and output shape/type changes keep the optional decision variable consistent. Deleting an input backed by a retained input-data declaration requires a DMN modeler so references and diagrams can be removed together; Studio explicitly refuses that mutation and preserves the document. New columns receive unused symbols.

Workflow authors select a policy and either an exact published version or latest-at-workflow-publication. The property editor writes `assetId@versionId` or `assetId@latest`; the workflow publication adapter owns immutable resolution and execution protection.

Build and verify from the repository root:

```sh
pnpm --dir Studio --filter @elsa-workflows/studio-decisions build
pnpm --dir Studio --filter @elsa-workflows/studio-decisions typecheck
pnpm --dir Studio --filter @elsa-workflows/studio-decisions test
```

The [UX acceptance record](UX-ACCEPTANCE.md) defines the user goal, references, composed-host evidence and remaining review gates. Unit/browser fixtures do not qualify the normal product journey.

## Cross-domain contributions

- Studio SDK `featureAreas`: Decisions resource navigation/routes and backend capability requirements.
- Studio SDK `propertyEditors`: `decision-version` picker for an activity's declared input metadata.
- API capabilities: consumes `elsa.api.decisions` v1; no Workflows implementation reference or host-specific route switch.
