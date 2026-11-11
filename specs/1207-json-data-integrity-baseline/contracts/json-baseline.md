# Contract: JSON baseline, wire changes and diagnostics

## Wire changes (fixes under matrix row 21)

| Surface | Before | After |
|---|---|---|
| Runtime instance outputs, expression parameter keys | camelCased (`orderTotal`, `amount`) | as authored (`OrderTotal`, `Amount`) |
| Publishing and Activities Design input-name keys | camelCased | as authored |
| Secrets error keys from input names | camelCased | as authored |
| A data value in any Runtime or Design response | re-escaped (`René`, `<`, `+`) | text as received |
| A changed value or Elsa's own strings | default encoder (Runtime, Design) or relaxed (Publishing) | relaxed everywhere |
| `IPayloadSerializer` output | nulls dropped | nulls kept |
| Git definition files | data re-escaped through `JsonNode` | data text as received |
| Elsa API responses | no `X-Content-Type-Options` | `application/json` and `X-Content-Type-Options: nosniff` |
| Workflow-authored HTTP responses | author's type, or inferred | author's type; with none, text → `text/plain`, anything else → `application/json` |
| A date/time without an offset converted to an instant | System.Text.Json default or server offset | its contract's declared interpretation, else refused |

Keys that start with a lowercase letter are byte-identical before and after.

## Diagnostics

| Code | Severity | When | Names |
|---|---|---|---|
| `json-unmatched-member` | warning | A JSON key matches no member under the effective conversion contract (FR-005) | key path, target type |
| `json-unmatched-member-refused` | refusal | The same, under a scoped contract that selects strict handling (FR-005) | key path, target type |
| `json-unmatched-coverage-unavailable` | info | A custom converter gives no matching information, so unmatched keys are not checked for that type (FR-005) | target type, converter |
| `json-datetime-interpretation-missing` | refusal | A date/time value without an offset is converted to an instant and the contract declares no source time zone or assume-UTC policy (FR-016) | path, target type |
| `ELS3-LITERAL-KEY-AMBIGUOUS` | refusal | An Elsa 3 literal has keys that differ only in case and both fit one member (FR-015) | node, input, the keys |
| `ELS3-LITERAL-KEY-REWRITTEN` | info | An Elsa 3 literal key was rewritten to the exact JSON name (FR-015) | node, input, old and new key |

## Baseline deviation register (format)

The register is a table in `docs/serialization.md` under "Baseline deviations". The guard reads it.

```markdown
| File | Setting | Reason | Approved by |
|---|---|---|---|
| `Serialization/SystemText/Services/JsonPayloadSerializer.cs` | `DeterministicDictionaryConverter`, `SortObjectMembers` | Byte-stable persisted output for our own maps | ADR 0091 decision 7 |
```
