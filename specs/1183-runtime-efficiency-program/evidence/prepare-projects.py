"""Materialize only the frozen manual qualification projects explicitly requested by the operator."""
from pathlib import Path
import sys
root = Path(__file__).resolve().parent
known = {"bpmn-qualification", "diagnostics-attribution", "etl-qualification", "native-qualification", "retained-boundaries", "timer-qualification"}
requested = sys.argv[1:]
if not requested or any(name not in known for name in requested):
    raise SystemExit("Pass one or more qualification directory names; see evidence/README.md.")
for name in requested:
    directory = root / name
    for template in [*directory.glob("*.csproj.template"), directory / "packages.lock.json.template"]:
        target = template.with_suffix("")
        data = template.read_bytes()
        if target.exists():
            if target.read_bytes() != data:
                raise SystemExit(f"Preserve the differing local file before preparing {target}.")
        else:
            with target.open("xb") as output:
                output.write(data)
    print(f"Prepared {name}; build/run it explicitly as described in its README.")
