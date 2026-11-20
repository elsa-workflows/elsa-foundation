# Data model: JSON data integrity baseline

No stored schema changes. These are the in-code entities the plan introduces or relies on.

| Entity | Where | Fields / shape | Rules |
|---|---|---|---|
| **Baseline** | `Elsa.Serialization.Core` `JsonBaseline.Options` | `PropertyNamingPolicy = CamelCase`, `DictionaryKeyPolicy = null`, `NumberHandling = Strict`, `DefaultIgnoreCondition = Never`, `PropertyNameCaseInsensitive = false`, `Encoder = UnsafeRelaxedJsonEscaping`, converters: `RawJsonElementConverter` | Read-only (`MakeReadOnly()`). Never handed out mutable. |
| **Baseline copy** | `JsonBaseline.CreateCopy()` | A mutable copy of the baseline | May add resolvers and converters for own types. A protected setting changed on a copy is a deviation. |
| **Conversion contract** | `JsonConversionContract` (default and scoped, per user type or conversion boundary); scoped ones from `[JsonConversion]` on the type or input member | Naming policy, exact or case-insensitive matching, member mappings, unmatched-member handling (warn or strict), date/time interpretation (none, source time zone, or explicit assume-UTC); a baseline copy with those settings and the protected settings unchanged | The options a data-to-.NET conversion uses for its target, in both directions (FR-004, FR-004a, FR-012, FR-016). Part of the type's code; not stamped into executables. The default is camelCase, exact, warn, no date/time interpretation. |
| **Canonical form** | `JsonCanonicalForm` | Members by unescaped name in any order, strings unescaped, numbers by exact decimal value, arrays in order, missing ≠ `null` | Value equality (FR-021a) and the ADR 0092 value digest. Never applied to an existing hash input; never written to the wire or a store. |
| **Unmatched-member warning** | `UnmatchedMemberScanner` | `code`, `path` (JSON path of the key), `targetType` | One per unmatched key, judged against the effective contract. It fails a step only under a strict scoped contract. |
| **Deviation register entry** | `docs/serialization.md`, "Baseline deviations" table | `File`, `Setting`, `Reason`, `Approved by` (ADR or issue) | The guard parses the table. A data-touching API deviation must cite an ADR. |
| **Legacy list** (option-less call ratchet) | `Essentials/tests/Architecture/Tests/Baselines/json-optionless-calls.txt` | One `path:symbol` per line | It may only shrink. Workflow-data paths may not appear in it. It is not the register. |
| **Durable format** | `[EfSchemaFamily]` `SchemaVersion` (existing) | unchanged | Not bumped. Before the first stable release a failed compatibility example means regenerated migrations and fixtures in place (FR-023). |

## State: a data value's text

```text
received text ──(stored as JsonElement)──► stored text == received text
       │                                         │
       │ script/activity changes it              │ read back, written again
       ▼                                         ▼
new value ──(written with the baseline encoder)  raw text written unchanged
```
