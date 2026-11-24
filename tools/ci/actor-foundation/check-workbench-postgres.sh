#!/usr/bin/env bash
set -euo pipefail
repo=$(git -c core.fsmonitor=false rev-parse --show-toplevel)
revision=$(git -C "$repo" -c core.fsmonitor=false rev-parse "${1:-HEAD}^{commit}")
scratch=$(mktemp -d "${TMPDIR:-/tmp}/elsa-workbench-preview.XXXXXXXX")
container=
cleanup() {
  if [[ -n "$container" ]]; then docker stop --time 3 "$container" >/dev/null || true; fi
  rm -rf -- "$scratch"
}
trap cleanup EXIT
mkdir "$scratch/source"
git -C "$repo" -c core.fsmonitor=false archive "$revision" | tar -xf - -C "$scratch/source"
cd "$scratch/source"
echo "Workbench preview source $revision"
python3 - <<'PY'
from pathlib import Path
app=Path('apps/Elsa.Workbench')
project=app/'Elsa.Workbench.csproj'
text=project.read_text()
assert text.count('</Project>')==1
project.write_text(text.replace('</Project>', '''
  <ItemGroup>
    <ProjectReference Include="../../prototypes/kernel-model/runtime/Kernel.Foundation.Postgres/Kernel.Foundation.Postgres.csproj" />
    <ProjectReference Include="../../prototypes/kernel-model/models/Native/Kernel.Native.csproj" />
    <ProjectReference Include="../../prototypes/kernel-model/src/Kernel.WorkflowActor/Kernel.WorkflowActor.csproj" />
    <Compile Include="../../tools/ci/actor-foundation/WorkbenchPostgresPreview.cs" Link="Qualification/WorkbenchPostgresPreview.cs" />
    <Compile Include="../../prototypes/kernel-model/models/Native.Run/DirectApprovalWorkflow.cs" Link="Qualification/DirectApprovalWorkflow.cs" />
    <Compile Include="../../prototypes/kernel-model/models/Native.Run/PostgresFixtureDatabase.cs" Link="Qualification/PostgresFixtureDatabase.cs" />
  </ItemGroup>
</Project>'''))
program=app/'Program.cs'
text=program.read_text()
entry='var bootStopwatch = Stopwatch.StartNew();'
assert text.count(entry)==1
program.write_text(text.replace(entry, '''
if (args is ["--qualify-workbench-postgres", .. var previewArguments])
{
    await Elsa.Workbench.Qualification.WorkbenchPostgresPreview.RunAsync(previewArguments);
    return;
}

''' + entry))
PY
dotnet restore apps/Elsa.Workbench /p:RestoreLockedMode=false
dotnet build -c Release --no-restore apps/Elsa.Workbench
workbench=apps/Elsa.Workbench/bin/Release/net10.0/Elsa.Workbench.dll

for major in ${PG_VERSIONS:-16 17}; do
  export POSTGRES_PASSWORD
  POSTGRES_PASSWORD=$(openssl rand -hex 24)
  container=$(docker run -d --rm -e POSTGRES_PASSWORD -p 127.0.0.1::5432 "postgres:$major")
  for ((attempt=0; attempt<60; attempt++)); do
    if docker exec "$container" pg_isready -h 127.0.0.1 -U postgres >/dev/null; then break; fi
    sleep 0.25
  done
  docker exec "$container" pg_isready -h 127.0.0.1 -U postgres >/dev/null
  port=$(docker port "$container" 5432/tcp)
  port=${port##*:}
  [[ -n "$port" && "$port" != 5095 ]]
  docker exec "$container" createdb -U postgres workbench_preview
  export KERNEL_PREVIEW_POSTGRES="Host=127.0.0.1;Port=$port;Username=postgres;Password=$POSTGRES_PASSWORD;Database=workbench_preview;SSL Mode=Disable;GSS Encryption Mode=Disable"
  echo "PostgreSQL $major"
  receipt="$scratch/pg$major-receipt.json"
  dotnet "$workbench" --qualify-workbench-postgres birth "$receipt"
  dotnet "$workbench" --qualify-workbench-postgres restart "$receipt"
  docker stop --time 3 "$container" >/dev/null
  container=
done
