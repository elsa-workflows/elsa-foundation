# Quickstart: File-based workflow deployment at startup

Validates SC-001/SC-002 end to end. Two flavours: from source (matches e2e conventions) and docker (the literal acceptance flow).

## A. From source

1. **Build + schema** (once, server stopped):

   ```bash
   dotnet build src/Apps/Elsa.Workbench/Elsa.Workbench.csproj
   ```

   (Groundwork schema deploy per `e2e-tests/README.md` if the SQLite db is fresh.)

2. **Author a definition file** into a folder, e.g. `C:/temp/defs/orders.json` — see `contracts/definition-file-format.md`. Pin `definitionId`; resolve the root activity's `actver_*` id from a running server (`Get-ActivityVersionId` in `e2e-tests/_ElsaCommon.ps1`) or compute it offline.

3. **Start with the feature composed via env vars** (no shells.json edit needed):

   ```bash
   CShells__Shells__default__Features__JsonWorkflowReconciliation__Options__SourceId=local-defs \
   CShells__Shells__default__Features__JsonWorkflowReconciliation__Options__FolderPath=C:/temp/defs \
   CShells__Shells__default__Features__JsonWorkflowReconciliation__Options__PublishOnReconcile=true \
   dotnet run --project src/Apps/Elsa.Workbench/Elsa.Workbench.csproj --launch-profile http
   ```

4. **Wait for readiness** — poll until 200 (this, not `/`, is the deployment gate):

   ```bash
   curl -s http://localhost:5095/health/ready
   ```

5. **Assert** (after cookie login `admin` / `Password123!` via `POST /_elsa/identity/login`):
   - `GET /design/workflows/definitions?name=orders-intake` → definition present, `isSourceOwned`.
   - `GET /publishing/workflows/{definitionId}/slots` → slot with `activePublicationId`, `publication.artifactId`, status Active.
   - `POST /runtime/workflows/executables/{artifactId}/execute` (body may carry `sourceReferenceId`) → 200, instance completes.

6. **Idempotency (SC-002)**: restart the server with the folder unchanged → same version count for the definition, same `activePublicationId`, log shows the skip path (no new publication).

7. **Failure surfaces worth spot-checking**: two path options set → activation fails with the exactly-one message; missing folder → activation fails naming the path; bad `activityVersionId` → definition imports, publish failure logged, server still reaches ready, other definitions still publish.

## B. Docker (SC-001 literal)

```bash
docker build -f src/Apps/Elsa.Workbench/Dockerfile -t elsa-workbench:local .
docker run -p 13000:8080 \
  -v ./defs:/app/workflow-definitions:ro \
  -v ./my-shells.json:/app/shells.json:ro \
  elsa-workbench:local
```

`my-shells.json` = the shipped default plus:

```jsonc
"JsonWorkflowReconciliation": {
  "Options": {
    "SourceId": "mounted-definitions",
    "FolderPath": "/app/workflow-definitions",
    "PublishOnReconcile": true
  }
}
```

Wait on `http://localhost:13000/health/ready`, then run the same three assertions as step A5. Restart the container to confirm SC-002.

## C. Automated

- Unit: `dotnet test tests/Elsa/Workflows/Design/Tests` (options matrix, folder scan, claims) and `dotnet test tests/Elsa/Workflows/Publishing/Tests` (subscriber behaviour, registration).
- e2e: `powershell -NoProfile -ExecutionPolicy Bypass -File e2e-tests/file-deployment/Test-FileBasedDeployment.ps1` (owns its server lifecycle, durability-style).
- Guards: `dotnet test tests/Elsa/Architecture` (Workbench catalog pin, seam rules, catalog parity, EF ratchet).
