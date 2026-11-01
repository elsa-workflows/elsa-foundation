#!/usr/bin/env python3
"""Qualify independent Weaver distributions using real owned package consumers.

This is the only complete package witness. One fresh run and host pair must observe the
original 38 cases, the four current-authority cases and every #3440 autonomy case once on
each actual adapter (evidence.witness_manifest()); only then is independentPackageWitness set.
qualify_autonomy.py remains a non-qualifying diagnostic.
"""
from __future__ import annotations

import argparse
import json
from pathlib import Path
import re


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", required=True, type=Path, help="New absolute owned directory outside the checkout")
    parser.add_argument("--version", default="4.0.0-weaver3438", help="Unique local candidate version; never published")
    options = parser.parse_args()
    if not options.output.is_absolute():
        parser.error("--output must be absolute")
    if not re.fullmatch(r"\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?", options.version):
        parser.error("--version must be a semantic version")
    from artifacts import ConformanceRun
    try:
        ConformanceRun(options.output.resolve(), options.version).execute()
    finally:
        summarize(options.output.resolve())


def summarize(output):
    """Print the retained outcome against the complete manifest, including each failed autonomy cause."""
    from autonomy_journeys import MATRIX_QUALIFICATION
    from evidence import witness_gaps, witness_manifest
    path = output / "report.json"
    report = json.loads(path.read_text()) if path.is_file() else {}
    autonomy = report.get(MATRIX_QUALIFICATION, {}).get("cases", [])
    print(json.dumps({"status": report.get("status"), "source": report.get("source"),
                      "independentPackageWitness": report.get("independentPackageWitness") is True,
                      "expectedCases": witness_manifest()["expectedCaseCount"],
                      "originalCases": len(report.get("cases", [])),
                      "currentAuthorityCases": len(report.get("contextDisclosureQualification", {}).get("records", [])),
                      "autonomyCases": len(autonomy), "autonomyPassed": sum(row.get("status") == "passed" for row in autonomy),
                      "autonomyFailed": {row.get("id"): row.get("cause") for row in autonomy if row.get("status") != "passed"},
                      "gaps": witness_gaps(report)[:20] if report else ["No report"], "report": str(path)}))


if __name__ == "__main__":
    main()
