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
provider_project=server/essentials/src/ActorFoundation/BuiltIn/Store/EntityFrameworkCore/Elsa.ActorFoundation.BuiltIn.Store.EntityFrameworkCore.csproj
postgres_host_project=server/essentials/src/ActorFoundation/BuiltIn/PostgreSql/Elsa.ActorFoundation.BuiltIn.PostgreSql.csproj
if [[ ! -f "$provider_project" ]]; then
  provider_project=prototypes/kernel-model/ef/Kernel.EntityFramework/Kernel.EntityFramework.csproj
  postgres_host_project=prototypes/kernel-model/runtime/Kernel.Foundation.Postgres/Kernel.Foundation.Postgres.csproj
fi
provider_dir=${provider_project%/*}
python3 - "$provider_project" "$postgres_host_project" <<'PY'
import json
from pathlib import Path
import sys
import xml.etree.ElementTree as ET

app = Path('server/apps/Elsa.Workbench.Host')
locked = json.loads((app / 'packages.lock.json').read_text())['dependencies']['net10.0']
provider, postgres_host = map(Path, sys.argv[1:])
workbench_versions = {package: info['resolved'] for package, info in locked.items() if 'resolved' in info}
provider_packages = {ref.attrib['Include'] for ref in ET.parse(provider).findall('.//PackageReference')}
provider_packages.update({'Microsoft.EntityFrameworkCore', 'Microsoft.EntityFrameworkCore.Relational',
                          'Microsoft.Data.SqlClient', 'Microsoft.Data.Sqlite.Core', 'MySql.Data', 'Npgsql'})
provider_lock = json.loads(provider.with_name('packages.lock.json').read_text())['dependencies']['net10.0']
provider_versions = {package: info['resolved'] for package, info in provider_lock.items() if 'resolved' in info}
for package in sorted(provider_packages):
    actual = provider_versions.get(package)
    expected = workbench_versions.get(package)
    if expected is None or actual != expected:
        raise SystemExit(f'Provider version mismatch for {package}: Workbench locked {expected}, '
                         f'provider locked {actual}')
Path('expected-workbench-providers.json').write_text(json.dumps({
    'aligned': sorted(provider_packages), 'provider': provider_versions, 'workbench': workbench_versions
}))
project=app/'Elsa.Workbench.Host.csproj'
text=project.read_text()
if text.count('</Project>') != 1:
    raise SystemExit('Cannot find the Workbench project insertion point.')
native_runner = Path('server/essentials/tests/ActorFoundation/Native/Run')
if not native_runner.is_dir():
    native_runner = Path('prototypes/kernel-model/models/Native.Run')
if not (native_runner / 'PostgresFixtureDatabase.cs').is_file():
    raise SystemExit('Cannot find Native workflow fixtures in the selected source revision.')
native_model = Path('server/essentials/src/Workflows/Runtime/Native/Model/Elsa.Workflows.Runtime.Native.Model.csproj')
if not native_model.is_file():
    native_model = Path('prototypes/kernel-model/models/Native/Kernel.Native.csproj')
workflow_actor = Path('server/essentials/src/Workflows/Runtime/WorkflowActor/Elsa.Workflows.Runtime.WorkflowActor.csproj')
if not workflow_actor.is_file():
    workflow_actor = Path('prototypes/kernel-model/src/Kernel.WorkflowActor/Kernel.WorkflowActor.csproj')
approval = ''
if not (app / 'CorePreview/DirectApprovalWorkflow.cs').is_file():
    approval = f'<Compile Include="../../{native_runner}/DirectApprovalWorkflow.cs" Link="Qualification/DirectApprovalWorkflow.cs" />'
project_references = (
    'server/essentials/src/ActorFoundation/Hosting/Elsa.ActorFoundation.Hosting.csproj',
    postgres_host,
    native_model,
    workflow_actor,
)
references = '\n'.join(
    f'    <ProjectReference Include="../../{reference}" />'
    for reference in project_references if f'Include="../../{reference}"' not in text
)
project.write_text(text.replace('</Project>', f'''
  <ItemGroup>
{references}
    <Compile Include="../../tools/ci/actor-foundation/WorkbenchPostgresPreview.cs" Link="Qualification/WorkbenchPostgresPreview.cs" />
    {approval}
    <Compile Include="../../prototypes/kernel-model/ef/Kernel.Ef.Run/Wire.cs" Link="Qualification/Wire.cs" />
    <Compile Include="../../{native_runner}/PostgresFixtureDatabase.cs" Link="Qualification/PostgresFixtureDatabase.cs" />
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
# The composition is temporary. Restore the provider's committed closure first.
dotnet restore "$provider_project" --locked-mode
dotnet restore server/apps/Elsa.Workbench.Host /p:RestoreLockedMode=false
git -C "$repo" -c core.fsmonitor=false show "$revision:$provider_dir/packages.lock.json" |
  cmp - "$provider_dir/packages.lock.json"
dotnet build -c Release --no-restore server/apps/Elsa.Workbench.Host
workbench=server/apps/Elsa.Workbench.Host/bin/Release/net10.0/Elsa.Workbench.Host.dll

python3 - "$provider_dir" <<'PYDEPS'
import json
from pathlib import Path
import sys

expected = json.loads(Path('expected-workbench-providers.json').read_text())
manifest = Path('server/apps/Elsa.Workbench.Host/bin/Release/net10.0/Elsa.Workbench.Host.deps.json')
compile_assets = Path(sys.argv[1]) / 'obj/project.assets.json'
actual = dict(key.rsplit('/', 1) for key in json.loads(manifest.read_text())['libraries'])
if 'Kernel.Foundation.InMemory' in actual:
    raise SystemExit('The PostgreSQL preview must not load the in-memory ingress assembly.')
print('ok PostgreSQL preview uses the durable actor adapter without the in-memory ingress assembly')
compile_entries = json.loads(compile_assets.read_text())['targets']['net10.0']
compiled = dict(key.rsplit('/', 1) for key, entry in compile_entries.items() if entry['type'] == 'package')
locked_provider = expected['provider']
for package in sorted(locked_provider.keys() | compiled.keys()):
    if compiled.get(package) != locked_provider.get(package):
        raise SystemExit(f'Provider compile graph changed for {package}: locked {locked_provider.get(package)}, '
                         f'compiled {compiled.get(package)}')
print(f'ok all {len(compiled)} provider compile packages match the committed provider lock')

locked_workbench = expected['workbench']
shared_packages = locked_provider.keys() & locked_workbench.keys() & actual.keys()
for package in sorted(shared_packages):
    if actual[package] != locked_workbench[package]:
        raise SystemExit(f'Workbench runtime graph changed for {package}: locked {locked_workbench[package]}, '
                         f'emitted {actual[package]}')
print(f'ok all {len(shared_packages)} emitted shared packages match the original Workbench lock')

runtime_required = {'Microsoft.EntityFrameworkCore', 'Microsoft.EntityFrameworkCore.Relational',
                    'Npgsql.EntityFrameworkCore.PostgreSQL', 'Npgsql'}
if runtime_required - set(expected['aligned']):
    raise SystemExit('Workbench and the provider must lock the PostgreSQL runtime dependencies.')
if runtime_required - actual.keys():
    raise SystemExit('Workbench must emit the PostgreSQL runtime dependencies.')
for package in expected['aligned']:
    print(f'ok matching provider dependency {package} {compiled[package]} (runtime {actual.get(package, "none")})')
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
  state_counts() {
    docker exec "$container" psql -XAtq -U postgres -d workbench_preview \
      -c 'SELECT (SELECT count(*) FROM "Nodes"), (SELECT count(*) FROM "Messages")'
  }
  expect_rejected_receipt() {
    local field=$1 diagnostic=$2 invalid="$scratch/pg$major-invalid-receipt.json" before
    python3 - "$receipt" "$invalid" "$field" <<'PYRECEIPT'
import json, sys
from pathlib import Path

receipt = json.loads(Path(sys.argv[1]).read_text())
receipt[sys.argv[3]] = "changed"
Path(sys.argv[2]).write_text(json.dumps(receipt))
PYRECEIPT
    before=$(state_counts)
    if dotnet "$workbench" --qualify-workbench-postgres restart "$invalid" > "$scratch/refusal.log" 2>&1; then
      echo "Restart accepted a receipt with an invalid $field." >&2
      exit 1
    fi
    grep -Fq "$diagnostic" "$scratch/refusal.log"
    [[ "$(state_counts)" == "$before" ]]
    echo "ok invalid $field refused without registering an owner or writing new input"
  }
  expect_rejected_receipt StateHash 'the receipt matches the stopped waiting actor before owner startup'
  expect_rejected_receipt Frame 'the receipt matches the frame and journal before owner startup'
  expect_rejected_receipt JournalHash 'the receipt matches the frame and journal before owner startup'
  expect_rejected_receipt Bookmark 'The continuation does not match this actor journal'
  dotnet "$workbench" --qualify-workbench-postgres restart "$receipt"
  docker stop --time 3 "$container" >/dev/null
  container=
done
