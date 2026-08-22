#!/usr/bin/env bash
set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd -P)"
repo_root="$(cd "$script_dir/../.." && pwd -P)"
cd "$repo_root"

dotnet restore Elsa.Server.slnx "$@"

# Historical capture executables deliberately stay out of the product solution, but repository-wide
# architecture ratchets inspect every project.assets.json. Restore those evidence projects explicitly
# without adding them to the product build or the container-free test filter.
while IFS= read -r project; do
  dotnet restore "$project" "$@"
done < <(find tests -type f -path '*/Capture/*.BeforeCapture.csproj' | LC_ALL=C sort)
