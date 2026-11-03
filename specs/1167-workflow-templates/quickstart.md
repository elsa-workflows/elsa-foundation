# Workflow templates

Open **More tools → Templates**. Choose **New template**, name it, select a root
activity and create it. The familiar designer edits its graph, variables, inputs,
outputs, layout and activity labels. Save, then return to Templates. Templates have
no publish or run action.

Use **Copy** to customize a built-in or existing template. Built-in Blank and HTTP
starters are read-only. In a workflow designer, choose **More workflow actions →
Save as template** to capture the currently displayed design, including unsaved edits.

Choose **Use template**, or select it under **Starting point** when creating a
workflow. The workflow owns an independent copy. Editing/deleting its template
cannot change that workflow. Missing activity versions are explained; they are not
automatically upgraded.

A conflicting save leaves the local design intact and reports the conflict. Save a
copy if you need to retain both versions. Leaving a template waits for graph and
name/description persistence; a failed save keeps the designer open.

## Developer qualification

Run `e2e-tests/templates/Test-WorkflowTemplates.ps1` after building
`Essentials/apps/Elsa.Workbench/Elsa.Workbench.csproj`. It owns a new SQLite content
root and free loopback port and verifies API CRUD/replay, stale refusal, restart,
layout/labels and workflow independence. It never operates on an existing host.

The four Design EF Initial migrations are regenerated under the repository's
pre-GA policy. Existing development databases need the repository's normal schema
reset/recreation procedure before adopting this schema; this feature does not reset
or migrate an already-running developer database.
