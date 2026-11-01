#!/usr/bin/env python3
"""Run eight bounded mode diagnostics through fresh independent packages/hosts.

This intentionally does NOT execute or qualify the original38+4 package witness.
It never sets independentPackageWitness=True, even when all eight outcomes pass.
An outer owner must bound overall production/startup/cleanup time as documented.
"""
from __future__ import annotations

import argparse
import json
from pathlib import Path
import re
import secrets


def diagnose(output, version):
    from artifacts import ConformanceRun
    from authors import build_authors
    from autonomy_journeys import diagnose_autonomy
    from case_records import CaseRecords
    from consumers import prepare_consumers, configure_runtime, freeze_consumer_baseline, capture_consumer_after
    from journey_runtime import JourneyRuntime
    from journeys import _seed_principals, _bootstrap, _install, EXPECTED_CONTROLS
    from runtime import owned_origin, private_json, server

    run = ConformanceRun(output, version)
    try:
        run.assert_immutable_inputs()
        run.produce()
        build_authors(run)
        prepare_consumers(run)
        run.assert_immutable_inputs()
        run.report.update(status="diagnostic-running", independentPackageWitness=False, liveProvider=False)
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
            diagnose_autonomy(runtime)
            capture_consumer_after(run)
            run.assert_immutable_inputs()
        run.assert_immutable_inputs()
        run.report.update(status="diagnostic-complete", independentPackageWitness=False)
        run.save()
    except BaseException as error:
        run.report.update(status="diagnostic-failed", independentPackageWitness=False,
                          diagnosticFailure={"type": type(error).__name__})
        run.save()
        raise
    return run.report


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", required=True, type=Path, help="NEW absolute privately owned output directory")
    parser.add_argument("--version", required=True, help="Unique local candidate semantic version; never published")
    args = parser.parse_args()
    if not args.output.is_absolute(): parser.error("--output must be absolute")
    if not re.fullmatch(r"\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?", args.version): parser.error("Use a candidate semantic version")
    report = diagnose(args.output.resolve(), args.version)
    records = report["autonomyBaselineDiagnostic"]["cases"]
    print(json.dumps({"status": report["status"], "source": report["source"], "qualified": False,
                      "independentPackageWitness": False, "observations": len(records),
                      "passed": sum(row["status"] == "passed" for row in records),
                      "failed": sum(row["status"] == "failed" for row in records),
                      "report": str(args.output / "report.json")}))


if __name__ == "__main__":
    main()
