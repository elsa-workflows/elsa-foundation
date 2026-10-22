# Data model

No persistence/schema change.

| Concept | Fields/relationship | Rules |
|---|---|---|
| Completed inventory observation | query/context owner, metadata items, authoritative total | Accept only current request; coverage refers to this query, never draft input |
| Displayed subset | current local facets over accepted items | Displayed≤loaded; local zero does not prove a globally empty inventory |
| Coverage | displayed count, loaded count, server search total | Partial only when total>loaded; total does not include local facet filtering |
| Scope options | unique scopes from loaded metadata | Label bounded coverage; descriptors are advisory and distinct |
| Detail selection | exact name in accepted items | Existing dialog; close preserves list criteria; no value fetch introduced |

Load transitions: initial/pending→accepted ready or failure; superseded query/context results do not commit. Loading/failure must not masquerade as complete0. Existing descriptor request and operation-specific mutation state remain their own concerns.
