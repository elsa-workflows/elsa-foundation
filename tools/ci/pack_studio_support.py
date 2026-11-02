#!/usr/bin/env python3
"""Pack the actual mandatory support closure used by Studio consumer qualification."""
import argparse
import json
from pathlib import Path
import subprocess

REPO = Path(__file__).resolve().parents[2]

if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--version", required=True)
    parser.add_argument("--source", required=True)
    args = parser.parse_args()
    for item in json.loads((REPO / "tools/ci/studio-package-identities.json").read_text())["support_projects"]:
        subprocess.run(["dotnet", "pack", item["project"], "-c", "Release", "-p:RestoreLockedMode=true",
                        "-p:Version=" + args.version, "-p:SourceRevisionId=" + args.source,
                        "-o", str(args.output.resolve())], cwd=REPO, check=True)
