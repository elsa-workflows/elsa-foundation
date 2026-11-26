# Keep shared graph authoring persistence-agnostic

The shared graph-authoring workspace edits controlled graph state and reports changes to its host; it does not own loading, saving, autosave policy, conflict handling, or recovery. Workflow Definitions retain their workflow persistence controls, while Activity Definitions retain mandatory exact-revision autosave and recovery without inheriting the workflow Autosave toggle or Save button.

The workspace may own local undo/redo history, but undo and redo report ordinary graph edits that the host persists as new revisions rather than rewinding server history. Hosts reset local history after conflict recovery, migration, or any external document replacement.
