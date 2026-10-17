#!/usr/bin/env bash
set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd "$script_dir/../../.." && pwd)"
host_project="$script_dir/UnifiedExecution.RuleModelProof.csproj"
engine_project="$script_dir/RuleEngine/UnifiedExecution.FiniteRules.csproj"
swap_project="$script_dir/SwapPackage/UnifiedExecution.FiniteRules.Swap.csproj"
package_dir="$(mktemp -d "${TMPDIR:-/tmp}/elsa-finite-rules-package.XXXXXX")"
package_install_dir="$script_dir/bin/Debug/net10.0/Packages/FiniteRules/1.0.0"
trap 'rm -rf "$package_dir" "$package_install_dir"' EXIT

cd "$repo_root"
dotnet restore "$host_project" --locked-mode
dotnet restore "$engine_project" --locked-mode
dotnet build "$engine_project" --no-restore
dotnet build "$host_project" --no-restore
dotnet pack "$engine_project" --configuration Debug --no-build --no-restore --output "$package_dir"
nupkg_path="$(find "$package_dir" -maxdepth 1 -name '*.nupkg' -print -quit)"
test -n "$nupkg_path"
mkdir -p "$package_install_dir/lib/net10.0"
python3 - "$nupkg_path" "$package_install_dir/lib/net10.0/UnifiedExecution.FiniteRules.dll" <<'EXTRACT'
import sys,zipfile
from pathlib import Path
package=Path(sys.argv[1]); destination=Path(sys.argv[2])
with zipfile.ZipFile(package) as archive:
    destination.write_bytes(archive.read('lib/net10.0/UnifiedExecution.FiniteRules.dll'))
EXTRACT
# Mint a pin for this build before creating any execution. Existing execution pins
# are never refreshed by the runtime resolver; changed builds must fail on resume.
python3 - "$script_dir" <<'PIN'
import hashlib,json,sys
from pathlib import Path
root=Path(sys.argv[1]); output=root/'bin/Debug/net10.0'
manifest=json.loads((root/'Assets/model-manifest.json').read_text())
package_assembly=output/'Packages/FiniteRules/1.0.0/lib/net10.0/UnifiedExecution.FiniteRules.dll'
manifest['packageSha256']=hashlib.sha256(package_assembly.read_bytes()).hexdigest()
(output/'Assets/model-manifest.json').write_text(json.dumps(manifest,indent=2)+'\n')
PIN
cp "$script_dir/bin/Debug/net10.0/Assets/model-manifest.json" "$script_dir/evidence/runtime-manifest.json"
dotnet restore "$swap_project" --locked-mode
dotnet build "$swap_project" --no-restore --output "$package_dir/swap"
export ELSA_RULE_MODEL_SWAP_PACKAGE="$package_dir/swap/UnifiedExecution.FiniteRules.Swap.dll"
export ELSA_RULE_MODEL_NUPKG="$nupkg_path"
export ELSA_RULE_MODEL_EVIDENCE="$script_dir/evidence/proof.json"
dotnet "$script_dir/bin/Debug/net10.0/UnifiedExecution.RuleModelProof.dll" proof
python3 - "$script_dir" <<'HASHES'
import hashlib,json,sys
from pathlib import Path
root=Path(sys.argv[1]); manifest_path=root/'evidence/source-sha256.json'
manifest=json.loads(manifest_path.read_text())
for relative in manifest:
    manifest[relative]=hashlib.sha256((root/relative).read_bytes()).hexdigest()
manifest_path.write_text(json.dumps(manifest,indent=2)+'\n')
HASHES
