# Groundwork closed-query capability spec (handoff to `valence-works/groundwork`)

Status: draft handoff. Audience: Groundwork maintainers.
Related: [Groundwork host-configurable persistence feasibility report](groundwork-host-configurable-persistence-feasibility.md) ·
[Groundwork Persistence Readiness program goal](../program-goals/groundwork-persistence-readiness.md)

## Why this exists

Elsa Foundation now expresses **all** of its design-lane reads (workflow/activity definitions and
versions, drafts, layouts) through a single **closed, provider-neutral query contract** —
`Query<TEntity>` in `Elsa.Persistence.Core/Queries` — surfaced behind small named read ports
(`IWorkflowDefinitionStore`, `IWorkflowDefinitionVersionStore`, `IActivityDefinitionStore`, …). The
old `IQueryable`/LINQ surface (`IQueries<T>` / `IFilter<T>`) has been **deleted**.

EF Core already backs that contract (via `EFCoreQueryTranslator` + `EFCoreReadStore<,>`). For a host
to select **one** provider that backs **every** module — runtime *and* design — Groundwork must be
able to honour the same closed contract. This document specifies the **exact, bounded** portable-query
capability Groundwork needs to do so.

> This is **not** a request to turn Groundwork into a general-purpose ORM or to support `IQueryable`.
> The contract below is the complete surface; do not grow it without a corresponding change in the
> Elsa closed-query model.

## The closed contract Groundwork must satisfy

A query is an **`AND` of `OR`-groups** of single-field comparisons, plus **at most one** ordering,
plus a tenant-agnostic flag. Formally:

```
query        := clause ( AND clause )*           // zero clauses ⇒ match all
clause       := comparison ( OR comparison )*     // disjunction within a clause
comparison   := field  op  value
op           := Equal | In | Contains
order        := ( OrderBy | OrderByDescending ) field   // optional, single field
tenantScope  := TenantAware (default) | TenantAgnostic
```

Fields are **direct member selectors** only (e.g. `x => x.Name`) — no navigation, no computed
expressions. Providers may address the field by simple member name (`QueryComparison.FieldName`).

### Operator semantics (must match exactly)

| Operator   | Meaning | Value shape | Required semantics |
|------------|---------|-------------|--------------------|
| `Equal`    | field == value | scalar (string, etc.) | Exact equality. `null` value ⇒ matches rows whose field is null. |
| `In`       | field ∈ values (SQL `IN`) | `IEnumerable<TField>` | Set membership over the field's own type. Empty set ⇒ matches nothing. |
| `Contains` | substring match (SQL `LIKE '%v%'`) | `string` | **Case-insensitive** substring. A **null field yields no match** (must not throw). |

`Contains` case-insensitivity is mandatory and is the search-term behaviour Elsa relies on; the EF
translator implements it as `string.Contains(value, StringComparison.CurrentCultureIgnoreCase)` with a
null guard. A document/key-value provider must produce the equivalent result set.

### Composition semantics

- Multiple clauses are combined with **`AND`**.
- Comparisons inside a clause are combined with **`OR`**.
- A clause may legitimately be the constant-false "no match" sentinel (Elsa emits one when a search
  term cannot match); the provider must return an empty set for it without error.

### Ordering

- **Single field**, ascending or descending. No multi-key ordering is required.
- The canonical use is latest-version resolution by a **precomputed `SemVerSortKey` string field** —
  so the provider needs **no SemVer knowledge**; it orders a plain string column/field. Order must be a
  stable, total ordinal/lexicographic sort consistent with string comparison.

### Tenant scoping

- Queries are tenant-aware by default. When a query is marked **tenant-agnostic**, the provider must
  **bypass ambient tenant filtering** for that query (the EF adapter calls `IgnoreQueryFilters()`).
  Groundwork's equivalent must expose a per-query opt-out of any ambient tenant predicate.

## Execution operations the read ports require

Behind the named ports, the relational base (`EFCoreReadStore<,>`) exposes exactly three executors.
Groundwork's adapter must offer equivalents:

| Operation | Returns | Notes |
|-----------|---------|-------|
| `QueryAsync(query)`          | all matching entities | with optional related-entity load (see below) |
| `FirstOrDefaultAsync(query)` | first match or `null`  | honours ordering when present |
| `AnyAsync(query)`            | `bool`                 | existence check |

### Paging + total count (near-term, not yet on the ports)

The design lanes historically listed with **offset paging + total count** (`PageArgs → Page<T>`). The
current named ports list-all, but the universal-provider target reintroduces server-side paging. To be
future-proof, Groundwork's portable query should support:

- **Offset paging**: skip + take.
- **Total count** alongside the page (count of the full predicate result, independent of the page
  window).

### Related-entity loads (NOT a join requirement)

Where a caller needs a related aggregate (e.g. a version plus its definition), Elsa models this as an
**explicit second read** against the other aggregate's port — never as a relational join. Groundwork
therefore does **not** need joins or `Include`; the relational adapter uses `Include` purely as an
optimization.

## Minimal capability uplift — the bounded ask

Groundwork's portable query today supports **`Equal` + offset paging**. To back the closed contract it
must additionally support:

1. **`In` / set-membership** over a field's own type (empty set ⇒ no match).
2. **`Contains` / substring** match, **case-insensitive**, null-field-safe.
3. **`OR`-composition** of comparisons within a clause (and `AND` across clauses).
4. **Single-field `ORDER BY`**, ascending/descending, over a plain (string) field.
5. **Total count** alongside **offset paging**.

Explicitly **out of scope** (do not implement for this contract):

- `IQueryable` / arbitrary expression trees.
- Joins / `Include` / navigation-property predicates.
- Multi-field ordering, grouping, projection, aggregates beyond count.
- SemVer or any Elsa domain semantics (sort keys are precomputed strings).

## Adapter strategy on the Elsa side (no Groundwork changes blocked)

The Elsa Groundwork design adapter implements the named read ports by translating `Query<TEntity>`:

- to Groundwork **native portable query** for the operators Groundwork supports, and
- via a small **in-adapter fallback** (declared-index fetch + in-memory predicate using the same
  operator semantics above) for operators not yet native.

This lets the Elsa-side refactor and single-provider host composition proceed **now**, with the adapter
shedding fallback paths as Groundwork ships each capability above. The fallback's in-memory predicate
mirrors `EFCoreQueryTranslator` exactly (null-guarded `Contains`, case-insensitive), so results match
the relational provider.

## Acceptance signals

Groundwork can be declared "closed-query capable" when, for a representative aggregate:

- All five uplift capabilities execute **server-side** (no adapter fallback) for the design-lane query
  shapes inventoried in the feasibility report.
- `Equal`/`In`/`Contains` produce result sets identical to the EF Core provider for the same data,
  including null-field and empty-set edge cases.
- Tenant-agnostic queries bypass ambient tenant filtering.
- Offset paging returns a correct page window plus a correct total count.
