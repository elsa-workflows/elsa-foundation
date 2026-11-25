# Quickstart: Elsa 3 migration host

For engineers migrating from Elsa 3 who have cloned this repository. The host is a local tool: don't deploy it.

## Run the sample

```bash
dotnet run --project apps/Elsa.Migration.Host
```

1. Wait for `GET /health/ready` to return 200.
2. Call the sample workflow's HTTP trigger (its path is in the host's README) and get its response.

## Run your migrated workflows

1. Convert your workflows with the migration CLI (spec 1237). It writes `migrated/workflows/` and
   `migrated/activities/activities.json`.
2. Point the host at your folder:

   ```bash
   dotnet run --project apps/Elsa.Migration.Host -- \
     --CShells:Shells:default:Features:JsonWorkflowReconciliation:Options:FolderPath=/abs/path/migrated/workflows \
     --CShells:Shells:default:Features:JsonActivityReconciliation:Options:FilePath=/abs/path/migrated/activities/activities.json
   ```

3. **Your own activities.** In your clone, add a project reference to your feature project in
   `apps/Elsa.Migration.Host/Elsa.Migration.Host.csproj` (restore updates its `packages.lock.json`). Then add the feature to the composition in a local settings source: a git-ignored
   `shells.Local.json` next to `shells.json`, `dotnet user-secrets`, or an environment variable.
4. Delete `data/` to start from an empty store; the host creates it again at startup.

## See what your host needs

The host's `composition-reasons.json`, shown in its README, gives every composed feature and why it is there. Use it as the starting point for your own Elsa 4
host.

## Studio

Start the repository's Studio host (named in the host's README) with its backend URL set to the migration host's
fixed URL, as the README shows, and sign in with the development administrator. The
definitions list shows what the host imported.

## Acceptance run (coordinator)

1. Start the host from the initiative head with an empty `data/` folder, and the CLI's `inventory` output for the S-15
   acceptance definitions as its migrated folder.
2. Run `jtest run` with the S-15 Elsa 4 templates. The environment differs from the Workbench one only in base URL and
   credentials.
3. Record the run in `extensions/Elsa3/tests/JTest/ledger/runs.md` in the ledger's existing columns (data-model.md), and compare it with the
   Workbench row for the same commit.
