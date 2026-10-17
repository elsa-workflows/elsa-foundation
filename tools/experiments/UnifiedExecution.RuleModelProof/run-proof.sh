#!/usr/bin/env bash
set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd "$script_dir/../../.." && pwd)"
host_project="$script_dir/UnifiedExecution.RuleModelProof.csproj"
engine_project="$script_dir/RuleEngine/UnifiedExecution.FiniteRules.csproj"
swap_project="$script_dir/SwapPackage/UnifiedExecution.FiniteRules.Swap.csproj"
package_dir="$(mktemp -d "${TMPDIR:-/tmp}/elsa-finite-rules-package.XXXXXX")"
trap 'rm -rf "$package_dir"' EXIT

cd "$repo_root"
dotnet restore "$host_project" --locked-mode
dotnet build "$host_project" --no-restore
# Mint a pin for this build before creating any execution. Existing execution pins
# are never refreshed by the runtime resolver; changed builds must fail on resume.
python3 - "$script_dir" <<'PIN'
import hashlib,json,sys
from pathlib import Path
root=Path(sys.argv[1]); output=root/'bin/Debug/net10.0'
manifest=json.loads((root/'Assets/model-manifest.json').read_text())
manifest['packageSha256']=hashlib.sha256((output/'UnifiedExecution.FiniteRules.dll').read_bytes()).hexdigest()
(output/'Assets/model-manifest.json').write_text(json.dumps(manifest,indent=2)+'\n')
PIN
dotnet pack "$engine_project" --configuration Debug --no-build --no-restore --output "$package_dir"
test -n "$(find "$package_dir" -maxdepth 1 -name '*.nupkg' -print -quit)"
dotnet restore "$swap_project" --locked-mode
dotnet build "$swap_project" --no-restore --output "$package_dir/swap"
export ELSA_RULE_MODEL_SWAP_PACKAGE="$package_dir/swap/UnifiedExecution.FiniteRules.Swap.dll"
dotnet "$script_dir/bin/Debug/net10.0/UnifiedExecution.RuleModelProof.dll" proof
