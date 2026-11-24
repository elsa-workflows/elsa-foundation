#!/usr/bin/env bash
set -euo pipefail
read -r -a versions <<< "${PG_VERSIONS:-16 17}"
if (( ${#versions[@]} == 0 )); then
  echo "Select at least one PostgreSQL version." >&2
  exit 2
fi
repo=$(git -c core.fsmonitor=false rev-parse --show-toplevel)
revision=$(git -C "$repo" -c core.fsmonitor=false rev-parse --verify --end-of-options "${1:-HEAD}^{commit}")
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
import json
from pathlib import Path
import xml.etree.ElementTree as ET

app = Path('apps/Elsa.Workbench')
locked = json.loads((app / 'packages.lock.json').read_text())['dependencies']['net10.0']
provider = Path('prototypes/kernel-model/ef/Kernel.EntityFramework/Kernel.EntityFramework.csproj')
provider_locked = json.loads(provider.with_name('packages.lock.json').read_text())['dependencies']['net10.0']
workbench_versions = {package: info['resolved'] for package, info in locked.items() if 'resolved' in info}
xml = ET.parse(provider)
references = {entry.attrib['Include']: entry for entry in xml.findall('.//PackageReference')}
if references.keys() - workbench_versions.keys():
    raise SystemExit("Workbench does not lock every direct provider dependency.")
pins = references.keys() | (provider_locked.keys() & workbench_versions.keys())
transitive = ET.SubElement(xml.getroot(), 'ItemGroup')
for package in sorted(pins):
    reference = references.get(package)
    if reference is None:
        reference = ET.SubElement(transitive, 'PackageReference', Include=package)
    reference.set('Version', workbench_versions[package])
    print(f'Compile kernel provider with Workbench {package} {workbench_versions[package]}')
xml.write(provider, encoding='unicode')
Path('expected-workbench-providers.json').write_text(json.dumps(workbench_versions))
project=app/'Elsa.Workbench.csproj'
text=project.read_text()
if text.count('</Project>') != 1:
    raise SystemExit('Cannot find the Workbench project insertion point.')
project.write_text(text.replace('</Project>', '''
  <ItemGroup>
    <ProjectReference Include="../../essentials/src/ActorFoundation/Hosting/Elsa.ActorFoundation.Hosting.csproj" />
    <ProjectReference Include="../../prototypes/kernel-model/runtime/Kernel.Foundation.Postgres/Kernel.Foundation.Postgres.csproj" />
    <ProjectReference Include="../../prototypes/kernel-model/models/Native/Kernel.Native.csproj" />
    <ProjectReference Include="../../prototypes/kernel-model/src/Kernel.WorkflowActor/Kernel.WorkflowActor.csproj" />
    <Compile Include="../../tools/ci/actor-foundation/WorkbenchPostgresPreview.cs" Link="Qualification/WorkbenchPostgresPreview.cs" />
    <Compile Include="../../prototypes/kernel-model/models/Native.Run/DirectApprovalWorkflow.cs" Link="Qualification/DirectApprovalWorkflow.cs" />
    <Compile Include="../../prototypes/kernel-model/ef/Kernel.Ef.Run/Wire.cs" Link="Qualification/Wire.cs" />
    <Compile Include="../../prototypes/kernel-model/models/Native.Run/PostgresFixtureDatabase.cs" Link="Qualification/PostgresFixtureDatabase.cs" />
  </ItemGroup>
</Project>'''))
program=app/'Program.cs'
text=program.read_text()
entry='var bootStopwatch = Stopwatch.StartNew();'
if text.count(entry) != 1:
    raise SystemExit('Cannot find the Workbench entry point.')
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

python3 - <<'PYDEPS'
import json
from pathlib import Path

expected = json.loads(Path('expected-workbench-providers.json').read_text())
manifest = Path('apps/Elsa.Workbench/bin/Release/net10.0/Elsa.Workbench.deps.json')
compile_assets = Path('prototypes/kernel-model/ef/Kernel.EntityFramework/obj/project.assets.json')
actual = dict(key.rsplit('/', 1) for key in json.loads(manifest.read_text())['libraries'])
compiled = dict(key.rsplit('/', 1) for key in json.loads(compile_assets.read_text())['targets']['net10.0'])
critical = ('Microsoft.EntityFrameworkCore', 'Microsoft.EntityFrameworkCore.Relational',
    'Microsoft.EntityFrameworkCore.Sqlite', 'Microsoft.EntityFrameworkCore.SqlServer',
    'Npgsql.EntityFrameworkCore.PostgreSQL', 'Npgsql', 'MySql.EntityFrameworkCore', 'Microsoft.Data.SqlClient')
for package in sorted((compiled.keys() & expected.keys()) | set(critical)):
    version = expected[package]
    if compiled.get(package) != version or actual.get(package) != version:
        raise SystemExit(f'Provider version mismatch for {package}: Workbench locked {version}, '
                         f'compile {compiled.get(package)}, host runtime {actual.get(package)}')
    print(f'ok matching provider compile and runtime dependency {package} {version}')
PYDEPS

for major in "${versions[@]}"; do
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
  invalid="$scratch/pg$major-invalid-receipt.json"
  python3 - "$receipt" "$invalid" <<'PYRECEIPT'
import json, sys
from pathlib import Path

receipt = json.loads(Path(sys.argv[1]).read_text())
receipt['StateHash'] = ''
Path(sys.argv[2]).write_text(json.dumps(receipt))
PYRECEIPT
  nodes() { docker exec "$container" psql -XAtq -U postgres -d workbench_preview -c 'SELECT count(*) FROM "Nodes"'; }
  before=$(nodes)
  if dotnet "$workbench" --qualify-workbench-postgres restart "$invalid" > "$scratch/refusal.log" 2>&1; then
    echo "Restart accepted an invalid receipt." >&2
    exit 1
  fi
  grep -Fq 'the receipt matches the stopped waiting actor before owner startup' "$scratch/refusal.log"
  [[ "$(nodes)" == "$before" ]]
  echo "ok invalid receipt refused without registering an owner"
  dotnet "$workbench" --qualify-workbench-postgres restart "$receipt"
  docker stop --time 3 "$container" >/dev/null
  container=
done
