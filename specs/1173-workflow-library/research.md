# Research decisions

Independent persistence, API and Studio research under the Spec Kit plan workflow established:

- **Reuse Studio folders, implement current-host support.** Historical #790 used Groundwork; current EF capability lacks folders/paging. Rebuilding the tree or assuming old completion would miss the delivery gap.
- **Use existing EF metadata seams.** Templates demonstrate revision/receipt handling, module schema stamps and backend ownership. Scope/hash keys protect tenant and SQL Server trailing-space semantics. Current Initial migrations are regenerated, not incrementally extended.
- **Use a hierarchy fence and SQL projections.** Read-committed ancestry checks permit concurrent cycles/delete-placement races. Legacy listing is unbounded and its projection loads all versions; paging that route does not establish history-independent work.
- **Add a separate advertised page endpoint.** Preserve legacy clients; enroll all new DTOs in source-generated wire metadata. Capability discovery is permission-neutral; derive personal subject server-side and authorize body targets explicitly.
- **Unify Studio query identity.** Folder/subtree/tag/favorite/sort choices must all reset cursor/selection and participate in stale-response rejection. Lazy organization dialogs preserve landing bundle budgets.
- **Keep vocabulary workflow-specific initially.** Another resource would earn a reusable Tagging domain. JSON-only assignment blobs are rejected because portable indexed tag queries are required.

These decisions resolve the implementation unknowns without treating historical Groundwork tests as current evidence.
