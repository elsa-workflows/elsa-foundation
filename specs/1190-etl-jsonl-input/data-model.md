# Data model: Finite JSON Lines ETL input

Only the artifact gains a shape; every persisted runtime shape is reused.

## Artifact (v2, `Elsa.Etl.Core`)

```json
{
  "source": {
    "kind": "jsonl",
    "binding": "orders",
    "fields": [
      { "name": "id",     "pointer": "/id",            "type": "int64",   "required": true  },
      { "name": "name",   "pointer": "/customer/name", "type": "string",  "required": false }
    ]
  }
}
```

`EtlJsonlSource(Binding, Fields)`, where the fields are `EtlJsonField` and the discriminator is `jsonl`. Existing `csv` and `httpJson` documents are byte-identical.

## Pinned binding (runtime store, unchanged shape)

`EtlConnectorBindingRevision(Kind: "jsonl", Revision{Alias, Revision, Handle, ExpectedSha256})`. For a source, `ExpectedSha256` is required. For JSON Lines destinations it stays null, so the digest rule follows the role, not the kind.

## Prepared input (unchanged shape)

`EtlConnectorPreparedRun.File = EtlPreparedCsvInput(Source, Index, RecordCount)`:

| Field | CSV | JSON Lines |
|---|---|---|
| `Source.Key` | `<sha256>.csv` | `<sha256>.jsonl` |
| `Index` entry 0 | header record span | sentinel `(0, 0, bomLength)` |
| `Index` entry *i* ≥ 1 | record *i* span, contiguous | record *i* span; gaps only over blank lines |
| `Index.ByteLength` | `(RecordCount + 1) × 24 ≤ Source × 24` | `(RecordCount + 1) × 24 ≤ (Source + 1) × 24` |
| `RecordCount` | data rows | non-blank lines; preparation refuses more than `int.MaxValue` |

## Position, batch range and identity (unchanged)

`EtlConnectorFilePosition(Start, EndExclusive, Next)` over ordinals 1..N; `EtlConnectorFileBatchRange`; `ConnectorBatchId` hashes the prepared references, the count, the range, the canonical bytes and their hash.

## Canonical batch record (unchanged format, version 2)

`{"index": ordinal, "rawInputBytes": end − start, "recordOrdinal": ordinal, "fields": [...]}`, or `{"index", "rawInputBytes", "error": {"code", "field"?, "recordOrdinal"}}`, with fields in declaration order.

## Record error codes

| Code | Cause |
|---|---|
| `etl.jsonl.record-invalid` | not one valid JSON value, duplicate property name, too deep, invalid UTF-8 |
| `etl.jsonl.record-not-object` | valid JSON whose top level is not an object |
| `etl.record-too-large` | content (span minus terminator) over `limits.maxRecordBytes` |
| `etl.required-value-missing` | required field missing or null |
| `etl.invalid-value` | field of the wrong JSON type or out of range for its scalar type |

## Preparation diagnostics added

`etl.source-too-many-records`: more than `int.MaxValue` records.
