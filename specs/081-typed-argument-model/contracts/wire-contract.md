# Wire Contract (Phase 1 → Phase 2)

The JSON shapes the Studio (Phase 2) consumes. This is the FR-019 / SC-007 deliverable: Phase 2 can be built against this document without reading backend source. All property names are camelCase (the configured `IPayloadSerializer` policy).

## 1. Argument type reference

The type of an authored **Variable / Input / Output** is carried as a `type` object:

```json
{
  "alias": "String",
  "collectionKind": "Single"
}
```

- `alias` *(string, required)* — stable element-type identifier. Bare for framework primitives (`String`, `Int32`, `Boolean`, `DateTime`, `Guid`, `Object`, …); dotted/reverse-DNS for module types (`Elsa.Http.HttpRequest`). Never a namespace/assembly/version.
- `collectionKind` *(string enum, required)* — one of `Single` | `Array` | `List` | `HashSet`. Absent ⇒ treat as `Single`.

There is **no** `isArray`, `typeName`, `namespace`, `assemblyName`, or `assemblyVersion` for authored argument types. (`isArray` is removed; collection-ness is `collectionKind`.)

### Storage driver reference

`storageDriverType` is a **bare alias string** (or `null`), not an object:

```json
"storageDriverType": "Elsa.Memory.MemoryStorageDriver"
```

### Example — Variable

```json
{
  "referenceKey": "var-1",
  "name": "Items",
  "type": { "alias": "String", "collectionKind": "List" },
  "storageDriverType": null,
  "default": { "value": null, "expressionType": null }
}
```

### Example — Input / Output

```json
{
  "referenceKey": "in-1",
  "name": "Tags",
  "type": { "alias": "String", "collectionKind": "HashSet" },
  "storageDriverType": null,
  "displayName": "Tags",
  "category": null,
  "isRequired": false
}
```

(Input/Output retain their other existing fields; only `type` and `storageDriverType` changed shape.)

### Unknown alias

If a definition references an alias the current backend can't resolve, the value **round-trips unchanged** — the `alias` string is preserved on save. The Studio should render it as a disabled/unknown selection rather than dropping it.

## 2. Descriptors endpoint

`GET /_elsa/workflow-management/descriptors/variables`

Returns the aggregated, module-contributed catalog of **selectable** argument element types. (Exact route group confirmed in `/speckit.tasks`; the Studio already expects this path.)

```json
{
  "descriptors": [
    { "alias": "String",  "displayName": "String",  "category": "Primitives", "defaultEditor": "text" },
    { "alias": "Int32",   "displayName": "Integer",  "category": "Primitives", "defaultEditor": "number" },
    { "alias": "Boolean", "displayName": "Boolean",  "category": "Primitives", "defaultEditor": "checkbox" },
    { "alias": "DateTime","displayName": "Date/Time","category": "Primitives", "defaultEditor": "date" },
    { "alias": "Elsa.Http.HttpRequest", "displayName": "HTTP Request", "category": "Http", "defaultEditor": "none" }
  ]
}
```

- `alias` *(string)* — matches the `type.alias` value persisted on definitions; the join key between picker and stored type.
- `displayName` *(string)* — picker label.
- `category` *(string)* — grouping key; the Studio groups options by this.
- `defaultEditor` *(string)* — open hint for which default-value editor to render. Known starter values: `text`, `number`, `checkbox`, `date`, `none`. The set may grow; the Studio must tolerate unknown values (fall back to `text` or read-only).

The collection-kind choice (`Single`/`Array`/`List`/`HashSet`) is **not** part of the descriptor — it is an orthogonal selection the Studio renders as a second dropdown, applied to any selected alias.

## Contract guarantees

- The set of `collectionKind` values is closed for this feature: `Single | Array | List | HashSet`.
- `alias` is a frozen identifier: the backend may rename the CLR type behind it, but the alias is stable, so persisted Studio documents remain valid.
- Every `alias` returned by the descriptors endpoint is resolvable by the backend at the time it is served.
