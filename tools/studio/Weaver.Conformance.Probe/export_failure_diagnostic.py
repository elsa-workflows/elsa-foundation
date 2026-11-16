#!/usr/bin/env python3
"""Export only a bounded failed diagnostic; never replace full witness export or acceptance."""
from __future__ import annotations

import argparse
import json
from pathlib import Path
import re
import tempfile

from evidence import witness_manifest
from export_evidence import export_evidence
from journey_runtime import safe_browser_failure


def export_failure_diagnostic(source, destination):
    source, destination = Path(source).resolve(), Path(destination)
    if not (source.is_dir() and destination.is_absolute() and not destination.exists()
            and not destination.resolve().is_relative_to(source)):
        raise ValueError("Use a new absolute diagnostic export directory outside the run")
    report = json.loads((source / "report.json").read_text())
    if not isinstance(report, dict) or report.get("status") != "failed":
        raise ValueError("Only a failed run can produce failure diagnostics")
    commit = report.get("source")
    if not isinstance(commit, str) or not re.fullmatch(r"[0-9a-f]{40}", commit):
        raise ValueError("Failure diagnostics require an exact committed source")
    manifest = witness_manifest()
    allowed_stages = {"browser-" + case for name, cases in manifest.items()
                      if name.endswith("Cases") for case in cases}
    allowed_stages.update(manifest["autonomyStages"])
    allowed_stages.update(case + "-browser" for case in manifest["workflowRunCases"])
    stage = report.get("stage")
    cases = report.get("cases")
    count = len(cases) if isinstance(cases, list) and len(cases) <= 1000000 else None
    diagnostic = {
        "status": "failed", "diagnosticOnly": True, "qualificationComplete": False,
        "independentPackageWitness": False, "source": commit,
        "stage": stage if isinstance(stage, str) and stage in allowed_stages else "Unavailable",
        "originalCaseCount": count, "expectedCaseCount": manifest["expectedCaseCount"],
        "browserFailureDiagnostic": safe_browser_failure({"failureDiagnostic": report.get("browserFailureDiagnostic")}),
    }
    # No original report graph or file reference enters this separately owned source.
    # The ordinary exporter still applies all of its privacy/hash/reference guards.
    with tempfile.TemporaryDirectory(prefix="weaver-failure-diagnostic-") as temporary:
        root = Path(temporary)
        minimal = root / "report.json"
        minimal.write_text(json.dumps(diagnostic, indent=2) + "\n")
        minimal.chmod(0o600)
        return export_evidence(root, destination)


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source", type=Path, required=True)
    parser.add_argument("--destination", type=Path, required=True)
    options = parser.parse_args()
    result = export_failure_diagnostic(options.source, options.destination)
    print(json.dumps({"status": "failure-diagnostic-exported", "qualificationStatus": result["qualificationStatus"],
                      "independentPackageWitness": False, "files": len(result["files"])}))
