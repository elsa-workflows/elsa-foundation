# Data model: JSON data integrity baseline

No stored schema changes. These are the in-code entities the plan introduces or relies on.

| Entity | Where | Fields / shape | Rules |
|---|---|---|---|
| **Baseline** | `Elsa.Serialization.Core` `JsonBaseline.Options` | `PropertyNamingPolicy = CamelCase`, `DictionaryKeyPolicy = null`, `NumberHandling = Strict`, `DefaultIgnoreCondition = Never`, `PropertyNameCaseInsensitive = false`, `Encoder = UnsafeRelaxedJsonEscaping`, converters: `RawJsonElementConverter` | Read-only (`MakeReadOnly()`). Never handed out mutable. |
| **Baseline copy** | `JsonBaseline.CreateCopy()` | A mutable copy of the baseline | May add resolvers and converters for own types. A protected setting changed on a copy is a deviation. |
| **Data-boundary copy** | `JsonBaseline.DataBoundary` | Baseline plus the UTC `DateTime` converter (ADR 0046 *Dates*) | The only options a data-to-.NET conversion uses (FR-012). |
| **Canonical form** | `CanonicalJson` | Members sorted by ordinal name; fixed default writer; numbers as their raw text | Hash preimages and value equality (FR-021). Never written to the wire or a store. |
| **Unmatched-member warning** | `UnmatchedMemberScanner` | `code`, `path` (JSON path of the key), `targetType` | One per unmatched key. It never fails a step. |
| **Deviation register entry** | `docs/serialization.md`, "Baseline deviations" table | `File`, `Setting`, `Reason`, `Approved by` (ADR or issue) | The guard parses the table. A data-touching API deviation must cite an ADR. |
| **Option-less call ratchet** | `Essentials/tests/Architecture/Tests/Baselines/json-optionless-calls.txt` | One `path:symbol` per line | It may only shrink. Value-path sites may not appear in it. |
| **Durable format stamp** | `[EfSchemaFamily]` `SchemaVersion` (existing) | unchanged | Not bumped by this spec (R-9). |

## State: a data value's text

```text
received text ──(stored as JsonElement)──► stored text == received text
       │                                         │
       │ script/activity changes it              │ read back, written again
       ▼                                         ▼
new value ──(written with the baseline encoder)  raw text written unchanged
```
