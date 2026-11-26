# Contract: Activity Definition Authoring Draft JSON

## Editable projection

```json
{
  "presentationLabel": "Optional draft label",
  "contract": {
    "contractSchemaVersion": "1",
    "inputs": [],
    "outputs": [],
    "outcomes": []
  },
  "implementation": {
    "providerKey": "elsa.activity-graph",
    "schemaVersion": "2",
    "payload": {}
  },
  "layout": []
}
```

## Excluded or read-only state

- definition and draft identifiers
- activity type key and tenant identity
- revision and source-version identity
- created/updated timestamps
- validation results
- lifecycle/publication state
- provider manifest fingerprint
- available migrations and migration status

Provider key and schema identify the editable payload contract but cannot be changed through ordinary JSON Apply. A provider/schema change must use the explicit migration workflow.

## Buffer behavior

- Opening Code serializes the latest applied authoring projection.
- Typing changes only a local text buffer.
- Parse or semantic errors remain visible in Code and never reach autosave.
- While dirty, navigation requires Apply, Reset, or explicit discard confirmation.
- Validation, migration, test, proposal apply, and publication are disabled while the buffer is dirty or invalid.
- Reset restores the current applied projection and creates no history or server revision.

## Apply behavior

Apply succeeds only when:

- JSON parses to an object
- required projection fields are present and correctly shaped
- provider key/schema equal the current draft
- contract reference keys and provider invariants are locally valid
- node identifiers required for layout reconciliation are valid

On success:

1. preserve layout for every unchanged node identifier
2. discard layout for removed nodes
3. place new nodes deterministically without overlap
4. emit one controlled document change
5. record one history entry
6. let the Activity Definition host create the next exact revision

On failure, no controlled document change or autosave occurs.
