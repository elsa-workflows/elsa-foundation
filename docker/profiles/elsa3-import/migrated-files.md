# Mount an already-converted Elsa 4 catalog

The Elsa 3 Import image includes two Elsa 4 JSON readers that you can enable in a mounted `shells.json`. The raw Elsa 3 import API still accepts Elsa 3 definitions. The file readers expect Elsa 4 reconciliation files.

The mounted directory follows this layout. Put workflow JSON files at the top level of `workflows/`. Use `[]` in `activities.json` when the activity catalog is empty. [Spec 1237 CLI contract](https://github.com/elsa-workflows/elsa-foundation/pull/5678)

```text
migrated/
├── activities/
│   └── activities.json
└── workflows/
    └── example.json
```

From the repository root, generate a complete shell file that enables the readers. The sample uses a CLR-backed catalog activity that writes one line. Set `migrated_dir` to your converted folder to load your own files.

```sh
profile_dir="$PWD/docker/profiles/elsa3-import"
migrated_dir="$profile_dir/example-migrated"
jq --slurpfile readers "$profile_dir/migrated-files.features.json" \
  '.CShells.Shells.default.Features += $readers[0]' \
  "$profile_dir/shells.json" > import-shells.json
```

Use the standard profile deployment settings with the Elsa 3 Import image. Mount `import-shells.json` as the complete shell file and the catalog directory as a second read-only volume. [Profile deployment settings](../README.md#configure-a-deployment)

```sh
--mount "type=bind,source=$PWD/import-shells.json,target=/app/shells.json,readonly" \
--mount "type=bind,source=$migrated_dir,target=/app/migrated,readonly"
```

At startup, the activity reader reconciles the catalog and the workflow reader reconciles and requests publication of the workflows. After readiness, check that each expected workflow has an active publication. The sample workflow has ID `wfdef-migrated-files-example` and writes `Hello from the mounted Elsa 4 catalog`. [Activity JSON format](../../../server/essentials/src/Activities/Design/Reconciliation/Json/README.md) · [Workflow JSON format and publication behavior](../../../server/essentials/src/Workflows/Design/Reconciliation/Json/README.md)

The current main-line engine refuses source-owned composite activities with child activity-version dependencies. The fix is on the Elsa 3 migration initiative branch. The sample exercises the readers supported by main, including workflow execution using an activity supplied by the mounted catalog. [Source-owned composite dependency fix](https://github.com/elsa-workflows/elsa-foundation/pull/5685)
