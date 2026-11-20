# Data model: JSON data integrity baseline

No stored schema changes. These are the in-code entities the plan introduces or relies on.

| Entity | Where | Fields / shape | Rules |
|---|---|---|---|
| **Baseline** | `Elsa.Serialization.Core` `JsonBaseline.Options` | `PropertyNamingPolicy = CamelCase`, `DictionaryKeyPolicy = null`, `NumberHandling = Strict`, `DefaultIgnoreCondition = Never`, `PropertyNameCaseInsensitive = false`, `Encoder = UnsafeRelaxedJsonEscaping`, converters: `RawJsonElementConverter` | Read-only (`MakeReadOnly()`). Never handed out mutable. |
| **Baseline copy** | `JsonBaseline.CreateCopy()` | A mutable copy of the baseline | May add resolvers and converters for own types. A protected setting changed on a copy is a deviation. |
| **Conversion contract** | `JsonConversionContract` (default and scoped, per user type or conversion boundary); scoped ones from `[JsonConversion]` on the type or input member | Naming policy, exact or case-insensitive matching, member mappings, unmatched-member handling (warn or strict); a baseline copy with those settings and the protected settings unchanged | The options a data-to-.NET conversion uses for its target, in both directions (FR-004, FR-004a, FR-012, FR-016). Part of the type's code; not stamped into executables. The default is camelCase, exact, warn. Dates always need `Z` or an offset. |
| **Canonical form** | `JsonCanonicalForm` | Members by unescaped name in any order, strings unescaped, numbers by exact decimal value, arrays in order, missing ≠ `null` | Value equality (FR-021a); `JsonHashInput` hashes it. Never written to the wire or a store. |
| **Hash input** | `JsonHashInput` | Explicit options (camelCase, nulls kept, string enums, depth 1000); SHA-256 over the canonical form | The only way to hash JSON (FR-021). A golden test pins each hash. |
| **Studio time zone** | Studio appearance preference `timeZone` | `"browser"` (default), `"UTC"` or an IANA ID | How Studio shows and enters dates, always with the offset; the engine stays in UTC (FR-016b). |
| **Strict date converters** | `StrictDateTimeConverters` | `DateTime`, `DateTimeOffset`: `Z` or an offset required; `DateTime` normalized to UTC; on write `Local` → UTC, `Unspecified` refused for user values and written as UTC for Elsa's own timestamps | In the API wire contexts and the conversion contracts, not the store option sets (FR-016, FR-016c). |
| **Unmatched-member warning** | `UnmatchedMemberScanner` | `code`, `path` (JSON path of the key), `targetType` | One per unmatched key, judged against the effective contract. It fails a step only under a strict scoped contract. |
| **Deviation register entry** | `docs/serialization.md`, "Baseline deviations" table | `File`, `Setting`, `Reason`, `Approved by` (ADR or issue) | The guard parses the table. A data-touching API deviation must cite an ADR. |
| **Legacy list** (option-less call ratchet) | `Essentials/tests/Architecture/Tests/Baselines/json-optionless-calls.txt` | One `path:symbol` per line | It may only shrink. Workflow-data paths may not appear in it. It is not the register. |
| **Durable format** | `[EfSchemaFamily]` `SchemaVersion` (existing) | unchanged | Not bumped. Before the first stable release a failed compatibility example means regenerated migrations and fixtures in place, not a format-version change (FR-023). |

## State: a data value's text

```text
received text ──(stored as JsonElement)──► stored text == received text
       │                                         │
       │ script/activity changes it              │ read back, written again
       ▼                                         ▼
new value ──(written with the baseline encoder)  raw text written unchanged
```
