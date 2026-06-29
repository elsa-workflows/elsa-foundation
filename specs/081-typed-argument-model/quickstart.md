# Quickstart: Verifying the Typed Argument Model

How to confirm the feature works end to end once implemented. All commands run from the worktree root.

## Build & test

```bash
dotnet build
dotnet test tests/Elsa/Expressions/Tests
dotnet test tests/Elsa/Activities/Design/Tests
dotnet test tests/Elsa/Serialization/Tests
```

Expected: new tests pass — `VariableMapperTests` (12 alias×kind combinations + unknown alias), `WellKnownTypeRegistryTests` (duplicate throw, reserved-namespace throw, resolve), `VariableTypeDescriptorCatalogTests` (aggregation + grouping), `TypeJsonConverterTests` (HashSet parity), and feature registration tests (new services resolve).

## Manual verification

### 1. Round-trip a typed, collection-aware argument (US1 / SC-001, SC-002)

Serialize a `VariableDefinition` with `type = { alias: "String", collectionKind: "List" }`, deserialize, and assert:
- the round-tripped JSON contains only `alias` + `collectionKind` (grep the JSON: **no** `typeName`/`namespace`/`assemblyName`/`assemblyVersion`);
- `VariableMapper.Map` yields a `Variable<List<string>>`;
- repeat for `Single`/`Array`/`HashSet` and for an Input and an Output → all retain `collectionKind`.

### 2. Descriptors endpoint (US2 / SC-006)

```bash
curl -s http://localhost:<port>/_elsa/workflow-management/descriptors/variables | jq .
```

Expected: a `descriptors` array; framework primitives present (`String`, `Int32`, `Boolean`, `DateTime`, …) each with `displayName`, `category`, `defaultEditor`; any module-contributed dotted aliases (e.g. `Elsa.Http.HttpRequest`) also present; entries groupable by `category`.

### 3. Rename-proofness (US3 / SC-003)

Rename the CLR type behind an alias (alias unchanged) and reload a definition saved against that alias → resolution still succeeds (0 failures).

### 4. Fail-fast registration (US3 / SC-004)

Register the same alias twice (or a module registering a bare alias) → application startup throws `DuplicateTypeAliasException` / `ReservedAliasNamespaceException`. The app refuses to start.

### 5. Graceful unknown alias (US3 / SC-005)

Load a definition referencing an unregistered alias → load succeeds (no throw); save it again and confirm the original `alias` string is preserved verbatim.

## Done criteria

All seven Success Criteria in [spec.md](spec.md) demonstrable; extension-point catalog, glossary, and generated maps updated (see plan follow-through); wire contract in [contracts/wire-contract.md](contracts/wire-contract.md) matches the actual emitted JSON.
