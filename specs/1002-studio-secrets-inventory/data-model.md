# Data model

No persistence/schema change.

| Concept | Fields/relationship | Rules |
|---|---|---|
| Completed inventory observation | query/context owner, metadata items, authoritative total | Accept only current request; coverage refers to this query, never draft input |
| Displayed subset | current local facets over accepted items | Displayed≤loaded; local zero does not prove a globally empty inventory |
| Coverage | displayed count, loaded count, server search total | Partial only when total>loaded; total does not include local facet filtering |
| Scope options | unique scopes from loaded metadata | Label bounded coverage; descriptors are advisory and distinct |
| Detail selection | exact name in accepted items | Existing dialog; close preserves list criteria; no value fetch introduced |
| Attempted query | latest request's exact query within its context | Read-failure Retry repeats it; accepted-query refresh and explicit draft submission stay distinct |

A scope selected from an earlier loaded result remains an applied criterion if a later response lacks it. Its qualified retained option describes selection state, not current inventory membership or a complete catalog.

Load transitions: initial/pending→accepted ready or failure; superseded query/context results do not commit. Loading/failure must not masquerade as complete0. Existing descriptor request and operation-specific mutation state remain their own concerns.
