# Direct Weaver workflow edits target designer working state

Weaver may directly construct and modify workflow definitions, but direct apply means updating the visible designer working state as an undoable transaction, not silently committing persisted draft state. The designer remains responsible for undo/redo, validation, dirty-state tracking, and save/publish flow so Weaver can accelerate authoring without bypassing user control over durable workflow changes.
