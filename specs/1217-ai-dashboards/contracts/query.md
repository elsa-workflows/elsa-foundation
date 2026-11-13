# Query and module contribution contract

**Status:** Query requirements and design rationale for the implementation under review in #3913. The source and public API files define the implemented names; proposed future seams below are not runtime contracts. Native, live-provider and composed Studio qualification remain separate evidence. [The specification](../spec.md) governs product requirements; [the data inventory](../research-data.md) records existing source limitations.

## Ownership and dispatch

The proposed generic contract belongs to Dashboard. Dataset identity, fields, metrics, relationships, eligibility, authorization and source reads remain with the participating module. The Workflows adapter may compose authorized Runtime and Design read contracts; neither Runtime nor Design acquires a dependency on Dashboard to execute its own domain behavior.

Declaration/discovery and execution are independent contracts, following framework constitution §2.6.4. Metadata contains no provider callbacks, `IQueryable`, executable expression trees, database connection details or concrete persistence types. Shared shape records may describe dataset references, fields, query definitions and result values. They do not merge metadata collection with a source execution interface.

Each proposed event is a named Sequential event in the owning `.Core`. It has a directly accessible, get-only output collection initialized to `[]`. Exactly one action-named aggregating handler injects the corresponding `IEnumerable<T>` extension contract and writes that collection; independent modules register extensions rather than additional aggregating handlers. Handler ordering never determines ownership or result meaning.

| Named event | Single aggregating handler | Module extension | Purpose |
|---|---|---|---|
| `DashboardDatasetsCollecting` | `CollectDashboardDatasets` | `IDashboardDatasetExtension` | Collect only metadata the current viewer may discover, with current module availability. No result query is executed. |
| `DashboardQueryValidating` | `ValidateDashboardQuery` | `IDashboardQueryValidationExtension` | After generic structural validation, obtain the owning module's semantic/access/compatibility decision. It does not execute the result query or save a resource. |
| `DashboardQueryExecuting` | `ExecuteDashboardQuery` | `IDashboardQueryExecutionExtension` | Execute one validated query through the one current owner, under trusted request scope and bounded reads. |
| `DashboardQueryDrilling` | `DrillDashboardQuery` | `IDashboardQueryDrillExtension` | Revalidate and obtain a bounded authorized source page for a selected result group/period. |

Extensions receive typed collection/validation/execution contexts and act on them. They do not expose an anonymous mediator or interpret arbitrary dataset identifiers in shared assistant code. The three query operations require exactly one owner result; zero, duplicate or conflicting results fail closed. Discovery aggregates multiple dataset declarations and rejects duplicate stable dataset identities.

Registration supplies immutable module/dataset ownership keys independently of viewer-visible metadata. Composition validation checks duplicate owner registrations before a denied discovery result could hide the collision. Each request binds one installed-module snapshot; a module unload/replacement invalidates that snapshot and cancels/refuses completion. No last-registration-wins or partial success from ambiguous owners is allowed. Exact registration mechanics remain an implementation-plan review item.

## Dataset declaration and authorized discovery

A proposed `DashboardDatasetDeclaration` contains:

| Member | Meaning |
|---|---|
| `datasetId`, `ownerModuleId` | Stable namespaced identities. Display names never identify a dataset or route execution. |
| `schemaRevision` | Opaque exact revision of queryable structural meaning. The module declares whether a prior reference is compatible; compatibility cannot silently change field or metric meaning. |
| `title`, `description`, `availability` | Understandable module-owned description and an actionable current availability state. Discovery failure is different from an empty authorized catalog. |
| `fields` | Stable ID, scalar type, nullability, label, units, available filter/group/sort/projection operations, sensitivity classification and owning access requirement. |
| `businessDimensions` | Explicit references to declared fields with capture/update semantics. Their presence never follows from an arbitrary workflow variable or metadata key. |
| `metrics` | Stable ID/revision; required inputs; allowed grouping/filtering; units, eligibility, null/denominator rules, precision/display rules and supported comparison behavior. |
| `relationships` | Named module-owned enrichment with target ownership, cardinality, keys, availability and authorization semantics. First profile supports only declared enrichments, not arbitrary joins. |
| `time` | Supported time fields, their cohort meaning, timezone/range/bucket support and relative-window operations. |
| `coverage` | Which collection/retention facts this module can attest. No earliest-row heuristic establishes completeness. |
| `operations` | Supported record/aggregate query forms, primitive aggregates, drill/export capabilities and lower module-specific limits. |

Discovery is scoped to trusted current subject/tenant/host and effective authorization. Clients, saved definitions and model input cannot supply that scope. Sensitive field names, allowed values, labels, relationship metadata and metric presence require their own permitted disclosure. An inaccessible dataset must not reveal its existence through a differently detailed public error.

The source may declare a normal list/chart/table/metric visualization profile. Generic renderers consume bounded typed results; any specialized renderer/drill contribution remains with its module and must use the same admission rules. There is no model-generated component code.

## Validated question grammar

The first-profile proposal is one dataset per question, with at most two explicitly declared enrichments. A question has schema version `1`, an exact dataset/schema reference, a record or aggregate form, typed parameters, source filters, projections/grouping, metrics, an optional time comparison, post-aggregate predicates, deterministic sorting and result bounds. Unsupported members fail validation; they are not ignored.

| Part | Proposed supported shape |
|---|---|
| Parameter | Stable name, scalar type, required/nullable flag and optional bounded default. An override must match the declared type and allowed range/list. |
| Value | A typed literal or parameter reference; scalar types are string, boolean, signed integer, decimal, instant and local date. Null is explicit and type-checked. Decimal wire values are invariant strings to avoid browser floating-point loss. |
| Source filter | A bounded `all`/`any` tree of field predicates. Operators: `eq`, `ne`, `gt`, `gte`, `lt`, `lte`, `in`, `notIn`, `contains`, `startsWith`, `isNull`, `isNotNull`, only where the field declares them. No raw expression, regex or computed field. |
| Record projection | Declared permitted scalar field IDs; no unrestricted “all fields.” |
| Group | Declared groupable field ID, or a declared time field with an allowed hour/day bucket. Null grouping and missing-value policy are explicit. |
| Metric | Exact module metric ID/revision, or a declared primitive aggregate over a permitted numeric field. Initial primitive candidates are `count`, `sum`, `average`, `min`, `max`; modules may admit a smaller set. `countDistinct` and arbitrary formulas are outside this profile. |
| Time | Absolute `[from,to)` instants, or a declared relative window resolved once per request with explicit timezone/week-start/time field. Both requested and effective windows appear in the result. |
| Comparison | One compatible baseline window; output references `current.<metric>`, `baseline.<metric>` and module-supported `delta.<metric>`. Baseline/current metrics use the same authorized query meaning. |
| Having | Bounded predicates over permitted current/baseline/delta metric outputs, using numeric/null operators only. Runs after authorized aggregation/comparison and before sorting/limiting. No access to hidden records or arbitrary arithmetic. |
| Sort | Declared projected/group/metric references, ascending/descending with an explicit null policy. A stable canonical key is appended as the final tie-breaker. |
| Limit | A validated output bound. Aggregate top-N follows complete authorized aggregation within budget; it is not a source sample. |

The same grammar powers conversation and ordinary editors. Native controls can change every supported field, filter, grouping, metric/aggregate, sort, window/comparison, parameter and presentation choice without a model call. The module validates combinations as well as individual members; syntactically valid unsupported combinations remain unavailable.

A current metric may use its plain alias or `current.<alias>` in visualization bindings. Use one spelling consistently across that visualization's X and Y columns; combining both spellings for the same metric is rejected before saving or execution. Baseline and delta remain distinct outputs.

For example, this illustrative definition asks for individual workflows with an increased failure rate and includes their current folder for presentation. Names/revisions below are proposed, not existing dataset identifiers:

```json
{
  "schemaVersion": 1,
  "dataset": {
    "id": "elsa.workflows.runs",
    "schemaRevision": "runs-v1"
  },
  "form": "aggregate",
  "parameters": [],
  "filters": {
    "all": [
      { "field": "runKind", "operator": "ne", "value": { "type": "string", "literal": "testRun" } }
    ]
  },
  "groupBy": ["definitionId", "currentFolderId"],
  "metrics": [
    { "alias": "failureRate", "metric": "workflow.failure-rate", "revision": "started-current-status-v1" }
  ],
  "time": {
    "field": "startedAt",
    "window": "calendarWeekToDate",
    "timeZone": "Europe/Amsterdam",
    "weekStart": "monday"
  },
  "comparison": { "mode": "previousCalendarWeekSameElapsed" },
  "having": {
    "all": [
      { "metric": "delta.failureRate", "operator": "gt", "value": { "type": "decimal", "literal": "0" } }
    ]
  },
  "sort": [{ "reference": "delta.failureRate", "direction": "descending", "nulls": "last" }],
  "limit": 50
}
```

Grouping only by folder asks a different aggregate question: a folder's rate is calculated from its eligible runs, not an average of workflow percentages or the rates of an implicitly preselected set of workflows. Refinement must explain/confirm that change. To retain workflow selection while presenting by folder, use both declared grouping keys as above. A genuinely required two-stage cohort query outside this grammar is unsupported rather than approximated.

## Recommended initial limits

These are reviewable proposed defaults, not measured performance claims. Hosts/modules may lower limits; a request, model or saved definition cannot raise them.

| Limit | Proposed default |
|---|---|
| Serialized question | 64 KiB UTF-8; unknown properties rejected |
| Dataset/enrichment count | One dataset; at most two declared enrichments |
| Parameters/literals | 12 parameters; strings 256 Unicode scalar values; `in` list 100 entries |
| Predicate complexity | 20 source leaves and 10 having leaves, each tree depth at most 3 |
| Projection/group/metric/sort count | 12 projected fields, 2 group keys, 4 metrics, 3 explicit sort keys |
| Time | At most two periods, each at most 93 elapsed days; at most 744 total time buckets |
| Source budget | 100,000 examined rows across both periods and enrichments combined, including candidates rejected by authorization |
| Aggregation/output | At most 200 groups; default aggregate output 50, maximum 200; raw/drill page default 25, maximum 100 |
| Execution | 10-second cooperative deadline; no partial successful aggregate on deadline/row/group exhaustion |
| Refresh concurrency | At most two active query reads per viewer/tenant; host global limit also applies; explicit cancellation/supersession |

Bound validation occurs before data reads where possible. Providers enforce source/deadline budgets during traversal. Counting rejected candidates is internal cost accounting: public diagnostics never disclose unauthorized row totals, identities or group cardinality. A budget failure returns a generic actionable restriction and no misleading aggregate. If authorization cannot be evaluated within the bound, refuse rather than aggregate unchecked rows. A bounded page can intentionally contain fewer displayed rows, but its total/group calculation must still be complete within the advertised query/coverage scope.

## Workflow metric interpretation proposal

The Workflows module remains authoritative. This proposal preserves the existing widget while defining a separate new question profile whose interpretation is visible and versioned.

- **Failure rate:** current `Faulted` outcomes divided by all eligible runs **started** in `[from,to)`, expressed as a percentage. The numerator/denominator are returned only when authorized; incomplete runs remain in the denominator. This does not represent historical status-at-instant or failure events occurring in that period.
- **No denominator:** a null rate with reason `noEligibleDenominator`, not an observed zero. A valid nonzero denominator with no failures is zero. This new profile does not silently alter the old Home widget's existing zero-denominator behavior.
- **Comparison:** compare rates for the same declared grouping/eligibility. Display delta in percentage points; do not label it relative percent growth. A baseline/current missing denominator yields no delta and cannot satisfy `delta > 0`. Ratio comparisons use exact numerator/denominator arithmetic before display rounding, so a small real increase is not lost because both displayed rates round to the same value.
- **Week defaults:** recommended Monday local-calendar week, with explicit saved timezone/week start. `previousCalendarWeekSameElapsed` resolves current start/now once, takes the elapsed instant duration since that start and applies that duration from the prior local-week start. If that would extend beyond the previous week because of daylight-saving length differences, require an explicit different comparison rather than silently clipping or changing meaning. Explicit absolute periods are always shown as resolved instants.
- **Run kind:** excluding `testRun` does not classify `unknown` as production. Preserve `unknown`, `publishedRun` and `backgroundWeaverRun` as separate declared values and show the effective eligibility. A strict published-only question uses an explicit supported filter.
- **Duration:** the module declares valid start/completion eligibility and included terminal outcomes. Proposed processing duration is elapsed milliseconds for valid pairs, with missing/inverted timestamps excluded and an authorized omitted count/reason. An average uses eligible durations, not all runs; no eligible durations yields null. No implicit variable scanning provides business timing.
- **Current folder:** Design's current organization is a declared enrichment, evaluated under current Design read authority. Missing/removed/unauthorized folder attribution is distinct from a genuinely unfiled definition. No historical-folder label is claimed. Runtime-only hosts cannot execute a question requiring this unavailable enrichment.
- **History coverage:** retained states answer questions about retained eligible records. Until the owning collector can attest coverage, return `unknown`; do not infer complete history from the oldest retained row or return a historical completeness claim.

The existing history store's date filter uses effective update/completion/start/create time and an inclusive upper bound. It must not be reused as equivalent to the proposed started cohort. A correct adapter needs an owning reader/projection that implements the declared semantics and bounded traversal; any required Runtime read-port change is a reviewed domain change.

## Authorization seam and evaluation order

The record-inspection contract `IActivityInspectionContextAsync` is now in the Runtime-owned `Elsa.Workflows.Runtime.Inspection.Core` package. The Dashboard satellite references that contract without copying Runtime API permission checks. The lift preserves request-scoped fail-closed semantics, separate structure/value/payload grants, canonical permission evaluation, trusted subject/tenant and cancellation. The existing HTTP adapter remains the semantic owner, and existing inspection/denial tests must continue to prove the same behavior. No allow-all development adapter becomes a production fallback.

Design already exposes `IWorkflowDefinitionAuthorizer.CanReadAsync` in `Elsa.Workflows.Design.Core`; its documented scope includes the owning organization. Current-folder enrichment can combine this with `IWorkflowLibraryStore` through owning `.Core` contracts. A known denial/unavailable resource is not an infrastructure success; an authorization infrastructure failure remains a retriable failure wrapped in the owning domain exception. No Runtime → Design reference is introduced.

For every execution, the proposed pipeline is:

1. Bind trusted subject, tenant, host, definition/question revision, installed-module snapshot and one evaluation instant. Verify current definition access; never accept an author principal from the saved resource.
2. Validate structural shape, parameters, budget and exact source/schema/metric references. Recheck discovery/semantic availability without exposing inaccessible metadata.
3. Authorize every used field/metric/relationship, including those used only in filters, grouping, having or sorting. Verify aggregate visibility separately from source-record grants. Any forbidden input fails the question rather than being omitted while preserving its influence.
4. Traverse bounded candidates through normal module/domain reads. Enforce record and required value authority before those rows/values enter counts, sums, groups, comparisons, sort keys or pagination. Apply any declared enrichment only under both owning access decisions.
5. Compute complete authorized results inside the budget, apply having, stable order and output limits, then return authorized provenance/coverage/drill descriptors. Recheck access before a deferred read or disclosure; do not use a previous successful result as current authority.

Permission changes use the identity provider's declared propagation boundary. Dashboard ACL changes apply to the next operation. The module must not claim immediate IAM revocation merely because normalized token claims were evaluated again; live IAM rehydration and token-refresh-bound grants remain separately tested configurations.

There is no server result cache in this initial proposal, and no refresh path invokes a model. Results are transient viewer-scoped responses. Client content clears on subject/tenant/access changes and becomes outdated after a definition edit. Any future cache requires a separate reviewed partition/invalidation design, including current record/field/aggregate policy identity; a general inspection permission hash is insufficient.

## Results, coverage and failures

A proposed `DashboardQueryResult` binds exact query/question revision, dataset/schema/metric references, request/evaluation identity, generated time, resolved periods, admitted parameters/filters, authoritative interpretation and bounded typed columns/rows/metric cells. It records current effective source scope in a form safe to disclose; it never returns credentials, author access snapshots or unchecked navigation URLs.

| Dimension | Proposed values and meaning |
|---|---|
| Execution outcome | `succeeded`, `unavailable`, `incompatible`, `denied`, `limitExceeded`, `cancelled`, `failed`. Public unavailable/denied detail must preserve existence boundaries. Infrastructure failure is not a successful empty result. |
| Coverage | `complete`, `incomplete`, `unknown`, with module-attested intervals/gaps and explanation. Missing retention/collection evidence means unknown. |
| Row/metric meaning | Successful empty set; observed numeric zero; null with `noEligibleDenominator`/`noEligibleValues`/other declared reason. These do not share one zero/default value. |
| Refresh presentation | Client state tracks refreshing, successful, failed/cancelled refresh and a stale prior success. Stale content retains its last successful timestamp only while current access remains admissible. |

An incomplete/unknown source may return accurate metrics over its disclosed retained scope, visibly qualified; it may not extrapolate or claim the full requested population. Source truncation caused by execution budget is `limitExceeded`, not ordinary incomplete historical coverage with a successful aggregate.

Schema/metric revisions are persisted with the question. Missing owner, module removal, lost field/enrichment, duplicate ownership or incompatible revision blocks execution with a repair/copy path. A module may explicitly attest compatible structural evolution without changing the saved question's meaning; no automatic fallback to a different source/metric or execution of a newly interpreted definition is allowed. Provider removal affects conversational authoring only.

## Drill and optional export

A result group carries a typed module-owned drill descriptor bound to the viewer/tenant, question/dataset revisions, exact resolved current/baseline period and canonical group constraints. It is a read request descriptor, not an access token or saved author result. The caller selects which period to inspect when a comparison is shown.

Drill rechecks definition/source access and compatibility, reapplies the original question meaning plus selected constraints and returns bounded authorized records through module-owned navigation. An aggregate having/comparison condition is revalidated against fresh authorized data before its drill is offered; a changed result is identified rather than pretending the previous aggregate is a transactional snapshot. The protocol does not promise a historical snapshot or source-data lock. Cursors are opaque, integrity protected and bound to current scope/query/revisions/period/group; a scope or question change invalidates them.

Exports are unavailable unless the source declares a separately bounded implementation with the same current record/field/aggregate authorization. No browser export of privileged author results, unbounded “all records” fallback or undeclared spreadsheet/script query is implied.

## Result disclosure and Weaver context

The implemented authoring profile sends currently admitted dataset declarations and declarative definitions/proposals to Weaver. It sends no query rows, computed aggregates or result previews to the provider. The preview tool validates a proposal; Studio executes and renders its protected preview through the native query API. Retained proposals with a non-null preview are rejected. See the [implemented Weaver contract](weaver.md#provider-disclosure-profile).

A future feature that sends protected result facts to a model would require a reviewed original-evidence provenance and readmission contract. Fresh execution of a narrower query cannot authorize older broader values already retained in a transcript. Evidence handles, a readmission event and an extension interface remain future design ideas, not implemented extension points or current acceptance claims.

## Independent contribution witness

The separately packaged reference module is **`Acme.Processing`**. It owns ordinary processing records, normal record creation/read capability, record/field authorization, explicit `customerCategory` exposure and a versioned elapsed-processing metric. Its reference business-record store is in-memory and ephemeral; saved question/dashboard definitions use durable resource storage. This witness does not claim business-record durability. It contributes through the same discovery, semantic validation, execution, rendering/drill and evidence rules; shared assistant/query orchestration contains no Acme branch.

An illustrative numeric witness uses tenant A records `a1: retail, 60 seconds`, `a2: retail, 180 seconds`, `a3: enterprise, 300 seconds`, plus tenant B record `b1: retail, 900 seconds`. Tenant A's fully authorized viewer obtains retail mean 120 seconds/count 2 and enterprise mean 300 seconds/count 1. A viewer allowed only `a1` and `a3` obtains retail mean 60/count 1 and enterprise mean 300/count 1. Tenant B's record never contributes. A viewer lacking category-field or grouped-aggregate permission receives an unavailable/denied question, not a grouped answer influenced by the protected category.

The witness creates records through Acme's normal capability, calculates expected values independently, drills to the admitted IDs, persists/reopens the novel question and repeats refresh without any provider invocation. It also removes/changes the module/schema, tests missing duration and unknown coverage, and shows actionable recovery. A separate Workflows witness exercises actual retained Elsa executions, both comparison periods, explicit test exclusion, unknown-kind interpretation and authorized current-folder enrichment. Reference business data, deterministic provider fixtures and live-provider qualification remain distinct evidence.
