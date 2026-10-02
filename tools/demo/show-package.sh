#!/usr/bin/env bash
# Opens a staged release of the Notes module for the screen, to show that what the host installs is an ordinary NuGet package:
# its id, version, description and dependencies from the .nuspec, the files inside it, and the module's own nuplane.json.
set -euo pipefail
source "$(dirname "${BASH_SOURCE[0]}")/lib.sh"

usage() {
  cat <<'USAGE'
Usage: bash tools/demo/show-package.sh <1|2>

  1   release 1.0.0 of Elsa.Samples.Nuplane.Notes
  2   release 1.1.0

Reads artifacts/demo/staging/N/Elsa.Samples.Nuplane.Notes.*.nupkg, which tools/demo/prepack.sh staged, and prints what is in it.
Read-only, and nothing is extracted.
USAGE
}

case "${1:-}" in
  1|2) ;;
  -h|--help) usage; exit 0 ;;
  *) demo_fail "Give the release to show, 1 or 2: bash tools/demo/show-package.sh <1|2> (see --help)." ;;
esac
[[ $# -eq 1 ]] || demo_fail "unexpected argument '$2' (see --help)."
package="$(demo_staged_package "$1")" || exit 1
[[ -f "$package" ]] || demo_fail "Release $1 is not staged (${package#"$demo_root"/} is missing). Run: bash tools/demo/prepack.sh"

demo_require_python
python3 - "$package" <<'PY'
import json, re, sys, textwrap, xml.etree.ElementTree as xml, zipfile

with zipfile.ZipFile(sys.argv[1]) as package:
    names = sorted(name for name in package.namelist() if not re.match(r"(\[Content_Types\]\.xml|_rels/|package/)", name))
    nuspec = xml.fromstring(package.read(next(name for name in names if name.endswith(".nuspec"))))
    nuplane = next((name for name in names if name.rsplit("/", 1)[-1] == "nuplane.json"), None)
    declaration = json.dumps(json.loads(package.read(nuplane)), indent=2) if nuplane else None


def metadata(tag):
    element = nuspec.find(f"./{{*}}metadata/{{*}}{tag}")
    return (element.text or "").strip() if element is not None else ""


print(f"{metadata('id')} {metadata('version')}")
print(textwrap.fill(metadata("description"), 100, initial_indent="  ", subsequent_indent="  "))
for group in nuspec.iterfind("./{*}metadata/{*}dependencies/{*}group"):
    print()
    print(f"depends on ({group.get('targetFramework') or 'any framework'}):")
    for dependency in group.iterfind("./{*}dependency"):
        print(f"  {dependency.get('id')} {dependency.get('version')}")
print()
print("files:")
for name in names:
    print(f"  {name}")
print()
if declaration:
    print(f"{nuplane}:")
    print(declaration)
PY
