#!/usr/bin/env bash
set -euo pipefail

elsa_commit="ca818b649d85c5167e2222c0ec534e215153d473"
elsa_tree="82433ec3170e88244f9d139b35c4b7c6e13225d5"
groundwork_version="0.0.1-preview.95"
groundwork_commit="d297147e0cd6b018d70b1f7d61fef771e32b022f"
evidence_time="2026-07-30T00:00:00Z"
packages=(
  Groundwork.Core
  Groundwork.DiagnosticRecords
  Groundwork.Documents
  Groundwork.MongoDb
  Groundwork.PostgreSql
  Groundwork.Sqlite
  Groundwork.SqlServer
)

script_directory="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repository_root="$(git -C "$script_directory" rev-parse --show-toplevel)"
fixture_directory="$repository_root/tests/Elsa/Persistence/Groundwork/UnifiedHost/Tests/Fixtures/schema-evolution/preview.95"
harness_source="$script_directory/Preview95SchemaFixtureReproducer.cs"
harness_project="$script_directory/Preview95SchemaFixtureReproducer.csproj"
temporary_root="$(mktemp -d "${TMPDIR:-/tmp}/elsa-preview95-replay.XXXXXX")"
detached_tree="$temporary_root/source"
generated_directory="$temporary_root/generated"
detached_harness="$detached_tree/tools/Preview95SchemaFixtureReproducer"

cleanup() {
  git -C "$repository_root" worktree remove --force "$detached_tree" >/dev/null 2>&1 || true
  rm -rf "$temporary_root"
}
trap cleanup EXIT

actual_tree="$(git -C "$repository_root" show -s --format=%T "$elsa_commit")"
if [[ "$actual_tree" != "$elsa_tree" ]]; then
  echo "Elsa commit $elsa_commit resolved to tree $actual_tree, expected $elsa_tree." >&2
  exit 1
fi

package_props="$(git -C "$repository_root" show "$elsa_commit:Directory.Packages.props")"
for package in "${packages[@]}"; do
  expected_pin="PackageVersion Include=\"$package\" Version=\"$groundwork_version\""
  if ! grep -Fq "$expected_pin" <<<"$package_props"; then
    echo "$package is not pinned to $groundwork_version at $elsa_commit." >&2
    exit 1
  fi
done

tool_manifest="$(git -C "$repository_root" show "$elsa_commit:.config/dotnet-tools.json")"
if ! grep -Fq "\"version\": \"$groundwork_version\"" <<<"$tool_manifest"; then
  echo "Groundwork.Tool is not pinned to $groundwork_version at $elsa_commit." >&2
  exit 1
fi

git -C "$repository_root" worktree add --detach "$detached_tree" "$elsa_commit"
mkdir -p "$detached_harness"
cp "$harness_source" "$detached_harness/Program.cs"
cp "$harness_project" "$detached_harness/Preview95SchemaFixtureReproducer.csproj"

dotnet restore "$detached_harness/Preview95SchemaFixtureReproducer.csproj" --force-evaluate
dotnet tool restore --tool-manifest "$detached_tree/.config/dotnet-tools.json"

global_packages="$(dotnet nuget locals global-packages --list | sed 's/^global-packages: //')"
for package in "${packages[@]}" Groundwork.Tool; do
  package_directory="$global_packages/$(tr '[:upper:]' '[:lower:]' <<<"$package")/$groundwork_version"
  nuspec="$(find "$package_directory" -maxdepth 1 -name '*.nuspec' -print -quit)"
  if [[ -z "$nuspec" ]] || ! grep -Fq "commit=\"$groundwork_commit\"" "$nuspec"; then
    echo "$package $groundwork_version is not bound to Groundwork commit $groundwork_commit." >&2
    exit 1
  fi
done

mkdir -p "$generated_directory"
dotnet run \
  --project "$detached_harness/Preview95SchemaFixtureReproducer.csproj" \
  --no-restore \
  -- "$generated_directory"

for provider in sqlite sql-server postgresql mongodb; do
  committed="$fixture_directory/$provider-applied-state.json.gz"
  generated="$generated_directory/$provider-applied-state.json"
  committed_digest="$(gzip -dc "$committed" | shasum -a 256 | awk '{print $1}')"
  generated_digest="$(shasum -a 256 "$generated" | awk '{print $1}')"
  if [[ "$committed_digest" != "$generated_digest" ]]; then
    echo "$provider canonical fixture mismatch: committed=$committed_digest generated=$generated_digest" >&2
    exit 1
  fi
  echo "$provider canonical fixture verified: $generated_digest"
done

echo "Verified Elsa $elsa_commit ($elsa_tree), Groundwork $groundwork_version ($groundwork_commit), timestamp $evidence_time."
