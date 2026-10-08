# Foundation backend source commands

This page includes only two commands from the current Foundation backend source quickstart. Follow that canonical guide for prerequisites, startup, readiness, smoke checks, and troubleshooting; this fixture does not replace it.

## Documented backend commands

The quickstart documents these Linux commands from the Foundation repository root:

```bash
dotnet build src/apps/Elsa.Workbench/Elsa.Workbench.csproj -p:RestoreLockedMode=true
dotnet run --no-build --project src/apps/Elsa.Workbench/Elsa.Workbench.csproj --launch-profile http
```

Source provenance: Foundation [`docs/contributing/backend-source-quickstart.md`, lines 92–97](https://github.com/elsa-workflows/elsa-foundation/blob/8a2a97d4e31857779da547797eed10f478dd8874/docs/contributing/backend-source-quickstart.md#L92-L97), read at `8a2a97d4e31857779da547797eed10f478dd8874`.

Continue with the [Markdown rendering page](./markdown-rendering.md#generic-c-code-fence).

## Source and editing

- Fixture source: [Foundation `tools/spikes/gitbook/content/backend-source.md`](https://github.com/elsa-workflows/elsa-foundation/blob/claude/2498-gitbook-sync-spike/tools/spikes/gitbook/content/backend-source.md)
- Edit this page: [GitHub editor](https://github.com/elsa-workflows/elsa-foundation/edit/claude/2498-gitbook-sync-spike/tools/spikes/gitbook/content/backend-source.md)
- Canonical source guide: [Foundation backend source quickstart](https://github.com/elsa-workflows/elsa-foundation/blob/8a2a97d4e31857779da547797eed10f478dd8874/docs/contributing/backend-source-quickstart.md)
