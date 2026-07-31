#!/usr/bin/env bash
set -euo pipefail

elsa_commit="ca818b649d85c5167e2222c0ec534e215153d473"
elsa_tree="82433ec3170e88244f9d139b35c4b7c6e13225d5"
groundwork_version="0.0.1-preview.95"
groundwork_commit="d297147e0cd6b018d70b1f7d61fef771e32b022f"
evidence_time="2026-07-30T00:00:00Z"
linux_noble_image="mcr.microsoft.com/dotnet/sdk:10.0-noble@sha256:ed034a8bf0b24ded0cbbac07e17825d8e9ebfe21e308191d0f7421eaf5ad4664"
darwin_unicode_algorithm="groundwork-unicode-ordinal-ignore-case-v1-3206f759667cb9cc764ec243dfb3d322a39970184efab619e80163c36d86818f"
linux_noble_unicode_algorithm="groundwork-unicode-ordinal-ignore-case-v1-124ca0d0d2b045d7be0e6aea8f07f74fbc0428a13c53a47d8a7d41db71b5ec5f"
packages=(
  Groundwork.Core
  Groundwork.DiagnosticRecords
  Groundwork.Documents
  Groundwork.MongoDb
  Groundwork.PostgreSql
  Groundwork.Sqlite
  Groundwork.SqlServer
)

runtime="host"
if [[ $# -gt 0 ]]; then
  if [[ $# -ne 2 || "$1" != "--runtime" || "$2" != "linux-noble" ]]; then
    echo "Usage: $0 [--runtime linux-noble]" >&2
    exit 2
  fi
  runtime="$2"
fi

script_directory="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repository_root="$(git -C "$script_directory" rev-parse --show-toplevel)"
fixture_directory="$repository_root/tests/Elsa/Persistence/Groundwork/UnifiedHost/Tests/Fixtures/schema-evolution/preview.95"
harness_source="$script_directory/Preview95SchemaFixtureReproducer.cs"
harness_project="$script_directory/Preview95SchemaFixtureReproducer.csproj"

if [[ -n "$(git -C "$repository_root" status --porcelain=v1 --untracked-files=all)" ]]; then
  echo "Refusing fixture replay from a dirty candidate worktree." >&2
  exit 1
fi

temporary_root="$(mktemp -d "${TMPDIR:-/tmp}/elsa-preview95-replay.XXXXXX")"
detached_tree="$temporary_root/source"
generated_directory="$temporary_root/generated"
detached_harness="$detached_tree/tools/Preview95SchemaFixtureReproducer"
isolated_nuget="$temporary_root/nuget"
isolated_cli_home="$temporary_root/dotnet-home"

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

mkdir -p "$generated_directory" "$isolated_nuget" "$isolated_cli_home"
if [[ "$runtime" == "host" ]]; then
  DOTNET_CLI_HOME="$isolated_cli_home" NUGET_PACKAGES="$isolated_nuget" \
    dotnet restore "$detached_harness/Preview95SchemaFixtureReproducer.csproj" --force-evaluate --no-http-cache
  DOTNET_CLI_HOME="$isolated_cli_home" NUGET_PACKAGES="$isolated_nuget" \
    dotnet tool restore --tool-manifest "$detached_tree/.config/dotnet-tools.json" --no-http-cache
  DOTNET_CLI_HOME="$isolated_cli_home" NUGET_PACKAGES="$isolated_nuget" \
    dotnet run \
    --project "$detached_harness/Preview95SchemaFixtureReproducer.csproj" \
    --no-restore \
    -- "$generated_directory"
  global_packages="$isolated_nuget"
else
  docker run --rm \
    --user "$(id -u):$(id -g)" \
    -e DOTNET_CLI_HOME=/tmp/dotnet-home \
    -e NUGET_PACKAGES=/tmp/nuget \
    -v "$detached_tree:/src" \
    -v "$generated_directory:/output" \
    -v "$isolated_nuget:/tmp/nuget" \
    -v "$isolated_cli_home:/tmp/dotnet-home" \
    -w /src \
    "$linux_noble_image" \
    bash -lc \
    'dotnet restore tools/Preview95SchemaFixtureReproducer/Preview95SchemaFixtureReproducer.csproj --force-evaluate --no-http-cache &&
     dotnet tool restore --tool-manifest .config/dotnet-tools.json --no-http-cache &&
     dotnet run --project tools/Preview95SchemaFixtureReproducer/Preview95SchemaFixtureReproducer.csproj --no-restore -- /output'
  global_packages="$isolated_nuget"
fi

for package in "${packages[@]}" Groundwork.Tool; do
  package_directory="$global_packages/$(tr '[:upper:]' '[:lower:]' <<<"$package")/$groundwork_version"
  nuspec="$(find "$package_directory" -maxdepth 1 -name '*.nuspec' -print -quit)"
  if [[ -z "$nuspec" ]] || ! grep -Fq "commit=\"$groundwork_commit\"" "$nuspec"; then
    echo "$package $groundwork_version is not bound to Groundwork commit $groundwork_commit." >&2
    exit 1
  fi
done

unicode_algorithm="$(
  jq -r \
    '.snapshot.routes[].identitySchema
     | select(.stringCasePolicy == "UnicodeOrdinalIgnoreCase")
     | .comparisonAlgorithm' \
    "$generated_directory/sqlite-applied-state.json" \
    | sort -u
)"
if [[ "$runtime" == "linux-noble" && "$unicode_algorithm" != "$linux_noble_unicode_algorithm" ]]; then
  echo "Pinned Ubuntu Noble emitted unexpected Unicode identity algorithm '$unicode_algorithm'." >&2
  exit 1
fi

case "$unicode_algorithm" in
  "$darwin_unicode_algorithm")
    selected_fixture_directory="$fixture_directory"
    ;;
  "$linux_noble_unicode_algorithm")
    selected_fixture_directory="$fixture_directory/unicode-identity-124ca0d0d2b0"
    ;;
  *)
    echo "No committed fixture family matches Unicode identity algorithm '$unicode_algorithm'." >&2
    exit 1
    ;;
esac

for provider in sqlite sql-server postgresql mongodb; do
  committed="$selected_fixture_directory/$provider-applied-state.json.gz"
  generated="$generated_directory/$provider-applied-state.json"
  committed_digest="$(gzip -dc "$committed" | shasum -a 256 | awk '{print $1}')"
  generated_digest="$(shasum -a 256 "$generated" | awk '{print $1}')"
  if [[ "$committed_digest" != "$generated_digest" ]]; then
    echo "$provider canonical fixture mismatch: committed=$committed_digest generated=$generated_digest" >&2
    exit 1
  fi
  echo "$provider canonical fixture verified: $generated_digest"
done

echo "Verified runtime $runtime with Unicode identity algorithm $unicode_algorithm."
echo "Verified Elsa $elsa_commit ($elsa_tree), Groundwork $groundwork_version ($groundwork_commit), timestamp $evidence_time."
