#!/usr/bin/env bash
set -euo pipefail

repo_root="$(git rev-parse --show-toplevel)"
baseline="b555bd8460270476e49496e519c69b65180e9505"
destination="${1:-$repo_root/Extensions/Etl/tests/Shared/Fixtures/V1/data}"
baseline_paths=(
    ':(glob)**/src/**'
    ':(glob)**/packages.lock.json'
    ':(glob)**/*.props'
    ':(glob)**/*.targets'
    'global.json'
    'NuGet.Config'
)

git -C "$repo_root" cat-file -e "$baseline^{commit}"
if ! git -C "$repo_root" diff --quiet "$baseline" -- "${baseline_paths[@]}"; then
    echo "Refusing V1 fixture capture: tracked source or dependency/build inputs differ from $baseline." >&2
    exit 1
fi
if [[ -e "$destination" ]]; then
    echo "Refusing to overwrite existing fixture destination: $destination" >&2
    exit 1
fi

mkdir -p "$(dirname "$destination")"
ETL_V1_FIXTURE_CAPTURE_ROOT="$destination" \
ETL_V1_FIXTURE_CAPTURE_BASE_COMMIT="$(git -C "$repo_root" rev-parse HEAD)" \
dotnet test "$repo_root/Extensions/Etl/tests/Tests/Elsa.Etl.Tests.csproj" \
    -p:EtlV1FixtureCapture=true \
    --filter FullyQualifiedName~CaptureAuthenticPreChangeV1Fixture \
    --logger 'console;verbosity=minimal'
