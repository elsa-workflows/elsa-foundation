#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../../../../.." && pwd)"
consumer_root="$repo_root/tests/Elsa/Diagnostics/Persistence/Groundwork/V2/Consumer"
feed="${GROUNDWORK_E2_V2_PACKAGES:-$repo_root/artifacts/packages}"
groundwork_version="${GROUNDWORK_E2_V2_VERSION:-0.4.0-preview.5}"
test -d "$feed" || {
  echo "Missing packed Groundwork packages at '$feed'. Set GROUNDWORK_E2_V2_PACKAGES or pack Groundwork first." >&2
  exit 1
}

for required in Groundwork.Kernel Groundwork.Query.Model Groundwork.Store Groundwork.Sqlite; do
  test -f "$feed/$required.$groundwork_version.nupkg" || {
    echo "The local feed is missing $required.$groundwork_version.nupkg." >&2
    exit 1
  }
done

if grep -REn '<ProjectReference|Groundwork\.Testing|TestingAdapter|InternalsVisibleTo|System\.Reflection|\.\./.*src' "$consumer_root" --include='*.cs' --include='*.csproj'; then
  echo "The E2 v2 consumer contains a forbidden internal, test, or source dependency." >&2
  exit 1
fi

package_cache="$(mktemp -d)"
build_root="$(mktemp -d)"
trap 'rm -rf "$package_cache" "$build_root"' EXIT

external_root="$build_root/consumer"
mkdir -p "$external_root/feed"
cp "$consumer_root/Elsa.Diagnostics.Persistence.Groundwork.V2.Consumer.csproj" "$external_root/"
cp "$consumer_root/Program.cs" "$external_root/"
cp "$feed"/Groundwork.*.nupkg "$external_root/feed/"
cat >"$external_root/NuGet.Config" <<'EOF'
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="groundwork-local" value="./feed" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" protocolVersion="3" />
  </packageSources>
  <packageSourceMapping>
    <packageSource key="groundwork-local">
      <package pattern="Groundwork.*" />
    </packageSource>
    <packageSource key="nuget.org">
      <package pattern="*" />
    </packageSource>
  </packageSourceMapping>
</configuration>
EOF

isolation_args=(
  -p:ImportDirectoryBuildProps=false
  -p:ImportDirectoryBuildTargets=false
  -p:ManagePackageVersionsCentrally=false
  -p:BaseIntermediateOutputPath="$external_root/obj/"
  -p:MSBuildProjectExtensionsPath="$external_root/obj/"
  -p:BaseOutputPath="$external_root/bin/"
)

NUGET_PACKAGES="$package_cache" dotnet restore "$external_root/Elsa.Diagnostics.Persistence.Groundwork.V2.Consumer.csproj" \
  --force --force-evaluate --packages "$package_cache" --nologo \
  -p:RestoreConfigFile="$external_root/NuGet.Config" \
  -p:GroundworkVersion="$groundwork_version" \
  "${isolation_args[@]}" -m:1 -v:q
NUGET_PACKAGES="$package_cache" dotnet run --project "$external_root/Elsa.Diagnostics.Persistence.Groundwork.V2.Consumer.csproj" \
  --configuration Release --no-restore --nologo \
  -p:GroundworkVersion="$groundwork_version" "${isolation_args[@]}"

echo "E2 v2 package-only SQLite proof passed."
