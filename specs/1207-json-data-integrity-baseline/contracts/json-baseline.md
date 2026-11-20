# Contract: JSON baseline, wire changes and diagnostics

## Wire changes (fixes under matrix row 21)

| Surface | Before | After |
|---|---|---|
| Runtime instance outputs, expression parameter keys | camelCased (`orderTotal`, `amount`) | as authored (`OrderTotal`, `Amount`) |
| Publishing and Activities Design input-name keys | camelCased | as authored |
| Secrets error keys from input names | camelCased | as authored |
| A data value in any Runtime or Design response | re-escaped (`Ren\u00E9`, `\u003C`, `\u002B`) | text as received |
| A changed value or Elsa's own strings | default encoder (Runtime, Design) or relaxed (Publishing) | relaxed everywhere |
| `IPayloadSerializer` output | nulls dropped | nulls kept |
| Git definition files | data re-escaped through `JsonNode` | data text as received |
| Elsa API responses | no `X-Content-Type-Options` | `application/json` and `X-Content-Type-Options: nosniff` |
| Workflow-authored HTTP responses | author's type, or inferred | author's type; with none, text → `text/plain`, anything else → `application/json` |
| A date/time without an offset converted to an instant | System.Text.Json default or server offset | refused |
| An activity input read from `{"CustomerName":…}` into `CustomerName` (default contract) | filled (case-insensitive since #3801) | not filled; `json-unmatched-member` warning |
| Studio Run dialog, a `DateTime` or `DateTimeOffset` input | `datetime-local` text without an offset | ISO 8601 with the offset of Studio's time zone setting |
| Studio date displays | browser local time, no offset | Studio's time zone setting, offset shown |
| An offset-less date-time sent to an Elsa API | read with the server's offset or as unspecified | 400, `json-datetime-offset-missing` |
| Every hash over JSON (artifact hash, contract fingerprint, Git hash, operation fingerprints) | own options per site | one value change, then `JsonHashInput` over the canonical form |
| Elsa 3 date literal or default without an offset | converted as written | refused, or written with the source zone's offset or `Z` (S-1's date interpretation) |

Keys that start with a lowercase letter are byte-identical before and after.

## Diagnostics

| Code | Severity | When | Names |
|---|---|---|---|
| `json-unmatched-member` | warning | A JSON key matches no member under the effective conversion contract (FR-005) | key path, target type |
| `json-unmatched-member-refused` | refusal | The same, under a scoped contract that selects strict handling (FR-005) | key path, target type |
| `json-unmatched-coverage-unavailable` | info | A custom converter gives no matching information, so unmatched keys are not checked for that type (FR-005) | target type, converter |
| `json-datetime-offset-missing` | refusal (400 at an API) | A date-time without `Z` or an offset reaches an Elsa API or a conversion to an instant (FR-016, FR-016c) | path, target type |
| `ELS3-LITERAL-KEY-AMBIGUOUS` | refusal | An Elsa 3 literal has keys that differ only in case and both fit one member (FR-015) | node, input, the keys |
| `ELS3-LITERAL-KEY-REWRITTEN` | info | An Elsa 3 literal key was rewritten to the exact JSON name (FR-015) | node, input, old and new key |
| `ELS3-LITERAL-DATE-OFFSET-MISSING` | refusal | An Elsa 3 date literal or default has no offset and the date interpretation is `Refuse` (FR-016a) | node, input or variable, the value |
| `ELS3-LITERAL-DATE-INTERPRETED` | warning (`AssumeUtc`) or info (`SourceTimeZone`) | An offset was written into an Elsa 3 date literal (FR-016a) | node, input or variable, old and new text |
| `ELS3-LITERAL-DATE-WALL-CLOCK` | warning | An Elsa 3 date literal with `Z` or an offset is bound to a `DateTime` or `DateOnly` member, or a non-literal expression is bound to any date member, so the hour or date may differ from what Elsa 3 showed (FR-016a) | node, input or variable |

## Baseline deviation register (format)

The register is a table in `docs/serialization.md` under "Baseline deviations". The guard reads it.

```markdown
| File | Setting | Reason | Approved by |
|---|---|---|---|
| `Serialization/SystemText/Services/JsonPayloadSerializer.cs` | `DeterministicDictionaryConverter`, `SortObjectMembers` | Byte-stable persisted output for our own maps | ADR 0091 decision 7 |
```
