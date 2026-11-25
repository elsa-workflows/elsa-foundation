# Contract: migration host composition and folders

This is what other parts of the migration (the migration CLI in spec 1237, the migration skill in spec 1238, the
quickstart) may rely on.

## Paths

| Item | Path (repository-relative) | Stable? |
|---|---|---|
| Host project | `extensions/Elsa3/src/Migration/Host/Elsa3.Migration.Host.csproj` | Yes |
| Composition | `extensions/Elsa3/src/Migration/Host/shells.json`, under `CShells:Shells:default:Features` | Yes |
| Reasons | The composition table in `extensions/Elsa3/src/Migration/Host/README.md` | Yes |
| Sample migrated folder | `extensions/Elsa3/src/Migration/Host/migrated/` | Yes |
| Package folder | `extensions/Elsa3/src/Migration/Host/packages/` (Nuplane directory feed `local-packages`) | Yes |
| Data | `extensions/Elsa3/src/Migration/Host/data/` (git-ignored) | Local only |

A tool reads the composition with any JSON reader. It needs no reference to the host project.

## Migrated folder layout

```text
migrated/
├── workflows/                 # flat; JSON workflow reconciliation format
└── activities/
    └── activities.json        # JSON activity reconciliation format; [] when empty; always present
```

The engineer points the host at their folder with two settings, from any standard configuration source:

```text
CShells__Shells__default__Features__JsonWorkflowReconciliation__Options__FolderPath=<abs path>/migrated/workflows
CShells__Shells__default__Features__JsonActivityReconciliation__Options__FilePath=<abs path>/migrated/activities/activities.json
```

## Behaviour

- **Readiness**: `GET /health/ready` returns 200 once the default shell is active.
- **Refusing to start** (spec): shell activation fails, `/health/ready` never returns 200, and the log names the
  cause. This happens for:
  - an unknown feature;
  - a missing workflows folder or activities file;
  - a workflows file that can't be imported.
- **Publishing**: imported definitions are published unless `PublishOnReconcile` is set to `false`.
- **Sign-in**: the development administrator from the committed settings (local use only).
- **Elsa 3**: the host carries no Elsa 3 assembly. A tool that needs Elsa 3 code composes it itself.
