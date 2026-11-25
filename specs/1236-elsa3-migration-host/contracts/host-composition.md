# Contract: migration host composition and folders

**Temporary.** The host is removed by the follow-up cleanup on #5800, once a Docker app profile covers the Elsa 3
import story; the migrated-folder layout and the composition's reasons are what carry over.

This is what other parts of the migration (the migration CLI in spec 1237, the migration skill in spec 1238, the
quickstart) may rely on.

## Paths

| Item | Path (repository-relative) | Stable? |
|---|---|---|
| Host project | `apps/Elsa.Migration.Host/Elsa.Migration.Host.csproj` | Yes |
| Composition | `apps/Elsa.Migration.Host/shells.json`, under `CShells:Shells:default:Features` | Yes |
| Reasons | `apps/Elsa.Migration.Host/composition-reasons.json` (feature name → reason), shown by the README | Yes |
| Sample migrated folder | `apps/Elsa.Migration.Host/migrated/` | Yes |
| Data | `apps/Elsa.Migration.Host/data/` (git-ignored) | Local only |

A tool reads the composition and the reasons with any JSON reader. The host loads no packages at runtime; an
engineer's own features come in as project references. It needs no reference to the host project.

## Migrated folder layout

```text
migrated/
├── workflows/                 # flat; JSON workflow reconciliation format
└── activities/
    └── activities.json        # JSON activity reconciliation format; [] when empty; always present
```

The engineer points the host at their folder with two settings, from user secrets, a git-ignored `shells.Local.json`,
environment variables or the command line (settings in `appsettings*.json` sit below `shells.json` and can't):

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
