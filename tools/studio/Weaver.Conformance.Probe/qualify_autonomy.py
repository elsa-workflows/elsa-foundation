#!/usr/bin/env python3
"""Qualify #3440 established-session autonomy through fresh independent packages/hosts.

--scope modes (default) runs the eight T012 mode cases. --scope matrix runs the full T015
matrix: all thirteen lifecycle cases once on each actual adapter, continuing after a failed
case, and succeeds only with all twenty-six passed. Neither scope executes or qualifies the
original38+4 package witness or sets qualified/independentPackageWitness. An outer owner
bounds production/startup/cleanup time. The complete witness (qualify.py) reuses the same
case runners after the original journeys in one host pair; this tool stays diagnostic.
"""
from __future__ import annotations

import argparse
import json
from pathlib import Path
import re
import secrets


SCOPES = {"modes": "autonomy-mode-qualification", "matrix": "autonomy-matrix-qualification"}


def qualify(output, version, scope="modes"):
    from artifacts import ConformanceRun
    from authors import build_authors
    from autonomy_journeys import qualify_autonomy_matrix, qualify_autonomy_modes
    from case_records import CaseRecords
    from consumers import prepare_consumers, configure_runtime, freeze_consumer_baseline, capture_consumer_after
    from journey_runtime import JourneyRuntime
    from journeys import _seed_principals, _bootstrap, _install, EXPECTED_CONTROLS
    from runtime import owned_origin, private_json, server

    status = SCOPES[scope]
    run = ConformanceRun(output, version)
    try:
        run.assert_immutable_inputs()
        run.produce()
        build_authors(run)
        prepare_consumers(run)
        run.assert_immutable_inputs()
        run.report.update(status=status + "-running", independentPackageWitness=False, liveProvider=False)
        run.save()
        admin = {"username": "weaver-" + secrets.token_hex(10), "password": secrets.token_urlsafe(40) + "!aA1"}
        backend, studio = owned_origin(), owned_origin()
        configure_runtime(run, backend_url=backend, studio_url=studio, admin_username=admin["username"], admin_password=admin["password"], provider="copilot", enroll=True)
        principals = _seed_principals(run)
        runtime = JourneyRuntime(run, backend, studio, admin, principals)
        shells_path = run.profiles["backend"]["published"] / "shells.json"
        shells = json.loads(shells_path.read_text())
        shells["CShells"]["Shells"]["Authoring"]["Features"]["AcmeCopilotProtocolFixture"]["GateFile"] = str(runtime.gate_file)
        private_json(shells_path, shells)
        private_json(runtime.root / "private-seeds.json", {"admin": admin, "principals": principals})
        setup = CaseRecords(run, EXPECTED_CONTROLS)
        with server(run, "backend", backend), server(run, "studio", studio):
            _bootstrap(runtime)
            freeze_consumer_baseline(run)
            _install(runtime, setup)
            (qualify_autonomy_matrix if scope == "matrix" else qualify_autonomy_modes)(runtime)
            capture_consumer_after(run)
            run.assert_immutable_inputs()
        run.assert_immutable_inputs()
        run.report.update(status=status + "-passed", independentPackageWitness=False)
        run.save()
    except BaseException as error:
        run.report.update(status=status + "-failed", independentPackageWitness=False,
                          qualificationFailure={"type": type(error).__name__})
        run.save()
        raise
    return run.report


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", required=True, type=Path, help="NEW absolute privately owned output directory")
    parser.add_argument("--version", required=True, help="Unique local candidate semantic version; never published")
    parser.add_argument("--scope", choices=sorted(SCOPES), default="modes", help="modes: T012 eight cases; matrix: T015 twenty-six cases")
    args = parser.parse_args()
    if not args.output.is_absolute(): parser.error("--output must be absolute")
    if not re.fullmatch(r"\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?", args.version): parser.error("Use a candidate semantic version")
    try:
        qualify(args.output.resolve(), args.version, args.scope)
    finally:
        summarize(args.output.resolve(), args.scope)


def summarize(output, scope="modes"):
    # Print the retained outcome, including each failed case's precise cause, on success or failure.
    from autonomy_journeys import MATRIX_QUALIFICATION, MODE_QUALIFICATION
    path = output / "report.json"
    report = json.loads(path.read_text()) if path.is_file() else {}
    records = report.get(MATRIX_QUALIFICATION if scope == "matrix" else MODE_QUALIFICATION, {}).get("cases", [])
    print(json.dumps({"status": report.get("status"), "source": report.get("source"), "qualified": False,
                      "independentPackageWitness": False, "observations": len(records),
                      "passed": sum(row.get("status") == "passed" for row in records),
                      "failed": {row["id"]: row.get("cause") for row in records if row.get("status") != "passed"},
                      "report": str(path)}))


if __name__ == "__main__":
    main()
