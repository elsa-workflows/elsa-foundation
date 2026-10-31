# Organizing workflows

Use folders for a workflow's home, tags for overlapping classifications, and saved views for queries you return to. Each appearance refers to the same workflow definition.

For example, **Collect overdue payment** can live in **Finance / Receivables**, carry **critical** and **customer-facing** tags, and appear in a saved view for published finance workflows.

## Folders

Open **Workflows** to browse **All workflows**, **Unfiled**, or a folder. Expand folders as needed. Search is scoped to the selected location; choose All workflows to search across the library. Select **Include subfolders** when a folder search should cover its descendants.

Create a folder at the current level, then create a workflow inside it. Use **Move to folder** for one workflow or a selected group. A group move succeeds for the entire selection or leaves it unchanged. Rename or move folders as your organization evolves; workflow links retain their identity.

Delete a folder only after moving its child folders and workflows elsewhere. Deleted workflows retain their placement and also prevent an empty-folder deletion. Restoring a workflow restores its organization.

## Tags and classifications

Open a workflow's action menu and choose **Organization**, or use Organization in the editor's Properties. Select labels and controlled values, then **Save tags**. Organization is saved independently from the authored draft.

Simple labels such as **critical** can be toggled directly. Controlled classifications such as **Owning team** present an agreed set of values; a single-valued classification accepts one choice. Reuse existing vocabulary through search. Authorized catalog managers can use **Manage tags** to create labels/classifications, rename them, manage values and retire obsolete vocabulary.

Retired tags and values remain understandable on existing workflows and in filters. They cannot be newly assigned. A conflicting concurrent edit asks you to reload and reconcile your intended changes.

To change several workflows, select their rows and choose **Edit tags**. Choose Add or Remove, select the intended labels/values, and **Preview changes** before applying. The result identifies each workflow's outcome. Successful changes remain applied when another target conflicts; reload the affected targets before a new attempt.

## Filters, views and favorites

Use **Filters** to combine classification conditions with the current location, search and lifecycle. All tag conditions must match. For a controlled condition choose whether any, every or none of the selected values should match. Missing-tag conditions help find organization gaps. The publication filter selects definitions that currently serve an activation, using the library's publication projection.

Choose **Save view**, name the query and save it privately. With sharing permission, select **Share within this tenant**. The view remembers location/subfolders, filters, sorting and optional columns. Its results change as workflows change. Sharing a view grants no access to its matching workflows. Missing references produce a diagnostic so you can intentionally repair the view.

Use a workflow's **Add to favorites** action for a personal shortcut, and select **Favorites** to browse your own set. Other users' favorites remain independent.

Keep Name and Definition ID visible for scanning and exact copying. Tags and Location are optional columns. Browser Back and editor return preserve your browsing context; changing a membership filter restarts paging and selection.

## Behavior and limits

Folders and tags organize definitions. They do not change credentials, access, scheduling, deployment, publication, authored versions or running workflows. Source-managed workflow content can remain read only while organization is separately permitted. Source reconciliation preserves manual organization.

Capabilities depend on the connected server and your permissions. An older or differently composed server can offer fewer controls. The reference current host uses durable server-side organization and bounded paging; Studio does not download the entire library to filter it.

One explicit bulk operation supports at most 100 workflows. Folders support up to 16 levels. Queries accept at most 16 tag conditions and 32 values in a condition. Large jobs selected from a changing query, manually ordered collections, free-text classifications and source-produced tag assignments are future extensions.

Definition content export/import does not automatically copy tenant-specific folders, vocabulary or personal views. Choose organization in the destination library explicitly.
