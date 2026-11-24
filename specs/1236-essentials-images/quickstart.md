# Quickstart: Elsa essentials images

## Start both images (user path)

```powershell
cd docker/compose
Copy-Item essentials/appsettings.example.json essentials/appsettings.json
# Replace every "change-me" value in essentials/appsettings.json.
docker compose -f docker-compose.essentials.yml up
```

- Engine: <http://localhost:13000> (health JSON). `essentials/workflows/hello.json` answers on the HTTP
  path it declares (`hello`, under the engine's HTTP activity base path).
- Studio: <http://localhost:14000>, sign in as `admin` with the password from `appsettings.json`.
- Own workflows: put Elsa 4 definition files in `essentials/workflows/` (for a migration, the CLI's `converted/`).
- Own features: put their `.nupkg` files in `essentials/packages/` and add the features under
  `CShells:Shells:default:Features` in `appsettings.json`.

## Build the images locally (initiative branch)

```powershell
docker build -f apps/Elsa.Essentials.Server/Dockerfile -t elsa-server-essentials:local .
docker build -f Studio/src/apps/Elsa.Studio.Essentials/Dockerfile -t elsa-studio-essentials:local .
```

## JTest acceptance run

1. Produce `converted/` for the S-15 acceptance definitions with the migration CLI's `inventory` command (approved
   adjusted copies in place of their originals).
2. Start a fresh engine container from the local image with `converted/` mounted at `/app/workflows` and a run
   `appsettings.json`.
3. Set capture through `PUT /runtime/workflows/diagnostics/settings`, as the S-15 README does.
4. From `extensions/Elsa3/tests/JTest`, run the S-15 Elsa 4 command with `ELSA_API_BASE_URL` and the credentials
   pointing at the container.
5. Record the run in `ledger/runs.md` with engine `essentials`, next to the Workbench row of the same head.
