# Workflow execution

Curated Essentials runs published Native and Flowchart workflows on the built-in Actor Foundation.
This became the default in preview `4.0.0-preview.3013`. The stock Workbench source configuration
also uses Actor Foundation. Check which host and version you are running if you started with the
[historical quickstart image pair](quickstart.md). See the
[default switch release receipt](https://github.com/elsa-workflows/elsa-foundation/issues/4229#issuecomment-7231238178).

## Run and inspect a workflow

Publish a workflow before starting it. The run uses the published version selected at the start.
A run keeps that version if you publish a newer one. Open the run's **Timeline** in Studio to
see committed activity events. API clients can read
`GET /runtime/workflows/instances/{workflowExecutionId}/history`.

## Choose where execution state lives

| Provider | Use it with |
| --- | --- |
| SQLite | The local Workbench or a single server process. Use one process per Actor database file, and persist both the Actor and platform database files across container replacement. |
| PostgreSQL | The Workbench Compose source stack or Curated Essentials configured for PostgreSQL. Use a dedicated Actor database alongside the database for workflow definitions, publications and other server data. |

See the [Curated Essentials deployment settings](https://github.com/elsa-workflows/elsa-foundation/blob/main/docker/profiles/essentials-curated/README.md#workflow-execution)
and [Workbench Compose setup](https://github.com/elsa-workflows/elsa-foundation/blob/main/docker/compose/README.md#2-fast-path--elsaworkbench--postgresql).

If a workload still needs the earlier runtime, use the tested
[pinned Full profile](https://github.com/elsa-workflows/elsa-foundation/blob/main/docker/profiles/full-compatibility.md)
with its matching data. The [architecture tour](https://github.com/elsa-workflows/elsa-foundation/blob/main/docs/architecture-tour.md)
explains the execution layers. [Program #4229](https://github.com/elsa-workflows/elsa-foundation/issues/4229)
records the earlier engine's retirement from active source and the release evidence.

---

[Edit this page on GitHub](https://github.com/elsa-workflows/elsa-foundation/edit/main/website/docs/essentials/workflow-execution.md).
