# Local setting review input v1

This optional, local input supplies the safety classification that the selection catalog does not contain. It is **not** a portable composition or a copy of host values. Omitting it imports exact feature decisions and logical resource references while leaving all feature setting values in local files.

The first CLI slice accepts `--setting-review <path>` containing one JSON object:

```json
{
  "schemaVersion": "1",
  "fields": [
    { "featureId": "A", "pointer": "/Flag", "type": "boolean", "portable": true },
    { "featureId": "A", "pointer": "/Limit", "type": "number", "portable": true }
  ]
}
```

`pointer` is an RFC 6901 JSON pointer relative to that feature's settings object, with escaped `~0` and `~1` segments. Entries are exact leaves; a parent path does not classify its children. Type is one of `boolean`, `number`, `string`, `null`, `object`, or `array`. Nonempty object/array values require reviewed leaf entries rather than one parent approval. A declared type mismatch, duplicate or case-equivalent field identity, unknown top-level key, malformed pointer, or unsafe feature ID refuses the review input. The input contains no values, paths to source files, provider names, connections, or credentials. It must not be copied to generated host files or serialized into authored v1.

`portable: true` is an explicit human assertion that the named field may be shown and carried as portable intent. There is no `copy all` setting. A nonclassified field stays masked by identity/type/presence in preview and remains only in the local source/candidate bundle. Known physical connection and provider trees are never setting-review targets. The human assertion is not proof that a field is safe; the operator must inspect the local configuration before approving the preview. A later descriptor-backed contract can replace this manual input without changing authored v1.

For generation, the same reviewed field identities must cover every authored setting path to be patched. A new path also requires an explicit target source layer in a later contract; v1 refuses it as `bridge-mapping-unresolved`. The two-shell fixture uses exactly `/Flag` and `/Limit`; `A.Future` stays unclassified. Thus both canaries in the fixture remain outside stdout, stderr, plan, and portable authored output.
