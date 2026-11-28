# Workflow templates

Open **More tools → Templates**. Choose **New template**, name it, select a root
activity and create it. The familiar designer edits its graph, variables, inputs,
outputs, layout and activity labels. Save, then return to Templates. Templates have
no publish or run action.

Use **Copy** to customize a built-in or existing template. Server-provided built-in
starters are read-only. Blank workflow remains a client-side starting point with its
root-activity choice. In a workflow designer, choose **More workflow actions →
Save as template** to capture the currently displayed design, including unsaved edits.

Choose **Use template**, or select it under **Starting point** when creating a
workflow. The workflow owns an independent copy. Editing/deleting its template
cannot change that workflow. Missing activity versions are explained; they are not
automatically upgraded.

A conflicting save leaves the local design intact and reports the conflict. Save a
copy if you need to retain both versions. Leaving a template waits for graph and
name/description persistence; a failed save keeps the designer open.

## Integrator: remove built-in starters

Add the ids you want to remove to the host's configuration. For example, to remove
Hello World HTTP (stable id `http`):

```json
{
  "Workflows": {
    "Design": {
      "Templates": {
        "RemovedStarters": ["http"]
      }
    }
  }
}
```

Use the ids returned by the built-in list for other starters. Removal applies to the
whole host, so the starter is absent from both Templates and Starting point for every
tenant; no Studio rebuild is needed. Unknown ids are ignored and diagnosed once at
startup. Removing every server starter still leaves Blank workflow available.

## Module author: contribute a starter

For example, a module can ship a Hello World Console starter by embedding a recipe
owned by that module and contributing its metadata through the starter contribution
interface. The following is illustrative only; the precise interface and recipe schema
are established by the implementation tasks:

```yaml
id: "<stable-module-owned-id>"
name: "Hello World Console"
owner: "<your-module-id>"
requiredActivityTypes:
  - "Elsa.Sequence"
  - "Elsa.WriteLine"
requiredExpressionLanguages: []
recipe: "<authored graph using activity types and placeholder node ids>"
```

Reference activity types, not pinned activity versions, and use placeholder node ids;
the server selects the newest available versions and creates fresh ids each time the
starter is used. A starter is absent when its owner module is not enabled. If a
required activity or expression language is missing, the starter is returned as
unavailable with an actionable explanation rather than creating an incomplete graph.

## Developer qualification

Run `e2e-tests/templates/Test-WorkflowTemplates.ps1` after building
`Essentials/apps/Elsa.Workbench/Elsa.Workbench.csproj`. It owns a new SQLite content
root and free loopback port and verifies API CRUD/replay, stale refusal, restart,
layout/labels and workflow independence. It never operates on an existing host.

The four Design EF Initial migrations are regenerated under the repository's
pre-GA policy. Existing development databases need the repository's normal schema
reset/recreation procedure before adopting this schema; this feature does not reset
or migrate an already-running developer database.
