# Edit authoring draft JSON, not database records

The Activity Definition Code view edits a canonical JSON representation of the complete editable draft boundary: its Public Interface contract, provider implementation payload, layout, and presentation label. Database representation and server-managed facts—including identity, revision, Activity Type Key, provider migration, timestamps, validation, and lifecycle—are not editable JSON contracts; this mirrors the Workflow Definition Code view, which edits draft state and layout rather than a complete persistence record.

Unapplied Code-view text is an editor-local buffer and never enters autosave. Invalid or dirty text blocks validation, test, migration, publication, and unconfirmed navigation until the user applies a valid document or resets the buffer.
