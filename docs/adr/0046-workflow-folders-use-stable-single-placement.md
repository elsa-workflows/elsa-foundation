# Workflow Folders Use Stable Single Placement

Status: proposed (2026-07-19; free-flow design sharpened through `grill-with-docs`)

Elsa organizes workflow definitions with tenant-scoped
[workflow folders](../glossary/elsa.md), represented by stable opaque identities and an adjacency
hierarchy (`ParentFolderId`). A logical workflow definition has zero or one nullable `FolderId`;
`null` produces the virtual Unfiled view. Folder placement is mutable Design-domain metadata outside
`WorkflowDefinitionState`, drafts, versions, publication state, executable identity, runtime state,
and Git content authority.

This chooses real folder identity over name-based paths, multiple folder membership, tags-as-folders,
or repository-directory inference. Renaming or moving a folder therefore preserves its identity and
the placement of every contained definition without rewriting descendant definitions. Multiple
classification remains a future tags or collections concern.

Folders organize definitions but do not themselves grant permissions or create tenant, deployment,
publication, or runtime boundaries. Deleting a non-empty folder is rejected; moving its contents is
an explicit operation. First-party durable persistence follows
[ADR 0042](0042-elsa-foundation-ships-only-groundwork-persistence-implementations.md): Core contracts
remain provider-neutral, Groundwork supplies the concrete implementation, and no new EF schema or
migration is added.

The detailed API, Studio, lifecycle, migration, verification, and deferred-extension design is
recorded in the [workflow-folder architecture report](../reports/workflow-folder-architecture.md).
